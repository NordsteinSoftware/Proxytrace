using Autofac.Features.OwnedInstances;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Proxytrace.Storage.Internal.Entities.TestResult;

namespace Proxytrace.Storage.Internal;

/// <summary>
/// One-time, idempotent backfill of the <see cref="EvaluationStatEntity"/> projection for test results
/// recorded before that table existed (added by the <c>AddEvaluationStatProjection</c> migration without
/// a data migration). The evaluator-statistics queries read this projection directly, so a result with no
/// projection row contributes nothing to its evaluators' average score / evaluation count / pass rate —
/// the page shows an em-dash or zero even though the authoritative evaluation still lives in the JSON
/// <see cref="TestResultEntity.Evaluations"/> column.
///
/// Runs after the database initializer (migrations applied) and before the app serves traffic. It walks
/// the results that have no projection row yet in bounded, keyset-paginated batches and rebuilds each
/// result's projection rows from its stored evaluations — exactly as <c>TestResultConfig.Map</c> does on
/// insert — so the backfilled rows match newly-written ones. A result that carries no evaluations can
/// never gain a projection row, so it would never leave the candidate set; the advancing
/// <c>(CreatedAt, Id)</c> cursor steps past it instead of looping on it forever. The batch bound means a
/// database with millions of pre-existing results is never materialised at once. It never fails host boot
/// — the only impact of a failure is that older results keep showing no statistics until a later restart
/// re-runs it.
/// </summary>
internal sealed class EvaluationStatBackfillService : IHostedService
{
    private const int MaxAttempts = 3;

    // An Owned<StorageDbContext> factory (not the ambient-aware Func<StorageDbContext>): this service is a
    // singleton hosted service resolved from the root container, so the ambient factory's fresh-resolve
    // branch would track every per-batch context on the root scope until process shutdown. Owned<> hands
    // out a context from a child lifetime scope this loop disposes per batch instead (issue #256). The
    // backfill never runs inside a logical transaction, so it never needs the shared ambient context.
    private readonly Func<Owned<StorageDbContext>> contextFactory;
    private readonly ILogger<EvaluationStatBackfillService> logger;
    private readonly int batchSize;
    private readonly TimeSpan retryDelay;

    // batchSize/retryDelay default to production values and are constructor-injectable purely so tests can
    // exercise the multi-batch loop and the retry/never-throw wrapper without large data or waits. Autofac
    // supplies only contextFactory/logger and uses these defaults for the optional parameters.
    /// <summary>
    /// Initializes a new instance of the <see cref="EvaluationStatBackfillService"/> class.
    /// </summary>
    public EvaluationStatBackfillService(
        Func<Owned<StorageDbContext>> contextFactory,
        ILogger<EvaluationStatBackfillService> logger,
        int batchSize = 500,
        TimeSpan? retryDelay = null)
    {
        this.contextFactory = contextFactory;
        this.logger = logger;
        this.batchSize = batchSize;
        this.retryDelay = retryDelay ?? TimeSpan.FromSeconds(2);
    }

    /// <summary>
    /// Starts asynchronously.
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                var count = await BackfillAsync(cancellationToken);
                if (count > 0)
                {
                    logger.LogInformation("Backfilled the statistics projection for {Count} pre-existing evaluations.", count);
                }

                return;
            }
            catch (Exception ex) when (attempt < MaxAttempts && !cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Evaluation statistics backfill failed (attempt {Attempt}/{Max}); retrying.", attempt, MaxAttempts);
                await Task.Delay(retryDelay, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "Evaluation statistics backfill failed after {Max} attempts; test results recorded before the projection existed will show no evaluator statistics until a successful restart.",
                    MaxAttempts);
                return;
            }
        }
    }

    /// <summary>
    /// Runs the backfill to completion in bounded batches and returns the number of projection rows
    /// inserted. Exposed (resolvable as itself) so tests can drive it directly.
    /// </summary>
    public async Task<int> BackfillAsync(CancellationToken cancellationToken)
    {
        var total = 0;
        DateTimeOffset? cursorCreatedAt = null;
        Guid cursorId = Guid.Empty;

        while (!cancellationToken.IsCancellationRequested)
        {
            // A fresh context per batch keeps the change tracker from growing across the whole run; the
            // Owned<> handle is disposed at the end of each iteration so the context (and its child scope)
            // is released promptly rather than accumulating on the root container (issue #256).
            await using var owned = contextFactory();
            var db = owned.Value;

            // Results with no projection row yet, in total (CreatedAt, Id) order. Guid has no `>`
            // operator, so the tie-break compares via CompareTo — Npgsql translates it to the native uuid
            // `>` and the in-memory provider uses Guid's IComparable, matching the OrderBy (see
            // ApplicationErrorRepository). The keyset advances even when a row never leaves the candidate
            // set (a result with no evaluations gains no projection row), so the pass always terminates.
            IQueryable<TestResultEntity> candidates = db.Set<TestResultEntity>()
                .Where(r => !db.Set<EvaluationStatEntity>().Any(s => s.TestResultId == r.Id));
            if (cursorCreatedAt is { } cc)
            {
                candidates = candidates.Where(r =>
                    r.CreatedAt > cc || (r.CreatedAt == cc && r.Id.CompareTo(cursorId) > 0));
            }

            var batch = await candidates
                .OrderBy(r => r.CreatedAt)
                .ThenBy(r => r.Id)
                .Take(batchSize)
                // Project only the columns the rebuild needs — never the large ActualResponse payload.
                .Select(r => new { r.Id, r.CreatedAt, r.Evaluations })
                .ToListAsync(cancellationToken);

            if (batch.Count == 0)
            {
                break;
            }

            foreach (var result in batch)
            {
                foreach (var e in result.Evaluations)
                {
                    // Mirror TestResultConfig.Map's projection so backfilled rows are identical to those
                    // written on insert (CreatedAt copied from the parent; HasError mirrors a non-null
                    // ErrorMessage).
                    db.Add(new EvaluationStatEntity
                    {
                        Id = Guid.NewGuid(),
                        TestResultId = result.Id,
                        EvaluatorId = e.EvaluatorId,
                        CreatedAt = result.CreatedAt,
                        Score = e.Score,
                        HasError = e.ErrorMessage is not null,
                        InputTokens = e.InputTokens,
                        OutputTokens = e.OutputTokens,
                        CachedInputTokens = e.CachedInputTokens,
                        LatencyMicroseconds = e.LatencyMicroseconds,
                        Cost = e.Cost,
                    });
                    total++;
                }
            }

            await db.SaveChangesAsync(cancellationToken);

            var last = batch[^1];
            cursorCreatedAt = last.CreatedAt;
            cursorId = last.Id;

            if (batch.Count < batchSize)
            {
                break;
            }
        }

        return total;
    }

    /// <summary>
    /// Stops asynchronously.
    /// </summary>
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
