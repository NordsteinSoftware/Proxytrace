using Proxytrace.Common.Time;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.ApiKey;
using Proxytrace.Domain.CostLimit;
using Proxytrace.Domain.CostLimitBreach;
using Proxytrace.Domain.Statistics;

namespace Proxytrace.Application.CostControl.Internal;

internal class CostStatistics : ICostStatistics
{
    /// <summary>
    /// Buckets the Costs chart renders, mirroring <c>MAX_BUCKETS</c> in
    /// <c>frontend/src/features/costs/costSeries.ts</c>. A finer request over a wide window is
    /// coarsened to this rather than aggregated, serialized and transferred for the client to throw
    /// away — a month at the 5-minute bucket is 8,640 cells to draw 400 bars.
    /// </summary>
    internal const int MaxSeriesBuckets = 400;

    private readonly IAgentCallStatsReader callStats;
    private readonly ICostLimitRepository costLimits;
    private readonly ICostLimitBreachRepository breaches;
    private readonly IAgentRepository agents;
    private readonly IApiKeyRepository apiKeys;
    private readonly IClock clock;

    public CostStatistics(
        IAgentCallStatsReader callStats,
        ICostLimitRepository costLimits,
        ICostLimitBreachRepository breaches,
        IAgentRepository agents,
        IApiKeyRepository apiKeys,
        IClock clock)
    {
        this.callStats = callStats;
        this.costLimits = costLimits;
        this.breaches = breaches;
        this.agents = agents;
        this.apiKeys = apiKeys;
        this.clock = clock;
    }

    public Task<IReadOnlyList<ProjectAgentCostStat>> GetMonthToDateSpendAsync(
        DateTimeOffset monthStart,
        CancellationToken cancellationToken = default)
        => callStats.GetCostByProjectAndAgentAsync(
            new StatisticsFilter(From: monthStart),
            cancellationToken);

    public Task<IReadOnlyList<ProjectApiKeyCostStat>> GetMonthToDateSpendByApiKeyAsync(
        DateTimeOffset monthStart,
        CancellationToken cancellationToken = default)
        => callStats.GetCostByApiKeyAsync(
            new StatisticsFilter(From: monthStart),
            cancellationToken);

