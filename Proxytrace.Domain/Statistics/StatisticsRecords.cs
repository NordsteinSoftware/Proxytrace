using Proxytrace.Domain.Agent;
using Proxytrace.Domain.AgentCall;
using Proxytrace.Domain.Usage;

namespace Proxytrace.Domain.Statistics;

public record StatisticsFilter(
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    Guid? ProjectId = null,
    Guid? AgentId = null,
    Guid? EndpointId = null,
    // When true, drops calls attributed to system agents (the Tracey assistant, evaluators) from
    // every aggregate. Default false keeps project-wide totals. Used by the Tracey dashboard tool so
    // its usage figures are about the user's own agents, not the platform's own activity.
    bool ExcludeSystemAgents = false);

public record StatisticsSummary(
    long TotalCalls,
    long TotalInputTokens,
    long TotalOutputTokens,
    long TotalCachedInputTokens,
    double AvgLatencyMs,
    double? OverallPassRate);

public record TokenUsageStat(
    DateTimeOffset BucketStart,
    Guid EndpointId,
    long? InputTokens,
    long? OutputTokens,
    long? CachedInputTokens);

public record LatencyStat(
    Guid EndpointId,
    double P50Ms,
    double P95Ms,
    double P99Ms,
    double MinMs,
    double MaxMs,
    int SampleCount);

public record PassRateStat(
    Guid SuiteId,
    DateTimeOffset RunTimestamp,
    int PassCount,
    int FailCount);

public record ErrorRateStat(
    Guid EndpointId,
    int TotalCalls,
    int ErrorCalls,
    double ErrorRate);

public record ModelBreakdownStat(
    Guid EndpointId,
    string ModelName,
    int CallCount,
    long? TotalInputTokens,
    long? TotalOutputTokens,
    long? TotalCachedInputTokens,
    double? AvgDurationMs);

public record AgentBreakdownStat(
    Guid AgentId,
    int CallCount);

/// <summary>
/// Real-time proxy telemetry surfaced on the dashboard.
/// </summary>
public record LiveTelemetry(
    double TracesPerMinute,
    double TokensPerSecond,
    int QueueDepth,
    double ErrorRate,
    double P95Ms);

/// <summary>
/// Token usage attributed to a single agent for one time bucket (for the stacked token-by-agent chart).
/// </summary>
public record AgentTokenUsageStat(
    DateTimeOffset BucketStart,
    Guid AgentId,
    long InputTokens,
    long OutputTokens,
    long CachedInputTokens);

/// <summary>
/// Flagged (outlier) calls attributed to a single agent for one time bucket, split into the
/// statistical ingestion-time bits and the async custom-detector bit. A call carrying both kinds of
/// bits counts in both — the two counts answer "how many calls did each detector kind flag", not
/// "how many flagged calls exist".
/// </summary>
public record AgentAnomalyStat(
    DateTimeOffset BucketStart,
    Guid AgentId,
    int StaticCount,
    int CustomCount);

/// <summary>
/// Bucketed trend series powering the dashboard stat-tile sparklines.
/// </summary>
public record DashboardTrends(
    IReadOnlyList<double> Traces,
    IReadOnlyList<double> LatencyMs,
    IReadOnlyList<double> Throughput,
    IReadOnlyList<double> PassRate);

/// <summary>
/// Everything the dashboard ("mission control") page needs, composed into a single payload so the
/// client fetches it in one round trip instead of fanning out across the granular statistics endpoints.
/// </summary>
public record DashboardView(
    StatisticsSummary Summary,
    LiveTelemetry LiveTelemetry,
    DashboardTrends Trends,
    IReadOnlyList<AgentBreakdownStat> AgentBreakdown,
    IReadOnlyList<LatencyStat> Latency,
    IReadOnlyList<ModelBreakdownStat> ModelBreakdown,
    IReadOnlyList<TokenUsageStat> TokenUsage,
    IReadOnlyList<AgentTokenUsageStat> TokenUsageByAgent,
    StatisticsBucket TokenBucket,
    IReadOnlyList<IAgentCall> RecentTraces,
    IReadOnlyList<IAgent> Agents,
    IReadOnlyDictionary<Guid, DateTimeOffset> AgentLastCallTimes,
    IReadOnlyList<int> Pulse);

