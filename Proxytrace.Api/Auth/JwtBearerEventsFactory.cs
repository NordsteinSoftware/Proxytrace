using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Proxytrace.Application.Auth;
using Proxytrace.Domain;
using Proxytrace.Domain.User;

namespace Proxytrace.Api.Auth;

internal static class JwtBearerEventsFactory
{
    private const string StreamTicketQueryKey = "stream_ticket";

    /// <summary>
    /// Builds the <see cref="JwtBearerEvents"/> that handle stream-ticket redemption, the SSE
    /// <c>?access_token</c> fallback, the httpOnly session-cookie fallback, and live role-claim
    /// overwriting so a demoted user loses privileges on their next request rather than at token expiry.
    /// </summary>
    public static JwtBearerEvents Create() => new()
    {
        OnMessageReceived = async context =>
        {
            // SSE connections authenticate with a short-lived, single-use ticket instead of
            // the session JWT (which would leak via the EventSource query string). Redeem it
            // here and authenticate the request directly — works in both local and OIDC mode.
            // Fenced to stream routes for the same reason as the access_token fallback below: the
            // ticket redeems into a FULL principal (including the live Role claim), so honoring it on
            // any path would let a URL-borne credential — the very thing the ticket exists to contain
            // — reach every endpoint its owner can, admin ones included, inside its validity window.
            var streamTicket = context.Request.Query[StreamTicketQueryKey].ToString();
            if (!string.IsNullOrEmpty(streamTicket) && IsStreamRequest(context.Request))
            {
                await AuthenticateStreamTicket(context, streamTicket);
                return;
            }

            // Fallback for SSE when the stream-ticket endpoint is unreachable: the EventSource API
            // can't set headers, so the client passes the session JWT as ?access_token. Accept it
            // ONLY on stream (GET …/stream) routes — honoring a URL-borne session token on every
            // request would re-introduce the token-in-URL leakage the stream ticket exists to avoid.
            if (string.IsNullOrEmpty(context.Token) && IsStreamRequest(context.Request))
            {
                var queryToken = context.Request.Query["access_token"].ToString();
                if (!string.IsNullOrEmpty(queryToken))
                {
                    context.Token = queryToken;
                }
            }

            // Browser sessions carry the JWT in the httpOnly session cookie (local mode).
            // An explicit Authorization header must win, but the handler only parses it
            // *after* this event when Token is still null — so the cookie fallback has to
            // step aside whenever a header is present, not just when Token is set.
            if (string.IsNullOrEmpty(context.Token) &&
                string.IsNullOrEmpty(context.Request.Headers.Authorization))
            {
                var cookieToken = context.Request.Cookies[SessionCookie.Name];
                if (!string.IsNullOrEmpty(cookieToken))
                {
                    context.Token = cookieToken;
                }
            }
        },
        OnTokenValidated = async context =>
        {
            var principal = context.Principal;
            if (principal is null)
            {
                context.Fail("Missing principal.");
                return;
            }

            var resolver = context.HttpContext.RequestServices.GetRequiredService<IAuthUserResolver>();
            var user = await resolver.Resolve(context, principal);
            if (user is null)
            {
                return;
            }

            context.HttpContext.Items[CurrentUserAccessor.UserIdItemKey] = user.Id;

            var identity = (ClaimsIdentity?)principal.Identity;
            if (identity != null)
            {
                // Authorization is role-claim based ([Authorize(Roles = …)]), but the JWT is a
                // long-lived (7-day) stateless token that bakes the role at issue time. Trust the
                // LIVE DB role resolved above, not the stale token claim: overwrite any role claim
                // the token carries so a demoted user loses privileges on their next request rather
                // than retaining them until the token expires. Deleted users are already rejected by
                // the resolver; this closes the same gap for role changes.
                foreach (var staleRole in identity.FindAll(ClaimTypes.Role).ToList())
                {
                    identity.RemoveClaim(staleRole);
                }

                identity.AddClaim(new Claim(ClaimTypes.Role, user.Role.ToString()));
            }

            // Normalize the resolved user's email onto the "email" claim so downstream consumers
            // (e.g. audit actor enrichment) find it regardless of OIDC's configurable EmailClaimType
            // (with MapInboundClaims=false the raw token may carry it under upn/preferred_username/…).
            if (identity != null && !identity.HasClaim(c => c.Type == "email" || c.Type == ClaimTypes.Email))
            {
                identity.AddClaim(new Claim("email", user.Email));
            }
        },
    };

    private static bool IsStreamRequest(HttpRequest request)
        => HttpMethods.IsGet(request.Method)
           && request.Path.Value?.EndsWith("/stream", StringComparison.Ordinal) == true;

    private static async Task AuthenticateStreamTicket(MessageReceivedContext context, string ticket)
    {
        var services = context.HttpContext.RequestServices;
        var userId = services.GetRequiredService<IStreamTicketService>().Consume(ticket);
        if (userId is null)
        {
            context.Fail("Invalid or expired stream ticket.");
            return;
        }

        var user = await services.GetRequiredService<IRepository<IUser>>()
            .FindAsync(userId.Value, context.HttpContext.RequestAborted);
        if (user is null)
        {
            context.Fail("Unknown user.");
            return;
        }

        context.HttpContext.Items[CurrentUserAccessor.UserIdItemKey] = user.Id;

        var identity = new ClaimsIdentity(JwtBearerDefaults.AuthenticationScheme);
        identity.AddClaim(new Claim("sub", user.Id.ToString()));
        identity.AddClaim(new Claim(ClaimTypes.Role, user.Role.ToString()));
        identity.AddClaim(new Claim("email", user.Email));
        context.Principal = new ClaimsPrincipal(identity);
        context.Success();
    }
}
