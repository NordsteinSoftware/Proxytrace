using Autofac.Features.OwnedInstances;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Proxytrace.Storage.Internal.Entities.AgentCall;
using Proxytrace.Storage.Internal.Entities.AgentVersion;

namespace Proxytrace.Storage.Internal;

/// <summary>
/// One-time, idempotent backfill of <see cref="AgentCallToolEntity"/> rows for traces ingested
/// before the tool-name table existed. Ingestion denormalises the tool names of a response into
/// per-call rows (backing the ToolName filter and the tool-name picker) — so pre-existing traces
/// are invisible to the tool filter until they are backfilled.
///
/// Runs after the database initializer (migrations applied) and before the app serves traffic. It
/// processes rows in bounded batches — candidates are calls whose denormalised
/// <see cref="AgentCallEntity.ResponseToolRequestCount"/> says "has tools" but that have no
/// <see cref="AgentCallToolEntity"/> rows yet — re-deriving the names from the stored response so
/// the result matches ingestion exactly. A call whose stored response yields no names (a shape
/// ingestion cannot produce, but the pass must not trust that) gets a single empty-string marker
/// row instead, so every processed row leaves the candidate set: the set strictly shrinks, the
/// pass terminates, and a re-run is a no-op. Marker rows never surface — the ToolName filter
/// rejects blank names and the picker query excludes the empty marker.
/// The batch bound means a database with millions of pre-existing traces is never materialised at
/// once. It never fails host boot — the only impact of a failure is that older traces stay
/// invisible to the tool filter until a later restart re-runs it.
/// </summary>
internal sealed class AgentCallToolBackfillService : IHostedService
{
    private const int MaxAttempts = 3;

    // An Owned<StorageDbContext> factory (not the ambient-aware Func<StorageDbContext>): this service is a
    // singleton hosted service resolved from the root container, so the ambient factory's fresh-resolve
    // branch would track every per-batch context on the root scope until process shutdown. Owned<> hands
    // out a context from a child lifetime scope this loop disposes per batch instead (issue #256). The
    // backfill never runs inside a logical transaction, so it never needs the shared ambient context.
    private readonly Func<Owned<StorageDbContext>> contextFactory;
    private readonly ILogger<AgentCallToolBackfillService> logger;
    private readonly int batchSize;
    private readonly TimeSpan retryDelay;

    // batchSize/retryDelay default to production values and are constructor-injectable purely so tests
    // can exercise the multi-batch loop and the retry/never-throw wrapper without large data or waits.
    // Autofac supplies only contextFactory/logger and uses these defaults for the optional parameters.
    /// <summary>
    /// Initializes a new instance of the <see cref="AgentCallToolBackfillService"/> class.
    /// </summary>
    public AgentCallToolBackfillService(
        Func<Owned<StorageDbContext>> contextFactory,
        ILogger<AgentCallToolBackfillService> logger,
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
                    logger.LogInformation("Backfilled tool-name rows for {Count} pre-existing traces.", count);
                }

                return;
            }
            catch (Exception ex) when (attempt < MaxAttempts && !cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Trace tool-name backfill failed (attempt {Attempt}/{Max}); retrying.", attempt, MaxAttempts);
                await Task.Delay(retryDelay, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "Trace tool-name backfill failed after {Max} attempts; traces ingested before the tool-name table will not match the tool filter until a successful restart.",
                    MaxAttempts);
                return;
            }
        }
    }

    /// <summary>
    /// Runs the backfill to completion in bounded batches and returns the number of calls processed.
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

            // ProjectId and AgentId are denormalised onto each tool row (see AgentCallToolEntity), so
            // join the call's agent version to recover them — exactly what ingestion reads from the
            // domain graph.
            var batch = await (
                    from call in db.Set<AgentCallEntity>()
                    join version in db.Set<AgentVersionEntity>() on call.AgentVersionId equals version.Id
                    where call.ResponseToolRequestCount > 0
                          && !db.Set<AgentCallToolEntity>().Any(t => t.AgentCallId == call.Id)
                    orderby call.CreatedAt
                    select new { call.Id, call.Response, call.CreatedAt, call.UpdatedAt, version.Project, version.AgentId })
                .Take(batchSize)
                .ToListAsync(cancellationToken);

            if (batch.Count == 0)
            {
                break;
            }

            foreach (var row in batch)
            {
                var names = row.Response?.ToolRequests.Select(t => t.Name).Distinct().ToList() ?? [];

                // Empty marker (single blank-named row) when the response yields no names, so the row
                // leaves the "has no tool rows" candidate set and the pass cannot loop on it. The filter
                // clause rejects blank names and the picker query excludes the marker, so it never surfaces.
                var rowNames = names.Count > 0 ? names : [string.Empty];
                db.Set<AgentCallToolEntity>().AddRange(rowNames.Select(name => new AgentCallToolEntity
                {
                    Id = Guid.NewGuid(),
                    AgentCallId = row.Id,
                    ProjectId = row.Project,
                    AgentId = row.AgentId,
                    ToolName = name,
                    CreatedAt = row.CreatedAt,
                    UpdatedAt = row.UpdatedAt,
                }));
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
