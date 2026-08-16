using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Proxytrace.Domain;
using Nordstein.Core.Domain.Events;
using Nordstein.Core.Domain.Paging;
using Proxytrace.Domain.Session;

namespace Proxytrace.Storage.Internal.Entities.Session;

[UsedImplicitly]
internal class SessionRepository
    : AbstractRepository<ISession, SessionEntity>,
      ISessionRepository
{
    private readonly ILogger<SessionRepository> logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SessionRepository"/> class.
    /// </summary>
    public SessionRepository(
        IMapper<ISession, SessionEntity> mapper,
        Func<StorageDbContext> contextFactory,
        ITransaction transaction,
        IEntityEventService entityEvents,
        AmbientDbContext ambient,
        ILogger<SessionRepository> logger) : base(mapper, contextFactory, transaction, entityEvents, ambient)
    {
        this.logger = logger;
    }

    /// <summary>
    /// Record activity asynchronously.
    /// </summary>
    public async Task RecordActivityAsync(
        Guid sessionId,
        string externalKey,
        Guid projectId,
        long totalTokens,
        DateTimeOffset lastActivityAt,
        CancellationToken cancellationToken = default)
    {
        var context = contextFactory();
        if (context.Database.IsRelational())
        {
            if (await TryBumpAsync(context, sessionId, totalTokens, lastActivityAt, cancellationToken))
                return;
            try
            {
                context.Set<SessionEntity>().Add(NewRow(sessionId, externalKey, projectId, totalTokens, lastActivityAt));
                await context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                // Lost the first-insert race to a concurrent ingester (unique PK / (ProjectId,
                // ExternalKey) index): the row exists now, so the recovery bump on a fresh context
                // succeeds — which is also why this upsert must never run inside an ambient
                // transaction (see ISessionRepository): there contextFactory() would return the
                // shared, already-aborted transactional context. A false result here means the
                // insert failed for another reason (e.g. the project was deleted concurrently);
                // best-effort, so log it rather than fail the caller.
                if (!await TryBumpAsync(contextFactory(), sessionId, totalTokens, lastActivityAt, cancellationToken))
                {
                    logger.LogWarning(
                        "Session upsert lost the insert race but the recovery bump found no row for session {SessionId}",
                        sessionId);
                }
            }
            return;
        }

        // In-memory provider (unit tests / kiosk): no ExecuteUpdate support, single-process, so a
        // read-modify-write is race-free enough. We fetch with tracking and modify in place.
        var existing = await context.Set<SessionEntity>()
            .FirstOrDefaultAsync(e => e.Id == sessionId, cancellationToken);
        if (existing is null)
        {
            context.Set<SessionEntity>().Add(NewRow(sessionId, externalKey, projectId, totalTokens, lastActivityAt));
        }
        else
        {
            // Modify in place, mirroring the relational ExecuteUpdate path (including the
            // forward-only LastActivityAt and UpdatedAt).
            context.Entry(existing).CurrentValues.SetValues(new
            {
                LastActivityAt = lastActivityAt > existing.LastActivityAt ? lastActivityAt : existing.LastActivityAt,
                TraceCount = existing.TraceCount + 1,
                TotalTokens = existing.TotalTokens + totalTokens,
                UpdatedAt = lastActivityAt > existing.UpdatedAt ? lastActivityAt : existing.UpdatedAt,
            });
        }
        await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Record trace removals asynchronously.
    /// </summary>
    public async Task RecordTraceRemovalsAsync(
        IReadOnlyCollection<SessionTraceRemoval> removals,
        CancellationToken cancellationToken = default)
    {
        if (removals.Count == 0)
            return;

        var context = contextFactory();

        foreach (var removal in removals)
        {
            // Clamped at zero on both counters. The deltas are computed from the rows about to be
            // deleted, but a counter can already be low (a bump that failed after its trace
            // persisted — the upsert is best-effort by design), and a negative count would render as
            // nonsense in the session header.
            if (context.Database.IsRelational())
            {
                await context.Set<SessionEntity>()
                    .Where(e => e.Id == removal.SessionId)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(e => e.TraceCount, e => e.TraceCount > removal.TraceCount ? e.TraceCount - removal.TraceCount : 0)
                        .SetProperty(e => e.TotalTokens, e => e.TotalTokens > removal.TotalTokens ? e.TotalTokens - removal.TotalTokens : 0),
                        cancellationToken);
                continue;
            }

            // In-memory provider (unit tests / kiosk): no ExecuteUpdate, single process, so a
            // read-modify-write is race-free enough — mirroring RecordActivityAsync.
            var existing = await context.Set<SessionEntity>()
                .FirstOrDefaultAsync(e => e.Id == removal.SessionId, cancellationToken);
            if (existing is null)
                continue;

            context.Entry(existing).CurrentValues.SetValues(new
            {
                TraceCount = Math.Max(0, existing.TraceCount - removal.TraceCount),
                TotalTokens = Math.Max(0, existing.TotalTokens - removal.TotalTokens),
            });
            await context.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Removes the older than asynchronously.
    /// </summary>
    public async Task<int> RemoveOlderThanAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default)
    {
        var context = contextFactory();
        var query = context.Set<SessionEntity>().Where(e => e.LastActivityAt <= cutoff);

        // Server-side DELETE without materializing rows, mirroring the trace retention sweep this
        // runs alongside; the in-memory provider can't translate it, so fall back there.
        if (context.Database.IsRelational())
            return await query.ExecuteDeleteAsync(cancellationToken);

        var toRemove = await query.ToListAsync(cancellationToken);
        context.Set<SessionEntity>().RemoveRange(toRemove);
        await context.SaveChangesAsync(cancellationToken);
        return toRemove.Count;
    }

    // LastActivityAt and UpdatedAt only ever move forward (CASE in SQL): a redelivered or
    // out-of-order ingest carrying an older CreatedAt must not rewind the session's activity (and
    // flip its Live indicator off) — and a rewound UpdatedAt could even fall before the row's
    // CreatedAt, making the entity fail domain validation on load. The counters still bump — the
    // trace did arrive.
    private static async Task<bool> TryBumpAsync(
        DbContext context,
        Guid sessionId,
        long totalTokens,
        DateTimeOffset lastActivityAt,
        CancellationToken cancellationToken)
        => await context.Set<SessionEntity>()
            .Where(e => e.Id == sessionId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(e => e.LastActivityAt, e => e.LastActivityAt > lastActivityAt ? e.LastActivityAt : lastActivityAt)
                .SetProperty(e => e.TraceCount, e => e.TraceCount + 1)
                .SetProperty(e => e.TotalTokens, e => e.TotalTokens + totalTokens)
                .SetProperty(e => e.UpdatedAt, e => e.UpdatedAt > lastActivityAt ? e.UpdatedAt : lastActivityAt), cancellationToken) > 0;

    private static SessionEntity NewRow(
        Guid sessionId, string externalKey, Guid projectId, long totalTokens, DateTimeOffset lastActivityAt)
        => new()
        {
            Id = sessionId,
            ExternalKey = externalKey,
            ProjectId = projectId,
            LastActivityAt = lastActivityAt,
            TraceCount = 1,
            TotalTokens = totalTokens,
            CreatedAt = lastActivityAt,
            UpdatedAt = lastActivityAt,
        };

    /// <summary>
    /// Gets the recent asynchronously.
    /// </summary>
    public async Task<(IReadOnlyList<ISession> Items, int Total)> GetRecentAsync(
        Guid projectId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query = contextFactory()
            .Set<SessionEntity>()
            .AsNoTracking()
            .Where(e => e.ProjectId == projectId);

        var total = await query.CountAsync(cancellationToken);
        var stored = await query
            .OrderByDescending(e => e.LastActivityAt)
            .Skip(Paging.Offset(page, pageSize))
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (await Map(stored, cancellationToken), total);
    }
}