    public async Task<CostOverview> GetCostOverviewAsync(
        Guid projectId,
        DateTimeOffset from,
        DateTimeOffset to,
        StatisticsBucket bucket,
        CancellationToken cancellationToken = default)
    {
        DateTimeOffset monthStart = CostMonth.StartOf(clock.UtcNow);
        DateTimeOffset previousMonthStart = monthStart.AddMonths(-1);

        // Never aggregate finer than the chart draws. The bucket is persisted client-side, so a
        // user who once picked "5 minutes" keeps it across every later window they open.
        StatisticsBucket effectiveBucket = bucket.CoarsenToFit(from, to, MaxSeriesBuckets);

        var windowFilter = new StatisticsFilter(From: from, To: to, ProjectId: projectId);

        Task<IReadOnlyList<AgentCostPoint>> seriesTask =
            callStats.GetCostSeriesByAgentAsync(windowFilter, effectiveBucket, cancellationToken);
        Task<IReadOnlyList<ProjectAgentCostStat>> windowTotalsTask =
            callStats.GetCostByProjectAndAgentAsync(windowFilter, cancellationToken);
        Task<IReadOnlyList<ProjectAgentCostStat>> monthTask =
            callStats.GetCostByProjectAndAgentAsync(
                new StatisticsFilter(From: monthStart, ProjectId: projectId), cancellationToken);
        Task<IReadOnlyList<ProjectAgentCostStat>> previousMonthTask =
            callStats.GetCostByProjectAndAgentAsync(
                new StatisticsFilter(From: previousMonthStart, To: monthStart.AddTicks(-1), ProjectId: projectId),
                cancellationToken);
        Task<IReadOnlyList<ApiKeyCostPoint>> keySeriesTask =
            callStats.GetCostSeriesByApiKeyAsync(windowFilter, effectiveBucket, cancellationToken);
        Task<IReadOnlyList<ProjectApiKeyCostStat>> keyWindowTotalsTask =
            callStats.GetCostByApiKeyAsync(windowFilter, cancellationToken);
        Task<bool> unpricedTask = callStats.HasUnpricedEndpointsAsync(windowFilter, cancellationToken);
        Task<IReadOnlyList<IAgent>> agentsTask = agents.GetByProjectAsync(projectId, cancellationToken);
        Task<IReadOnlyList<IApiKey>> keysTask = apiKeys.GetByProjectAsync(projectId, cancellationToken);

        // Safe to fan out ONLY because this is a read path with no transaction open: with no
        // ambient context each repository call resolves its own StorageDbContext. Wrapping this
        // method in a transaction would make them share one and turn this into
        // "A second operation was started on this context instance" — keep it transaction-free.
        await Task.WhenAll(
            seriesTask, windowTotalsTask, monthTask, previousMonthTask,
            keySeriesTask, keyWindowTotalsTask,
            unpricedTask, agentsTask, keysTask);

        IReadOnlyList<AgentCostPoint> series = await seriesTask;
        IReadOnlyList<ProjectAgentCostStat> windowTotals = await windowTotalsTask;
        IReadOnlyList<ProjectAgentCostStat> monthTotals = await monthTask;
        IReadOnlyList<ProjectAgentCostStat> previousMonthTotals = await previousMonthTask;
        IReadOnlyList<ApiKeyCostPoint> keySeries = await keySeriesTask;
        IReadOnlyList<ProjectApiKeyCostStat> keyWindowTotals = await keyWindowTotalsTask;
        bool hasUnpriced = await unpricedTask;
        IReadOnlyList<IAgent> projectAgents = await agentsTask;
        IReadOnlyList<IApiKey> projectKeys = await keysTask;

        Dictionary<Guid, string> agentNames = projectAgents
            .GroupBy(a => a.Id)
            .ToDictionary(g => g.Key, g => g.First().Name);

        Dictionary<Guid, IApiKey> keysById = projectKeys
            .GroupBy(k => k.Id)
            .ToDictionary(g => g.Key, g => g.First());

        ApiKeyCostTotal[] keyTotals = keyWindowTotals
            .GroupBy(s => s.ApiKeyId)
            .Select(g => new ApiKeyCostTotal(
                ApiKeyId: g.Key,
                // A revoked key still owns historical spend; fall back to the id rather than
                // dropping the row and silently under-reporting the total. The null group keeps
                // null name/prefix — the page labels it as unattributed.
                ApiKeyName: g.Key is { } id
                    ? keysById.TryGetValue(id, out var k) ? k.Name : id.ToString()
                    : null,
                KeyPrefix: g.Key is { } prefixId && keysById.TryGetValue(prefixId, out var pk)
                    ? pk.KeyPrefix
                    : null,
                CostEur: g.Sum(s => s.CostEur)))
            // Attributed keys by spend, with the unattributed remainder pinned last.
            .OrderBy(t => t.ApiKeyId is null ? 1 : 0)
            .ThenByDescending(t => t.CostEur)
            .ToArray();

        AgentCostTotal[] agentTotals = windowTotals
            .GroupBy(s => s.AgentId)
            .Select(g => new AgentCostTotal(
                AgentId: g.Key,
                // An archived or since-deleted agent still owns historical spend; fall back to the
                // id rather than dropping the row and silently under-reporting the total.
                AgentName: agentNames.TryGetValue(g.Key, out var name) ? name : g.Key.ToString(),
                CostEur: g.Sum(s => s.CostEur)))
            .OrderByDescending(t => t.CostEur)
            .ToArray();

        return new CostOverview(
            MonthToDateSpendEur: monthTotals.Sum(s => s.CostEur),
            PreviousMonthSpendEur: previousMonthTotals.Sum(s => s.CostEur),
            Series: series,
            AgentTotals: agentTotals,
            ApiKeySeries: keySeries,
            ApiKeyTotals: keyTotals,
            HasUnpricedEndpoints: hasUnpriced,
            Bucket: effectiveBucket);
    }

