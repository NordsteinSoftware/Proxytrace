using Microsoft.AspNetCore.Authorization;

namespace Proxytrace.Api.Auth.Rest;

/// <summary>
/// Authorization requirement added to the default policy so that a request authenticated by the
/// <see cref="ApiKeyAuthenticationHandler"/> is held to the REST scope its key was granted. It places
/// <b>no</b> constraint on JWT/cookie (interactive) callers — those are governed by roles as before.
/// </summary>
internal sealed class ApiKeyScopeRequirement : IAuthorizationRequirement;

/// <summary>
/// Enforces the REST read/write split for API-key callers: safe requests
/// (<c>GET</c>/<c>HEAD</c>/<c>OPTIONS</c>) require the <c>ApiRead</c> scope, mutating requests
/// (<c>POST</c>/<c>PUT</c>/<c>PATCH</c>/<c>DELETE</c>) require <c>ApiWrite</c>. A caller that is not an
/// API key satisfies the requirement unconditionally, so ordinary user sessions are unaffected.
/// </summary>
internal sealed class ApiKeyScopeHandler : AuthorizationHandler<ApiKeyScopeRequirement>
{
    private readonly IHttpContextAccessor httpContextAccessor;

    /// <summary>
    /// Initializes a new instance of the <see cref="ApiKeyScopeHandler"/> class.
    /// </summary>
    public ApiKeyScopeHandler(IHttpContextAccessor httpContextAccessor)
    {
        this.httpContextAccessor = httpContextAccessor;
    }

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ApiKeyScopeRequirement requirement)
    {
        // Only constrain API-key callers; anyone else (JWT/cookie/kiosk) passes untouched.
        var apiKeyIdentity = context.User.Identities.FirstOrDefault(
            i => string.Equals(i.AuthenticationType, ApiKeyAuthenticationHandler.SchemeName, StringComparison.Ordinal));
        if (apiKeyIdentity is null)
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        // Resource is the HttpContext under endpoint routing; fall back to the accessor for safety.
        var method = (context.Resource as HttpContext ?? httpContextAccessor.HttpContext)?.Request.Method;
        var required = IsSafeMethod(method)
            ? nameof(Domain.ApiKey.ApiKeyScopes.ApiRead)
            : nameof(Domain.ApiKey.ApiKeyScopes.ApiWrite);

        if (apiKeyIdentity.HasClaim(ApiKeyAuthenticationHandler.ScopeClaimType, required))
        {
            context.Succeed(requirement);
        }
        // Otherwise leave the requirement unmet — the API key lacks the scope this method needs.

        return Task.CompletedTask;
    }

    // Unknown method (null) is treated as unsafe so a missing scope never fails open to a write.
    private static bool IsSafeMethod(string? method)
        => method is not null
           && (HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method));
}
