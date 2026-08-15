using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using Proxytrace.Domain.Statistics;
using Proxytrace.Domain.Evaluation;
using Proxytrace.Storage.Internal.Entities.Evaluator;
using Proxytrace.Storage.Internal.Entities.TestResult;

namespace Proxytrace.Storage.Internal.Statistics;

[UsedImplicitly]
internal class EvaluatorStatsQueries : IEvaluatorStatsReader
{
    private readonly Func<StorageDbContext> contextFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="EvaluatorStatsQueries"/> class.
    /// </summary>
    public EvaluatorStatsQueries(Func<StorageDbContext> contextFactory)
    {
        this.contextFactory = contextFactory;
    }

    /// <summary>
    /// Gets the overview asynchronously.
    /// </summary>
    public async Task<EvaluatorOverviewStat> GetOverviewAsync(
        Guid evaluatorId,
        DateTimeOffset from,
        DateTimeOffset to,
        StatisticsBucket bucket,
        CancellationToken cancellationToken = default)
    {
        StorageDbContext context = contextFactory();

        // Filter to this evaluator's evaluations in SQL via the EvaluationStat projection, so only
        // the in-scope rows cross the wire — never the whole window's worth of test results, and no
        // JSON deserialization. See EvaluationStatEntity.
        var raw = await context.Set<EvaluationStatEntity>()
            .AsNoTracking()
            .Where(e => e.EvaluatorId == evaluatorId && e.CreatedAt >= from && e.CreatedAt <= to)
            .Select(e => new
            {
                e.CreatedAt,
                e.Score,
                e.HasError,
                e.InputTokens,
                e.OutputTokens,
                e.CachedInputTokens,
                e.LatencyMicroseconds,
                e.Cost,
            })
            .ToListAsync(cancellationToken);

        EvaluationRow[] rows = raw
            .Select(e => new EvaluationRow(
                e.CreatedAt,
                e.Score,
                e.HasError,
                e.InputTokens,
                e.OutputTokens,
                e.CachedInputTokens,
                e.LatencyMicroseconds,
                e.Cost))
            .ToArray();

        return BuildOverview(rows, bucket);
    }

    /// <summary>
    /// Gets the sparklines asynchronously.
    /// </summary>
    public async Task<IReadOnlyList<EvaluatorSparklineStat>> GetSparklinesAsync(
        IReadOnlyCollection<Guid> projectIds,
        DateTimeOffset from,
        DateTimeOffset to,
        StatisticsBucket bucket,
        CancellationToken cancellationToken = default)
    {
        if (projectIds.Count == 0)
        {
            return [];
        }

        StorageDbContext context = contextFactory();

        // The owning-project filter runs in SQL for one project or several alike (#483); the
        // evaluations query below is unchanged — it was already keyed on the resulting evaluator ids.
        Guid[] evaluatorIds = await context.Set<EvaluatorEntity>()
            .AsNoTracking()
            .Where(e => projectIds.Contains(e.Project))
            .Select(e => e.Id)
            .ToArrayAsync(cancellationToken);

        if (evaluatorIds.Length == 0)
        {
            return [];
        }

        // Scope to this project's evaluators in SQL; only their evaluations are materialized.
        var rows = await context.Set<EvaluationStatEntity>()
            .AsNoTracking()
            .Where(e => evaluatorIds.Contains(e.EvaluatorId) && e.CreatedAt >= from && e.CreatedAt <= to)
            .Select(e => new
            {
                e.EvaluatorId,
                e.CreatedAt,
                e.Score,
                e.HasError,
            })
            .ToListAsync(cancellationToken);

        ILookup<Guid, (DateTimeOffset Timestamp, EvaluationScore? Score, bool HasError)> byEvaluator =
            rows.ToLookup(
                r => r.EvaluatorId,
                r => (r.CreatedAt, r.Score, r.HasError));

        return evaluatorIds
            .Select(id =>
            {
                EvaluatorPassRatePoint[] points = byEvaluator[id]
                    .Where(x => x is { HasError: false, Score: not null })
                    .GroupBy(x => bucket.BucketStart(x.Timestamp))
                    .OrderBy(g => g.Key)
                    .Select(g => new EvaluatorPassRatePoint(
                        BucketStart: g.Key,
                        Passed: g.Count(x => IsPassed(x.Score ?? 0)),
                        Total: g.Count()))
                    .ToArray();

                return new EvaluatorSparklineStat(id, points);
            })
            .ToArray();
    }