    public async Task<IReadOnlyList<CostBudgetStatus>> GetBudgetStatusAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ICostLimit> limits = await costLimits.GetByProjectAsync(projectId, cancellationToken);

        // The same fast path the guard takes: an install with no budgets never touches the trace
        // table for this at all.
        if (limits.Count == 0)
            return [];

        DateTimeOffset monthStart = CostMonth.StartOf(clock.UtcNow);
        var monthFilter = new StatisticsFilter(From: monthStart, ProjectId: projectId);

        Task<IReadOnlyList<ProjectAgentCostStat>> monthTask =
            callStats.GetCostByProjectAndAgentAsync(monthFilter, cancellationToken);

        // Only pay for the per-key aggregate when a key-scoped budget exists — a second scan of the
        // highest-volume table, and most projects configure none. Mirrors CostBudgetGuard.
        Task<IReadOnlyList<ProjectApiKeyCostStat>> keyMonthTask = limits.Any(l => l.ApiKey is not null)
            ? callStats.GetCostByApiKeyAsync(monthFilter, cancellationToken)
            : Task.FromResult<IReadOnlyList<ProjectApiKeyCostStat>>([]);

        // Project-scoped: an unscoped read would pull every other tenant's threshold crossings.
        Task<IReadOnlyList<FiredThreshold>> breachTask =
            breaches.GetFiredThresholdsAsync(monthStart, projectId, cancellationToken);

        // Transaction-free fan-out, same constraint as GetCostOverviewAsync above.
        await Task.WhenAll(monthTask, keyMonthTask, breachTask);

        IReadOnlyList<ProjectAgentCostStat> monthTotals = await monthTask;
        IReadOnlyList<ProjectApiKeyCostStat> keyMonthTotals = await keyMonthTask;
        IReadOnlyList<FiredThreshold> monthBreaches = await breachTask;

        decimal monthToDate = monthTotals.Sum(s => s.CostEur);

        Dictionary<Guid, decimal> monthByAgent = monthTotals
            .GroupBy(s => s.AgentId)
            .ToDictionary(g => g.Key, g => g.Sum(s => s.CostEur));

        // Only attributed rows are keyed; the null group is the unattributed remainder and belongs
        // to no key's budget.
        Dictionary<Guid, decimal> monthByKey = keyMonthTotals
            .Where(s => s.ApiKeyId is not null)
            .GroupBy(s => s.ApiKeyId ?? Guid.Empty)
            .ToDictionary(g => g.Key, g => g.Sum(s => s.CostEur));

        var breachSet = monthBreaches
            .Select(b => (b.CostLimitId, b.Threshold))
            .ToHashSet();

        return limits
            .Select(limit => new CostBudgetStatus(
                CostLimitId: limit.Id,
                AgentId: limit.Agent?.Id,
                AgentName: limit.Agent?.Name,
                ApiKeyId: limit.ApiKey?.Id,
                ApiKeyName: limit.ApiKey?.Name,
                SoftLimitEur: limit.SoftLimitEur,
                HardLimitEur: limit.HardLimitEur,
                Enabled: limit.Enabled,
                MonthToDateSpendEur: (limit.Agent, limit.ApiKey) switch
                {
                    ({ } agent, _) => monthByAgent.GetValueOrDefault(agent.Id),
                    (_, { } key) => monthByKey.GetValueOrDefault(key.Id),
                    _ => monthToDate,
                },
                SoftBreached: breachSet.Contains((limit.Id, CostThreshold.Soft)),
                HardBreached: breachSet.Contains((limit.Id, CostThreshold.Hard))))
            // Project-wide budget first, then agent overrides, then key overrides — each
            // alphabetically within its group.
            .OrderBy(b => b switch { { AgentId: null, ApiKeyId: null } => 0, { AgentId: not null } => 1, _ => 2 })
            .ThenBy(b => b.AgentName ?? b.ApiKeyName)
            .ToArray();
    }
}
