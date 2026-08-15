using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Proxytrace.Domain.CustomAnomaly;
using Proxytrace.Licensing;

namespace Proxytrace.Proxy.Internal;

/// <summary>
/// TTL-cached blocking-rule lookup. Rules may be cached where credentials must not
/// (<see cref="ApiKeyResolver"/> is deliberately uncached, #407): a stale rule list delays a
/// detector edit by one TTL, not a security-sensitive key rotation. It also caches EMPTY lists:
/// most projects have no blocking detectors, and without negative caching every proxied request
/// would hit the database. Detector edits therefore
/// propagate within one TTL (default 30 s). A database error is fail-open — log and return no
/// rules (uncached, so recovery is immediate) rather than failing the LLM call. Blocking is
/// Enterprise-gated: without <see cref="LicenseFeature.CustomAnomalyDetectors"/> no rules apply.
/// </summary>
internal sealed class CachedBlockingRuleProvider : IBlockingRuleProvider
{
    private readonly ICustomAnomalyDetectorRepository detectors;
    private readonly ILicenseService license;
    private readonly IMemoryCache cache;
    private readonly TimeSpan ttl;
    private readonly ILogger<CachedBlockingRuleProvider> logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="CachedBlockingRuleProvider"/> class.
    /// </summary>
    public CachedBlockingRuleProvider(
        ICustomAnomalyDetectorRepository detectors,
        ILicenseService license,
        IMemoryCache cache,
        TimeSpan ttl,
        ILogger<CachedBlockingRuleProvider> logger)
    {
        this.detectors = detectors;
        this.license = license;
        this.cache = cache;
        this.ttl = ttl;
        this.logger = logger;
    }

    /// <summary>
    /// Gets the rules asynchronously.
    /// </summary>
    public async Task<IReadOnlyList<BlockingDetectorRule>> GetRulesAsync(
        Guid projectId,
        CancellationToken cancellationToken)
    {
        if (!license.IsFeatureEnabled(LicenseFeature.CustomAnomalyDetectors))
        {
            return [];
        }

        var cacheKey = $"blockrules:{projectId}";
        if (cache.TryGetValue(cacheKey, out IReadOnlyList<BlockingDetectorRule>? cached) && cached is not null)
        {
            return cached;
        }

        IReadOnlyList<BlockingDetectorRule> rules;
        try
        {
            rules = await detectors.GetEnabledBlockingRulesByProjectAsync(projectId, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex, "Failed to load blocking detector rules for project {ProjectId}; failing open (no blocking)",
                projectId);
            return [];
        }

        if (ttl > TimeSpan.Zero)
        {
            cache.Set(cacheKey, rules, ttl);
        }

        return rules;
    }
}
