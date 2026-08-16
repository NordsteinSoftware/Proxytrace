using Autofac.Features.OwnedInstances;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Proxytrace.Storage.Internal.Entities.AgentCall;

namespace Proxytrace.Storage.Internal;

/// <summary>
/// One-time, idempotent backfill of <see cref="AgentCallEntity.RequestPreview"/> for traces ingested
/// before that column existed. The column was added nullable, without a data migration, and the
/// traces list reads this denormalised preview directly — so rows written earlier render no message
/// preview until they are backfilled.
///
/// Runs after the database initializer (migrations applied) and before the app serves traffic. It
/// processes rows in bounded batches <c>WHERE RequestPreview IS NULL</c>, computing the preview from
/// the stored request via <see cref="AgentCallPreview.Build"/> so the result matches ingestion exactly.
/// A request with no user message gets an empty-string marker rather than null, so every processed row
/// leaves the candidate set: the set strictly shrinks, the pass terminates, and a re-run is a no-op.
/// The batch bound means a database with millions of pre-existing traces is never materialised at once.
/// It never fails host boot — the only impact of a failure is that older traces keep showing no preview
/// until a later restart re-runs it.
/// </summary>
internal sealed class AgentCallPreviewBackfillService : IHostedService
{
    private const int MaxAttempts = 3;

    // An Owned<StorageDbContext> factory (not the ambient-aware Func<StorageDbContext>): this service is a
    // singleton hosted service resolved from the root container, so the ambient factory's fresh-resolve
    // branch would track every per-batch context on the root scope until process shutdown. Owned<> hands
    // out a context from a child lifetime scope this loop disposes per batch instead (issue #256). The
    // backfill never runs inside a logical transaction, so it never needs the shared ambient context.
    private readonly Func<Owned<StorageDbContext>> contextFactory;
    private readonly ILogger<AgentCallPreviewBackfillService> logger;
    private readonly int batchSize;
    private readonly TimeSpan retryDelay;

    // batchSize/retryDelay default to production values and are constructor-injectable purely so tests
    // can exercise the multi-batch loop and the retry/never-throw wrapper without large data or waits.
    // Autofac supplies only contextFactory/logger and uses these defaults for the optional parameters.
    /// <summary>
    /// Initializes a new instance of the <see cref="AgentCallPreviewBackfillService"/> class.
    /// </summary>
    public AgentCallPreviewBackfillService(
        Func<Owned<StorageDbContext>> contextFactory,
        ILogger<AgentCallPreviewBackfillService> logger,
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
                    logger.LogInformation("Backfilled the message preview for {Count} pre-existing traces.", count);
                }

                return;
            }
            catch (Exception ex) when (attempt < MaxAttempts && !cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Trace preview backfill failed (attempt {Attempt}/{Max}); retrying.", attempt, MaxAttempts);
                await Task.Delay(retryDelay, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "Trace preview backfill failed after {Max} attempts; traces ingested before the preview column will show no preview until a successful restart.",
                    MaxAttempts);
                return;
            }
        }
    }

    /// <summary>
    /// Runs the backfill to completion in bounded batches and returns the number of rows updated.
    /// Exposed (resolvable as itself) so tests can drive it directly.
    /// </summary>
    public async Task<int> BackfillAsync(CancellationToken cancellationToken)
    {
        var total = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            // A fresh context per batch keeps the change tracker from growing across the whole run; the
            // Owned<> handle is disposed at the end of each iteration so the context (and its child scope)
            // is released promptly rather than accumulating on the root container (issue #256).
            await using var owned = contextFactory();
            var db = owned.Value;
            var batch = await db.Set<AgentCallEntity>()
                .Where(e => e.RequestPreview == null)
                .OrderBy(e => e.CreatedAt)
                .Take(batchSize)
                .ToListAsync(cancellationToken);

            if (batch.Count == 0)
            {
                break;
            }

            foreach (var row in batch)
            {
                // Empty marker (not null) when the request has no user message, so the row leaves the
                // IS NULL candidate set and the pass cannot loop on it. The list query passes this value
                // straight through; the client renders an empty preview as the same em-dash placeholder.
                var preview = AgentCallPreview.Build(row.Request) ?? string.Empty;
                db.Entry(row).CurrentValues.SetValues(row with { RequestPreview = preview });
            }

            await db.SaveChangesAsync(cancellationToken);
            total += batch.Count;

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
