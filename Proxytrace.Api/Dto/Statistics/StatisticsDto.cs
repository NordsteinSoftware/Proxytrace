using Proxytrace.Api.Dto.AgentCalls;
using Proxytrace.Api.Dto.Agents;

namespace Proxytrace.Api.Dto.Statistics;

/// <summary>
/// Single-call payload for the dashboard page; bundles every widget's data so the client
/// makes one request instead of fanning out across the granular statistics endpoints.
/// </summary>
public record DashboardViewDto(
    SummaryDto Summary,
    LiveTelemetryDto LiveTelemetry,
    DashboardTrendsDto Trends,
    IReadOnlyList<AgentBreakdownDto> AgentBreakdown,
    IReadOnlyList<LatencyDto> Latency,
    IReadOnlyList<ModelBreakdownDto> ModelBreakdown,
    IReadOnlyList<TokenUsageDto> TokenUsage,
    IReadOnlyList<AgentTokenUsageDto> TokenUsageByAgent,
    /// <summary>Bucket granularity used for the token series, e.g. "fiveMinutes", "hourly", "daily".</summary>
    string TokenBucket,
    IReadOnlyList<AgentCallListItemDto> RecentTraces,
    IReadOnlyList<AgentListItemDto> Agents,
    /// <summary>Per-minute call counts over the trailing hour (60 entries, oldest → newest).</summary>
    IReadOnlyList<int> Pulse);

/// <summary>
/// Data transfer object representing a summary.
/// </summary>
public record SummaryDto(
    long TotalCalls,
    long TotalInputTokens,
    long TotalOutputTokens,
    long TotalCachedInputTokens,
    double AvgLatencyMs,
    double? OverallPassRate);

/// <summary>
/// Data transfer object representing a token usage.
/// </summary>
public record TokenUsageDto(DateTimeOffset BucketStart, Guid EndPointId, long InputTokens, long OutputTokens, long CachedInputTokens);

/// <summary>
/// Data transfer object representing a latency.
/// </summary>
public record LatencyDto(Guid EndpointId, double P50Ms, double P95Ms, double P99Ms, double MinMs, double MaxMs, int SampleCount);

/// <summary>
/// Data transfer object representing a model breakdown.
/// </summary>
public record ModelBreakdownDto(Guid EndpointId, string ModelName, int CallCount, long TotalInputTokens, long TotalOutputTokens, long TotalCachedInputTokens, double AvgDurationMs);

/// <summary>
/// Data transfer object representing a agent breakdown.
/// </summary>
public record AgentBreakdownDto(Guid AgentId, int CallCount);

/// <summary>
/// Data transfer object representing a live telemetry.
/// </summary>
public record LiveTelemetryDto(
    double TracesPerMinute,
    double TokensPerSecond,
    int QueueDepth,
    double ErrorRate,
    double P95Ms);

/// <summary>
/// Data transfer object representing a agent token usage.
/// </summary>
public record AgentTokenUsageDto(DateTimeOffset BucketStart, Guid AgentId, long InputTokens, long OutputTokens, long CachedInputTokens);

/// <summary>
/// Flagged (outlier) calls per (bucket, agent), split into the statistical ingestion-time flags and
/// the custom-detector flag. A call carrying both kinds counts in both.
/// </summary>
public record AgentAnomalyStatDto(DateTimeOffset BucketStart, Guid AgentId, int StaticCount, int CustomCount);

/// <summary>
/// Data transfer object representing a dashboard trends.
/// </summary>
public record DashboardTrendsDto(
    IReadOnlyList<double> Traces,
    IReadOnlyList<double> LatencyMs,
    IReadOnlyList<double> Throughput,
    IReadOnlyList<double> PassRate);

/// <summary>
/// Data transfer object representing a agent time series point.
/// </summary>
public record AgentTimeSeriesPointDto(
    DateTimeOffset BucketStart,
    int TraceCount,
    long InputTokens,
    long OutputTokens,
    long CachedInputTokens,
    decimal CostEur,
    double AvgLatencyMs);

