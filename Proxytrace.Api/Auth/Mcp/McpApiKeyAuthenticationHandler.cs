using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Proxytrace.Api.Mcp;
using Proxytrace.Domain.ApiKey;

namespace Proxytrace.Api.Auth.Mcp;

/// <summary>
/// Authenticates MCP requests with a Proxytrace API key (<see cref="IApiKey"/>) presented as an
/// <c>Authorization: Bearer &lt;key&gt;</c> header. The key's project is stashed on the request so
/// <see cref="IMcpProjectAccessor"/> can scope every tool invocation to it. This scheme is pinned to
/// the <c>/mcp</c> endpoint via the "Mcp" authorization policy and never touches the default JWT scheme.
/// </summary>
internal sealed class McpApiKeyAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    /// <summary>
    /// The scheme name constant value.
    /// </summary>
    public const string SchemeName = "McpApiKey";

    /// <summary>Request-item key under which the authenticated API key's id is stashed (for audit attribution).</summary>
    public const string ApiKeyIdItemKey = "Proxytrace.ApiKeyId";

    private const string BearerPrefix = "Bearer ";

    private readonly IApiKeyRepository apiKeys;

    /// <summary>
    /// Initializes a new instance of the <see cref="McpApiKeyAuthenticationHandler"/> class.
    /// </summary>
    public McpApiKeyAuthenticationHandler(
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
        if (string.IsNullOrEmpty(rawKey))
        {
            return AuthenticateResult.NoResult();
        }

        IApiKey? apiKey = await apiKeys.FindByKeyAsync(rawKey, Context.RequestAborted);
        if (apiKey is null)
        {
            return AuthenticateResult.Fail("Unknown API key.");
        }

        // Least privilege: only keys granted the McpRead scope may reach the MCP server at all.
        // Ingestion-only keys are rejected here (they can proxy LLM traffic, not drive MCP).
        if (!apiKey.Scopes.HasFlag(ApiKeyScopes.McpRead))
        {
            return AuthenticateResult.Fail("This API key is not authorized for the MCP server.");
        }

        // The MCP call runs in the context of the project the key belongs to. Stash the project id and
        // the granted scopes for IMcpProjectAccessor to resolve inside the tools (mirrors
        // CurrentUserAccessor's UserIdItemKey).
        Context.Items[McpProjectAccessor.ProjectIdItemKey] = apiKey.Project.Id;
        Context.Items[McpProjectAccessor.ScopesItemKey] = apiKey.Scopes;

        // Attribute the call to the key's owner: stashing their id makes ICurrentUserAccessor resolve
        // the owner inside the tools, exactly as a JWT-authenticated request would.
        Context.Items[CurrentUserAccessor.UserIdItemKey] = apiKey.Owner.Id;

        // Also stash the key id so audit capture can attribute the action to the specific API key.
        Context.Items[ApiKeyIdItemKey] = apiKey.Id;

        var claims = new[]
        {
            new Claim(ClaimTypes.Name, apiKey.Name),
            new Claim("project", apiKey.Project.Id.ToString()),
        };
        var identity = new ClaimsIdentity(claims, SchemeName);
        return AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
    }
}
