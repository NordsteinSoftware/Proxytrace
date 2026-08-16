using Nordstein.Core.Common.Text;
using Proxytrace.Domain.ApiKey;
using Proxytrace.Domain.ModelProvider;
using Proxytrace.Domain.Project;

namespace Proxytrace.Proxy.Internal;

internal sealed class ApiKeyResolver : IApiKeyResolver
{
    private readonly IApiKeyRepository apiKeys;
    private readonly IModelProviderRepository providers;
    private readonly IProjectRepository projects;

    /// <summary>
    /// Initializes a new instance of the <see cref="ApiKeyResolver"/> class.
    /// </summary>
    public ApiKeyResolver(
        IApiKeyRepository apiKeys,
        IModelProviderRepository providers,
        IProjectRepository projects)
    {
        this.apiKeys = apiKeys;
        this.providers = providers;
        this.projects = projects;
    }

    // Deliberately resolves from storage on every request — no positive credential cache. A cached
    // ResolvedApiKey carries the decrypted upstream provider key, so any TTL becomes a window in
    // which a rotated key keeps being forwarded and a revoked inbound credential keeps
    // authenticating, independently per proxy replica (#407). Rotation and revocation must take
    // effect on the next request; when the database is unreachable the proxy fails closed instead
    // of serving stale credentials. The per-request cost is a few indexed point lookups (guarded by
    // the proxyResolve* perf budgets), negligible next to an upstream LLM round trip.
    /// <summary>
    /// Resolves asynchronously.
    /// </summary>
    public async Task<ResolvedApiKey?> ResolveAsync(string rawKey, string? projectSlug, CancellationToken cancellationToken)
    {
        // Proxytrace-issued key path wins on collisions. The key already carries its project, so the
        // path slug is optional — but when supplied it must agree, to avoid silently mis-attributing.
        IApiKey? apiKey = await apiKeys.FindByKeyAsync(rawKey, cancellationToken);
        if (apiKey is not null)
        {
            // A key without the Ingestion scope (e.g. an MCP-only key) must not authenticate at the
            // ingestion proxy — least privilege keeps an MCP credential from also proxying LLM traffic.
            if (!apiKey.Scopes.HasFlag(ApiKeyScopes.Ingestion))
            {
                return null;
            }

            // Slugify the path slug before comparing: the URL segment keeps its original casing,
            // so match canonical slug to canonical slug rather than rejecting "/Development".
            return !string.IsNullOrEmpty(projectSlug) && apiKey.Project.Name.ToSlug() != projectSlug.ToSlug()
                ? null
                : new ResolvedApiKey(apiKey.Project, apiKey.Provider, apiKey.Id, apiKey.Scopes);
        }

        // Upstream-provider-key path: caller authenticated with the provider's own credentials. The
        // bearer identifies only the provider, so the project must come from the request path.
        IModelProvider? provider = await providers.FindByApiKeyAsync(rawKey, cancellationToken);
        if (provider is null || string.IsNullOrEmpty(projectSlug))
        {
            return null;
        }

        IProject? project = await projects.FindBySlugAsync(projectSlug, cancellationToken);
        if (project is null)
        {
            return null;
        }

        return new ResolvedApiKey(project, provider);
    }
}
