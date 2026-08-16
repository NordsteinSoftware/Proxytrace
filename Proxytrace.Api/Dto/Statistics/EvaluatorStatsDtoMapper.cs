using Proxytrace.Domain.Statistics;

namespace Proxytrace.Api.Dto.Statistics;

/// <summary>
/// Maps evaluator statistics records to their DTOs. Shared by the statistics controller
/// and the evaluators aggregate view endpoints.
/// </summary>
internal static class EvaluatorStatsDtoMapper
{
    /// <summary>
    /// To dto.
    /// </summary>
    public static EvaluatorOverviewDto ToDto(EvaluatorOverviewStat result) => new(
        Summary: ToDto(result.Summary),
        PassRateTrend: result.PassRateTrend.Select(ToDto).ToArray(),
        ScoreDistribution: result.ScoreDistribution.Select(ToDto).ToArray(),
        CostTrend: result.CostTrend.Select(ToDto).ToArray());

    /// <summary>
    /// To dto.
    /// </summary>
    public static EvaluatorSparklineDto ToDto(EvaluatorSparklineStat s) =>
        new(s.EvaluatorId, s.Points.Select(ToDto).ToArray());

    private static EvaluatorSummaryDto ToDto(EvaluatorSummary s) =>
        new(s.TotalEvaluations, s.AvgScore, s.OverallPassRate, s.InputTokens, s.OutputTokens, s.CachedInputTokens, s.TotalCost, s.AvgLatencyMs);

    private static EvaluatorPassRatePointDto ToDto(EvaluatorPassRatePoint p) =>
        new(p.BucketStart, p.Passed, p.Total);

    private static EvaluatorScoreBucketDto ToDto(EvaluatorScoreBucket b) =>
        new(b.Score, b.Count);

    private static EvaluatorCostPointDto ToDto(EvaluatorCostPoint p) =>
        new(p.BucketStart, p.InputTokens, p.OutputTokens, p.CachedInputTokens, p.Cost, p.AvgLatencyMs);
}
