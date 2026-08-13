using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Nordstein.Core.Common.Time;
using Proxytrace.Domain.CostLimit;
using Proxytrace.Domain.CostLimitBreach;
using Proxytrace.Licensing;

namespace Proxytrace.Proxy.Internal;

/// <summary>
/// TTL-cached lookup of a project's active monthly-budget hard blocks, mirroring
/// <see cref="CachedBlockingRuleProvider"/>. Blocks may be cached where credentials must not
/// (<see cref="ApiKeyResolver"/> is deliberately uncached, #407): a stale block list delays a
/// budget change by one TTL, not a security-sensitive key rotation. It also caches EMPTY lists —
/// most projects have no budget at all, and without negative caching every proxied request would
/// hit the database.
/// </summary>
/// <remarks>
/// The cache TTL adds to the guard's recompute interval, so the two together bound how far spend
/// can overshoot a hard limit before calls actually stop. Raising the budget (or disabling the
/// limit) likewise takes effect within one TTL. A database error is fail-open — log and return no
/// blocks, uncached, so recovery is immediate — because a budget is a cost control, not a security
/// control, and failing closed would take an organisation's LLM traffic down on a transient
/// database blip. Enforcement is Enterprise-gated: without
/// <see cref="LicenseFeature.CostControls"/> nothing blocks.
/// </remarks>
internal sealed class CachedBudgetBlockProvider : IBudgetBlockProvider
{
    private readonly ICostLimitBreachRepository breaches;
    private readonly ILicenseService license;
    private readonly IMemoryCache cache;
    private readonly IClock clock;
    private readonly TimeSpan ttl;
    private readonly ILogger<CachedBudgetBlockProvider> logger;

    public CachedBudgetBlockProvider(
        ICostLimitBreachRepository breaches,
        ILicenseService license,
        IMemoryCache cache,
        IClock clock,
        TimeSpan ttl,
        ILogger<CachedBudgetBlockProvider> logger)
    {
        this.breaches = breaches;
        this.license = license;
        this.cache = cache;
        this.clock = clock;
        this.ttl = ttl;
        this.logger = logger;
    }

    public async Task<IReadOnlyList<BudgetHardBlock>> GetBlocksAsync(
        Guid projectId,
        CancellationToken cancellationToken)
    {
        if (!license.IsFeatureEnabled(LicenseFeature.CostControls))
        {
            return [];
        }

        DateTimeOffset monthStart = CostMonth.StartOf(clock.UtcNow);

        // The month is part of the key, so the first request after a month rollover misses and
        // re-reads instead of serving last month's blocks for the rest of the TTL.
        var cacheKey = $"budgetblocks:{projectId}:{monthStart:yyyyMM}";
        if (cache.TryGetValue(cacheKey, out IReadOnlyList<BudgetHardBlock>? cached) && cached is not null)
        {
            return cached;
        }

        IReadOnlyList<BudgetHardBlock> blocks;
        try
        {
            blocks = await breaches.GetActiveHardBlocksAsync(projectId, monthStart, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex, "Failed to load budget blocks for project {ProjectId}; failing open (no blocking)",
                projectId);
            return [];
        }

        if (ttl > TimeSpan.Zero)
        {
            cache.Set(cacheKey, blocks, ttl);
        }

        return blocks;
    }
}