/// <summary>
/// Equal-width bucketed call-traffic series (excludes pass rate, which comes from run stats).
/// </summary>
public record CallTrends(
    IReadOnlyList<double> Traces,
    IReadOnlyList<double> LatencyMs,
    IReadOnlyList<double> Throughput);

public record CostEstimateStat(
    Guid EndpointId,
    decimal? InputCostEur,
    decimal? OutputCostEur,
    decimal? TotalCostEur);

/// <summary>
/// Derived spend of one agent within one project over a window. The project comes from the call's
/// <c>AgentVersion</c> (an <c>AgentCall</c> has no project of its own), so one row exists per
/// (project, agent) pair that had traffic — the shape the cost-budget guard evaluates and the
/// Costs page renders as its agent breakdown.
/// </summary>
public record ProjectAgentCostStat(
    Guid ProjectId,
    Guid AgentId,
    decimal CostEur);

/// <summary>
/// Derived spend of one agent in one time bucket. Aggregated per (bucket, agent, endpoint) in SQL
/// and priced in C#, so the result is O(buckets × agents × endpoints) and never O(calls).
/// </summary>
public record AgentCostPoint(
    DateTimeOffset BucketStart,
    Guid AgentId,
    decimal CostEur);

/// <summary>
/// Derived spend attributed to one inbound API key within one project over a window. A null
/// <see cref="ApiKeyId"/> is the unattributed remainder — traffic authenticated with the provider's
/// own upstream key, plus any call ingested before key attribution existed. It is a real group
/// rather than a dropped row, so per-key figures always reconcile with the project total.
/// </summary>
public record ProjectApiKeyCostStat(
    Guid ProjectId,
    Guid? ApiKeyId,
    decimal CostEur);

/// <summary>
/// Derived spend attributed to one inbound API key in one time bucket. Aggregated per
/// (bucket, key, endpoint) in SQL and priced in C#, so the result is O(buckets × keys × endpoints)
/// and never O(calls). A null <see cref="ApiKeyId"/> is the unattributed series.
/// </summary>
public record ApiKeyCostPoint(
    DateTimeOffset BucketStart,
    Guid? ApiKeyId,
    decimal CostEur);

public record AgentTimeSeriesPoint(
    DateTimeOffset BucketStart,
    int TraceCount,
    long InputTokens,
    long OutputTokens,
    long CachedInputTokens,
    decimal CostEur,
    double AvgLatencyMs);

public record AgentPassRatePoint(
    DateTimeOffset BucketStart,
    int Passed,
    int TestCases);

public record AgentSuitePassRate(
    Guid SuiteId,
    string SuiteName,
    DateTimeOffset LatestRunAt,
    int Passed,
    int TestCases);

public record AgentEntityCounts(
    int SuiteCount,
    int TestCaseCount,
    int OpenProposalCount,
    int TotalProposalCount);

public record AgentTimeSummary(
    int TotalTraces,
    long TotalInputTokens,
    long TotalOutputTokens,
    long TotalCachedInputTokens,
    decimal TotalCostEur,
    double AvgLatencyMs);

public record AgentOverviewStat(
    AgentTimeSummary Summary,
    IReadOnlyList<AgentTimeSeriesPoint> TimeSeries,
    IReadOnlyList<AgentPassRatePoint> PassRateTrend,
    IReadOnlyList<AgentSuitePassRate> SuitePassRates,
    AgentEntityCounts Counts);

/// <summary>One equal-width histogram bin: the half-open value range and how many samples fell in it.</summary>
public record HistogramBin(double Start, double End, int Count);

