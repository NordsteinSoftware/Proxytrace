using System.Security.Claims;
using Proxytrace.Api.Auth.Mcp;
using Proxytrace.Api.Auth.Rest;
using Proxytrace.Application.AuditLog;
using Proxytrace.Domain.AuditLog;

namespace Proxytrace.Api.Auth;

/// <summary>
/// Resolves the current <see cref="AuditActor"/> from the HTTP request context for audit capture,
/// synchronously and without any DB access. Reads the user id stashed by the auth handlers
/// (<see cref="CurrentUserAccessor.UserIdItemKey"/>), detects API-key callers by their authentication
/// scheme, and best-effort reads the actor email from claims. With no request context (background work)
/// it returns <see cref="AuditActor.System"/>.
/// </summary>
internal sealed class HttpContextAuditActorAccessor : IAuditActorAccessor
{
    private readonly IHttpContextAccessor httpContextAccessor;

    /// <summary>
    /// Initializes a new instance of the <see cref="HttpContextAuditActorAccessor"/> class.
    /// </summary>
    public HttpContextAuditActorAccessor(IHttpContextAccessor httpContextAccessor)
    {
        this.httpContextAccessor = httpContextAccessor;
    }

    /// <summary>
    /// Gets the current actor.
    /// </summary>
    public AuditActor GetCurrentActor()
    {
        var http = httpContextAccessor.HttpContext;
        if (http is null)
        {
            return AuditActor.System;
        }

        Guid? userId = http.Items.TryGetValue(CurrentUserAccessor.UserIdItemKey, out var rawUserId) && rawUserId is Guid id
            ? id
            : null;

        // Both API-key schemes (MCP and REST) attribute to AuditActorType.ApiKey.
        var authType = http.User.Identity?.AuthenticationType;
        var isApiKey = string.Equals(authType, McpApiKeyAuthenticationHandler.SchemeName, StringComparison.Ordinal)
            || string.Equals(authType, ApiKeyAuthenticationHandler.SchemeName, StringComparison.Ordinal);

        // No resolved user and not an API-key caller (anonymous or pre-auth) — attribute to the System.
        if (userId is null && !isApiKey)
        {
            return AuditActor.System;
        }

        Guid? apiKeyId = http.Items.TryGetValue(McpApiKeyAuthenticationHandler.ApiKeyIdItemKey, out var rawKeyId) && rawKeyId is Guid keyId
            ? keyId
            : null;

        // Best-effort, claim-only email (no DB hit). API-key requests carry no email claim — null is fine,
        // the owner id is still captured.
        var email = http.User.FindFirst(ClaimTypes.Email)?.Value ?? http.User.FindFirst("email")?.Value;

        var type = isApiKey ? AuditActorType.ApiKey : AuditActorType.User;
        return new AuditActor(type, userId, email, apiKeyId);
    }
}
