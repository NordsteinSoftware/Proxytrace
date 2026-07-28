namespace Proxytrace.Domain.Statistics;

/// <summary>
/// Storage-side projection of evaluation results into evaluator-scoped statistics.
/// </summary>
public interface IEvaluatorStatsReader
{
    Task<EvaluatorOverviewStat> GetOverviewAsync(
        Guid evaluatorId,
        DateTimeOffset from,
        DateTimeOffset to,
        StatisticsBucket bucket,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// One pass-rate sparkline per evaluator owned by <paramref name="projectIds"/>. Takes a set
    /// rather than a single project so the evaluators overview can be scoped to everything the
    /// caller may read — a non-admin who belongs to several projects and filtered by none would
    /// otherwise see evaluators with no sparklines beside them (#483).
    /// </summary>
    Task<IReadOnlyList<EvaluatorSparklineStat>> GetSparklinesAsync(
        IReadOnlyCollection<Guid> projectIds,
        DateTimeOffset from,
        DateTimeOffset to,
        StatisticsBucket bucket,
        CancellationToken cancellationToken = default);
}