/// <summary>
/// Data transfer object representing a agent pass rate point.
/// </summary>
public record AgentPassRatePointDto(
    DateTimeOffset BucketStart,
    int Passed,
    int TestCases);

/// <summary>
/// Data transfer object representing a agent suite pass rate.
/// </summary>
public record AgentSuitePassRateDto(
    Guid SuiteId,
    string SuiteName,
    DateTimeOffset LatestRunAt,
    int Passed,
    int TestCases);

/// <summary>
/// Data transfer object representing a agent entity counts.
/// </summary>
public record AgentEntityCountsDto(
    int SuiteCount,
    int TestCaseCount,
    int OpenProposalCount,
    int TotalProposalCount);

/// <summary>
/// Data transfer object representing a agent time summary.
/// </summary>
public record AgentTimeSummaryDto(
    int TotalTraces,
    long TotalInputTokens,
    long TotalOutputTokens,
    long TotalCachedInputTokens,
    decimal TotalCostEur,
    double AvgLatencyMs);

/// <summary>
/// Data transfer object representing a agent overview.
/// </summary>
public record AgentOverviewDto(
    AgentTimeSummaryDto Summary,
    IReadOnlyList<AgentTimeSeriesPointDto> TimeSeries,
    IReadOnlyList<AgentPassRatePointDto> PassRateTrend,
    IReadOnlyList<AgentSuitePassRateDto> SuitePassRates,
    AgentEntityCountsDto Counts);

/// <summary>
/// Data transfer object representing a histogram bin.
/// </summary>
public record HistogramBinDto(
    double Start,
    double End,
    int Count);

/// <summary>
/// Data transfer object representing a metric distribution.
/// </summary>
public record MetricDistributionDto(
    double Mean,
    double StdDev,
    int SampleCount,
    double Min,
    double Max,
    IReadOnlyList<HistogramBinDto> Histogram);

/// <summary>
/// Data transfer object representing a agent distributions.
/// </summary>
public record AgentDistributionsDto(
    MetricDistributionDto InputTokensPerCall,
    MetricDistributionDto OutputTokensPerCall,
    MetricDistributionDto LatencyMsPerCall,
    MetricDistributionDto CostPerConversationEur,
    MetricDistributionDto CacheHitRatePerConversation,
    MetricDistributionDto ToolCallsPerConversation);

/// <summary>
/// Data transfer object representing a evaluator summary.
/// </summary>
public record EvaluatorSummaryDto(
    int TotalEvaluations,
    double? AvgScore,
    double? OverallPassRate,
    long? InputTokens,
    long? OutputTokens,
    long? CachedInputTokens,
    decimal? TotalCost,
    double? AvgLatencyMs);

/// <summary>
/// Data transfer object representing a evaluator pass rate point.
/// </summary>
public record EvaluatorPassRatePointDto(
    DateTimeOffset BucketStart,
    int Passed,
    int Total);

/// <summary>
/// Data transfer object representing a evaluator score bucket.
/// </summary>
public record EvaluatorScoreBucketDto(
    string Score,
    int Count);

/// <summary>
/// Data transfer object representing a evaluator cost point.
/// </summary>
public record EvaluatorCostPointDto(
    DateTimeOffset BucketStart,
    long InputTokens,
    long OutputTokens,
    long CachedInputTokens,
    decimal Cost,
    double AvgLatencyMs);

/// <summary>
/// Data transfer object representing a evaluator overview.
/// </summary>
public record EvaluatorOverviewDto(
    EvaluatorSummaryDto Summary,
    IReadOnlyList<EvaluatorPassRatePointDto> PassRateTrend,
    IReadOnlyList<EvaluatorScoreBucketDto> ScoreDistribution,
    IReadOnlyList<EvaluatorCostPointDto> CostTrend);

/// <summary>
/// Data transfer object representing a evaluator sparkline.
/// </summary>
public record EvaluatorSparklineDto(
    Guid EvaluatorId,
    IReadOnlyList<EvaluatorPassRatePointDto> Points);