/// <summary>
/// Mean and (sample) standard deviation of one metric over its sample set, the min/max, the sample
/// count, and an equal-width <see cref="Histogram"/> of the samples (so the UI can plot the real
/// shape, not a synthesized curve). <see cref="StdDev"/> is 0 when fewer than two samples exist.
/// </summary>
public record MetricDistribution(
    double Mean,
    double StdDev,
    int SampleCount,
    double Min,
    double Max,
    IReadOnlyList<HistogramBin> Histogram)
{
    public static MetricDistribution Empty => new(0d, 0d, 0, 0d, 0d, []);
}

/// <summary>
/// Distribution (mean ± std) of the agent's ingested calls over a window. Token and latency metrics
/// are sampled per call; cost, cache-hit-rate and tool-call metrics are sampled per conversation
/// (<c>ConversationId ?? AgentCallId</c>). Cache hit rate only samples conversations with a turn ≥ 2.
/// </summary>
public record AgentCallDistributions(
    MetricDistribution InputTokensPerCall,
    MetricDistribution OutputTokensPerCall,
    MetricDistribution LatencyMsPerCall,
    MetricDistribution CostPerConversationEur,
    MetricDistribution CacheHitRatePerConversation,
    MetricDistribution ToolCallsPerConversation)
{
    public static AgentCallDistributions Empty => new(
        MetricDistribution.Empty,
        MetricDistribution.Empty,
        MetricDistribution.Empty,
        MetricDistribution.Empty,
        MetricDistribution.Empty,
        MetricDistribution.Empty);
}

public record EvaluatorSummary(
    int TotalEvaluations,
    double? AvgScore,
    double? OverallPassRate,
    long? InputTokens,
    long? OutputTokens,
    long? CachedInputTokens,
    decimal? TotalCost,
    double? AvgLatencyMs);

public record EvaluatorCostPoint(
    DateTimeOffset BucketStart,
    long InputTokens,
    long OutputTokens,
    long CachedInputTokens,
    decimal Cost,
    double AvgLatencyMs);

public record EvaluatorPassRatePoint(
    DateTimeOffset BucketStart,
    int Passed,
    int Total);

public record EvaluatorScoreBucket(
    string Score,
    int Count);

public record EvaluatorOverviewStat(
    EvaluatorSummary Summary,
    IReadOnlyList<EvaluatorPassRatePoint> PassRateTrend,
    IReadOnlyList<EvaluatorScoreBucket> ScoreDistribution,
    IReadOnlyList<EvaluatorCostPoint> CostTrend);

public record EvaluatorSparklineStat(
    Guid EvaluatorId,
    IReadOnlyList<EvaluatorPassRatePoint> Points);



/// <summary>
/// Aggregate KPI rollup across multiple test runs.
/// </summary>
public record TestRunStatsAggregate(
    int TestCases,
    int Passed,
    TimeSpan? TotalDuration,
    TokenUsage? Usage,
    decimal? Cost)
{
    public int Failed => TestCases - Passed;

    public double? PassRate
        => TestCases > 0 ? Passed / (double)TestCases : null;

    public static TestRunStatsAggregate Empty => new(
        TestCases: 0,
        Passed: 0,
        TotalDuration: null,
        Usage: null,
        Cost: null);

    public static TestRunStatsAggregate operator -(TestRunStatsAggregate a, TestRunStatsAggregate b) =>
        new(
            TestCases: a.TestCases - b.TestCases,
            Passed: a.Passed - b.Passed,
            TotalDuration: a.TotalDuration.HasValue && b.TotalDuration.HasValue
                ? a.TotalDuration.Value - b.TotalDuration.Value
                : null,
            Usage: a.Usage != null && b.Usage != null ? a.Usage - b.Usage : null,
            Cost: a.Cost.HasValue && b.Cost.HasValue ? a.Cost.Value - b.Cost.Value : null);
}
