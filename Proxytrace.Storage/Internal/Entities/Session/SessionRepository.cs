using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Proxytrace.Domain;
using Proxytrace.Domain.Events;
using Proxytrace.Domain.Session;

namespace Proxytrace.Storage.Internal.Entities.Session;

[UsedImplicitly]
internal class SessionRepository
    : AbstractRepository<ISession, SessionEntity>,
      ISessionRepository
{
    private readonly ILogger<SessionRepository> logger;

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

    // LastActivityAt and UpdatedAt only ever move forward (CASE in SQL): a redelivered or
    // out-of-order ingest carrying an older CreatedAt must not rewind the session's activity (and
    // flip its Live indicator off) — and a rewound UpdatedAt could even fall before the row's
    // CreatedAt, making the entity fail domain validation on load. The counters still bump — the
    // trace did arrive.
    private static async Task<bool> TryBumpAsync(
        StorageDbContext context,
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
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (await Map(stored, cancellationToken), total);
    }
}
