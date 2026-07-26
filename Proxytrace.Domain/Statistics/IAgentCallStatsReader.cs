namespace Proxytrace.Domain.Statistics;

/// <summary>
/// Read-only aggregations over <c>AgentCallEntity</c> rows.
/// Insert-only call data aggregates cheaply at read time, so no projection table is required.
/// Consumed by <see cref="IDashboardStatistics"/> and <see cref="IAgentStatistics"/>; not part of the public read API.
/// </summary>
public interface IAgentCallStatsReader
{
    Task<StatisticsSummary> GetSummaryAsync(StatisticsFilter filter, CancellationToken cancellationToken = default);

    /// <summary>Timestamp of the earliest matching call, or <c>null</c> when the filter matches nothing.</summary>
    Task<DateTimeOffset?> GetEarliestCallAsync(StatisticsFilter filter, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TokenUsageStat>> GetTokenUsageAsync(StatisticsFilter filter, StatisticsBucket bucket, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LatencyStat>> GetLatencyAsync(StatisticsFilter filter, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ErrorRateStat>> GetErrorRatesAsync(StatisticsFilter filter, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ModelBreakdownStat>> GetModelBreakdownAsync(StatisticsFilter filter, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AgentBreakdownStat>> GetAgentBreakdownAsync(StatisticsFilter filter, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CostEstimateStat>> GetCostEstimateAsync(StatisticsFilter filter, CancellationToken cancellationToken = default);

    /// <summary>
    /// Derived spend grouped by (project, agent) over the filtered window. The existing
    /// <see cref="GetCostEstimateAsync"/> answers a different question (per-endpoint totals for one
    /// filter), so budget evaluation — which must see every project in one pass — needs its own
    /// aggregate. Cost stays derived: tokens are summed per (project, agent, endpoint) in SQL and
    /// priced in C#, so a price change reprices history.
    /// </summary>
    Task<IReadOnlyList<ProjectAgentCostStat>> GetCostByProjectAndAgentAsync(StatisticsFilter filter, CancellationToken cancellationToken = default);

    /// <summary>
    /// Derived spend per (bucket, agent) over the filtered window — the Costs page's
    /// cost-over-time series.
    /// </summary>
    Task<IReadOnlyList<AgentCostPoint>> GetCostSeriesByAgentAsync(StatisticsFilter filter, StatisticsBucket bucket, CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether any call in the filtered window was served by an endpoint with no configured price.
    /// Those calls contribute nothing to derived spend, so budgets and the Costs page undercount —
    /// the flag lets the UI say so instead of presenting an incomplete estimate as exact.
    /// </summary>
    Task<bool> HasUnpricedEndpointsAsync(StatisticsFilter filter, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AgentTokenUsageStat>> GetTokenUsageByAgentAsync(StatisticsFilter filter, StatisticsBucket bucket, CancellationToken cancellationToken = default);

    /// <summary>
    /// Per-bucket, per-agent counts of flagged (outlier) calls, split into the statistical
    /// ingestion-time bits and the async custom-detector bit (a call with both kinds counts in both).
    /// Only rows with a non-zero <c>OutlierFlags</c> are scanned, so the query rides the partial
    /// outlier index instead of the whole table.
    /// </summary>
    Task<IReadOnlyList<AgentAnomalyStat>> GetAnomalyCountsByAgentAsync(StatisticsFilter filter, StatisticsBucket bucket, CancellationToken cancellationToken = default);

    Task<LiveTelemetry> GetLiveTelemetryAsync(StatisticsFilter filter, DateTimeOffset since, DateTimeOffset now, CancellationToken cancellationToken = default);

    Task<CallTrends> GetCallTrendsAsync(StatisticsFilter filter, int buckets, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);

    /// <summary>
    /// Per-bucket call counts over [from, to] as a dense array (oldest → newest, zeros for empty
    /// buckets). Drives the dashboard's live pulse band: 60 one-minute buckets over the trailing hour.
    /// </summary>
    Task<IReadOnlyList<int>> GetPulseAsync(StatisticsFilter filter, DateTimeOffset from, DateTimeOffset to, int buckets, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AgentTimeSeriesPoint>> GetAgentTimeSeriesAsync(Guid agentId, DateTimeOffset from, DateTimeOffset to, StatisticsBucket bucket, CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<AgentTimeSeriesPoint> Series, AgentTimeSummary Summary)> GetAgentWindowAsync(
        Guid agentId, DateTimeOffset from, DateTimeOffset to, StatisticsBucket bucket, CancellationToken cancellationToken = default);

    /// <summary>
    /// Distribution (mean ± std) of the agent's successful (2xx) calls over <paramref name="from"/>..<paramref name="to"/>.
    /// Token and latency metrics are per call; cost, cache-hit-rate and tool-call metrics are per conversation.
    /// </summary>
    Task<AgentCallDistributions> GetAgentDistributionsAsync(
        Guid agentId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