    private static EvaluatorOverviewStat BuildOverview(EvaluationRow[] matching, StatisticsBucket bucket)
    {
        var succeeded = matching
            .Where(x => x.Score.HasValue)
            .ToArray();
        int total = succeeded.Length;
        double? avgScore = total > 0 ? succeeded.Average(x => (double)(byte)(x.Score ?? 0)) : null;
        int passedCount = succeeded.Count(x => IsPassed(x.Score ?? 0));
        double? passRate = total > 0 ? passedCount / (double)total : null;

        var withTokens = matching.Where(x => x is { InputTokens: not null, OutputTokens: not null }).ToArray();
        long? inputTokens = withTokens.Length > 0 ? withTokens.Sum(x => x.InputTokens ?? 0) : null;
        long? outputTokens = withTokens.Length > 0 ? withTokens.Sum(x => x.OutputTokens ?? 0) : null;
        long? cachedInputTokens = withTokens.Length > 0 ? withTokens.Sum(x => x.CachedInputTokens ?? 0) : null;

        var withCost = matching.Where(x => x.Cost.HasValue).ToArray();
        decimal? totalCost = withCost.Length > 0 ? withCost.Sum(x => x.Cost ?? 0) : null;

        double? avgLatencyMs = matching.Length > 0
            ? matching.Average(x => x.LatencyMicroseconds / 1_000.0)
            : null;

        var summary = new EvaluatorSummary(
            TotalEvaluations: total,
            AvgScore: avgScore,
            OverallPassRate: passRate,
            InputTokens: inputTokens,
            OutputTokens: outputTokens,
            CachedInputTokens: cachedInputTokens,
            TotalCost: totalCost,
            AvgLatencyMs: avgLatencyMs);

        EvaluatorPassRatePoint[] trend = succeeded
            .GroupBy(x => bucket.BucketStart(x.Timestamp))
            .OrderBy(g => g.Key)
            .Select(g => new EvaluatorPassRatePoint(
                BucketStart: g.Key,
                Passed: g.Count(x => IsPassed(x.Score ?? 0)),
                Total: g.Count()))
            .ToArray();

        EvaluatorScoreBucket[] distribution = succeeded
            .Where(x => x.Score.HasValue)
            .GroupBy(x => x.Score ?? 0)
            .OrderBy(g => (byte)g.Key)
            .Select(g => new EvaluatorScoreBucket(g.Key.ToString(), g.Count()))
            .ToArray();

        EvaluatorCostPoint[] costTrend = matching
            .GroupBy(x => bucket.BucketStart(x.Timestamp))
            .OrderBy(g => g.Key)
            .Select(g => new EvaluatorCostPoint(
                BucketStart: g.Key,
                InputTokens: g.Sum(x => x.InputTokens ?? 0),
                OutputTokens: g.Sum(x => x.OutputTokens ?? 0),
                CachedInputTokens: g.Sum(x => x.CachedInputTokens ?? 0),
                Cost: g.Sum(x => x.Cost ?? 0m),
                AvgLatencyMs: g.Average(x => x.LatencyMicroseconds / 1_000.0)))
            .ToArray();

        return new EvaluatorOverviewStat(summary, trend, distribution, costTrend);
    }

    private static bool IsPassed(EvaluationScore score) => (byte)score >= (byte)EvaluationScore.Acceptable;

    // Scalar projection of an evaluation for in-memory aggregation, after the SQL filter has already
    // scoped the rows to the queried evaluator(s) and time window.
    private sealed record EvaluationRow(
        DateTimeOffset Timestamp,
        EvaluationScore? Score,
        bool HasError,
        long? InputTokens,
        long? OutputTokens,
        long? CachedInputTokens,
        long LatencyMicroseconds,
        decimal? Cost);
}
