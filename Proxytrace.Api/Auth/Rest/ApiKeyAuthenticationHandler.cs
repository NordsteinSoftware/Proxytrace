using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Proxytrace.Api.Auth.Mcp;
using Proxytrace.Domain.ApiKey;

namespace Proxytrace.Api.Auth.Rest;

/// <summary>
/// Authenticates REST (<c>/api/*</c>) requests with a Proxytrace API key (<see cref="IApiKey"/>)
/// presented as an <c>Authorization: Bearer &lt;key&gt;</c> header, so a machine caller can drive the
/// API with a scoped credential instead of a long-lived service-user JWT. The key acts as its
/// <see cref="IApiKey.Owner"/> — the handler stashes the owner id so <c>ICurrentUserAccessor</c>
/// resolves them exactly as a JWT request would.
///
/// Least privilege: only keys carrying a REST scope (<see cref="ApiKeyScopes.ApiRead"/> or
/// <see cref="ApiKeyScopes.ApiWrite"/>) authenticate here; the read/write split is enforced per HTTP
/// method by <see cref="ApiKeyScopeHandler"/>. This scheme is added to the default authorization policy
/// alongside JwtBearer, so it never grants access to admin (<c>[Authorize(Roles = Admin)]</c>) endpoints
/// — an API key carries no role claim.
/// </summary>
internal sealed class ApiKeyAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "ApiKey";

    /// <summary>Claim type carrying each granted REST scope (<c>ApiRead</c>/<c>ApiWrite</c>).</summary>
    public const string ScopeClaimType = "proxytrace:api_scope";

    /// <summary>
    /// Key under which this handler stashes the key's own project id on <see cref="HttpContext.Items"/>,
    /// so <c>ProjectAccessGuard</c> can confine the request to it. The analogue of
    /// <c>McpProjectAccessor.ProjectIdItemKey</c>.
    /// </summary>
    internal const string ProjectIdItemKey = "Proxytrace.ApiKey.ProjectId";

    // Proxytrace-issued keys are the only bearer tokens this scheme handles. Anything else on the
    // Authorization header (an OIDC/local JWT) is left to the JwtBearer handler, and — crucially —
    // skipped before any database lookup, so this scheme adds no per-request cost to JWT traffic.
    private const string KeyPrefix = "proxytrace-";
    private const string BearerPrefix = "Bearer ";

    private readonly IApiKeyRepository apiKeys;

    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IApiKeyRepository apiKeys)
        : base(options, logger, encoder)
    {
        this.apiKeys = apiKeys;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        string? header = Request.Headers.Authorization;
        if (string.IsNullOrWhiteSpace(header) ||
            !header.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return AuthenticateResult.NoResult();
        }

        string rawKey = header[BearerPrefix.Length..].Trim();
        // Only Proxytrace-issued keys are ours; a JWT bearer falls through to JwtBearer with no DB hit.
        if (string.IsNullOrEmpty(rawKey) || !rawKey.StartsWith(KeyPrefix, StringComparison.Ordinal))
        {
            return AuthenticateResult.NoResult();
        }

        IApiKey? apiKey = await apiKeys.FindByKeyAsync(rawKey, Context.RequestAborted);
        if (apiKey is null)
        {
            return AuthenticateResult.Fail("Unknown API key.");
        }

        // Least privilege: a key must carry a REST scope to reach the API at all. Ingestion-only and
        // MCP-only keys are rejected here — keys are not interchangeable across surfaces.
        bool canRead = apiKey.Scopes.HasFlag(ApiKeyScopes.ApiRead);
        bool canWrite = apiKey.Scopes.HasFlag(ApiKeyScopes.ApiWrite);
        if (!canRead && !canWrite)
        {
            return AuthenticateResult.Fail("This API key is not authorized for the REST API.");
        }

        // Attribute the call to the key's owner: stashing their id makes ICurrentUserAccessor resolve
        // the owner inside the controllers, exactly as a JWT-authenticated request would. Also stash the
        // key id so audit capture can attribute the action to the specific key (mirrors the MCP handler,
        // and reuses the same scheme-neutral item key the audit actor accessor already reads).
        Context.Items[CurrentUserAccessor.UserIdItemKey] = apiKey.Owner.Id;
        Context.Items[McpApiKeyAuthenticationHandler.ApiKeyIdItemKey] = apiKey.Id;

        // Confine the request to the key's own project. This MUST be an HttpContext.Items entry that
        // ProjectAccessGuard reads, not just a claim: the key acts as its owner, and because
        // POST /api/providers/{id}/keys is admin-only the owner is typically an Admin — whose role
        // alone would otherwise let the guard wave through every project in the instance. The
        // manual's contract for the REST scopes is "read/write *the key's project*"
        // (manual/admin/providers-and-api-keys.md), which is exactly what this pins.
        Context.Items[ProjectIdItemKey] = apiKey.Project.Id;

        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, apiKey.Name),
        };
        // Emit a claim per granted REST scope; ApiKeyScopeHandler reads these to enforce read vs write.
        if (canRead) claims.Add(new Claim(ScopeClaimType, nameof(ApiKeyScopes.ApiRead)));
        if (canWrite) claims.Add(new Claim(ScopeClaimType, nameof(ApiKeyScopes.ApiWrite)));

        var identity = new ClaimsIdentity(claims, SchemeName);
        return AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
    }
}
