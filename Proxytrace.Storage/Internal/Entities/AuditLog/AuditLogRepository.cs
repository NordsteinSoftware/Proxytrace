using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using Proxytrace.Domain;
using Proxytrace.Domain.AuditLog;
using Proxytrace.Domain.Events;
using Proxytrace.Domain.Paging;

namespace Proxytrace.Storage.Internal.Entities.AuditLog;

[UsedImplicitly]
internal class AuditLogRepository
    : AbstractRepository<IAuditLogEntry, AuditLogEntryEntity>,
      IAuditLogRepository
{
    public AuditLogRepository(
        IMapper<IAuditLogEntry, AuditLogEntryEntity> mapper,
        Func<StorageDbContext> contextFactory,
        ITransaction transaction,
        IEntityEventService entityEvents,
        AmbientDbContext ambient) : base(mapper, contextFactory, transaction, entityEvents, ambient)
    {
    }

    public async Task<PagedResult<IAuditLogEntry>> GetPagedNewestFirstAsync(
        int page,
        int pageSize,
        AuditAction? action,
        string? actorSearch,
        IReadOnlyCollection<Guid>? projectIds,
        bool includeGlobal,
        string? targetType,
        Guid? targetId,
        DateTimeOffset? from,
        DateTimeOffset? to,
        CancellationToken cancellationToken = default)
    {
        (page, pageSize) = Paging.Clamp(page, pageSize);

        var query = contextFactory().Set<AuditLogEntryEntity>().AsNoTracking();

        if (action.HasValue)
        {
            query = query.Where(e => e.Action == action.Value);
        }

        if (projectIds is not null)
        {
            // Non-admin scope: only the caller's projects, plus global (null-project) rows only
            // when includeGlobal is set. Admin passes projectIds == null and skips this entirely.
            var ids = projectIds.ToList();
            query = includeGlobal
                ? query.Where(e => (e.ProjectId != null && ids.Contains(e.ProjectId.Value)) || e.ProjectId == null)
                : query.Where(e => e.ProjectId != null && ids.Contains(e.ProjectId.Value));
        }

        if (!string.IsNullOrWhiteSpace(targetType))
        {
            query = query.Where(e => e.TargetType == targetType);
        }

        if (targetId.HasValue)
        {
            query = query.Where(e => e.TargetId == targetId.Value);
        }

        if (from.HasValue)
        {
            query = query.Where(e => e.CreatedAt >= from.Value);
        }

        if (to.HasValue)
        {
            query = query.Where(e => e.CreatedAt <= to.Value);
        }

        if (!string.IsNullOrWhiteSpace(actorSearch))
        {
            // Case-insensitive infix match: lower-case both sides so it works identically on Postgres
            // (where SQL LIKE is case-sensitive — translates to lower(ActorEmail) LIKE …) and the
            // in-memory provider. A plain EF.Functions.Like would silently be case-sensitive in prod.
            var pattern = $"%{actorSearch.Trim().ToLowerInvariant()}%";
            query = query.Where(e => e.ActorEmail != null && EF.Functions.Like(e.ActorEmail.ToLower(), pattern));
        }

        int total = await query.CountAsync(cancellationToken);
        var stored = await query
            // Id is the secondary key so rows sharing a CreatedAt (bursts truncate to the same µs) keep a
            // stable, total order across pages — without it a tied row can duplicate or vanish at a boundary.
            .OrderByDescending(e => e.CreatedAt)
            .ThenByDescending(e => e.Id)
            .Skip(Paging.Offset(page, pageSize))
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var items = await Map(stored, cancellationToken);
        return new PagedResult<IAuditLogEntry>(items, total, page, pageSize);
    }

    public async Task<int> RemoveOlderThanAsync(DateTimeOffset cutoffDate, CancellationToken cancellationToken = default)
    {
        var context = contextFactory();
        var query = context.Set<AuditLogEntryEntity>().Where(x => x.CreatedAt <= cutoffDate);

        if (context.Database.IsRelational())
            return await query.ExecuteDeleteAsync(cancellationToken);

        // The in-memory provider does not support ExecuteDelete — materialize then remove.
        var toRemove = await query.ToListAsync(cancellationToken);
        context.Set<AuditLogEntryEntity>().RemoveRange(toRemove);
        return await context.SaveChangesAsync(cancellationToken);
    }
}
