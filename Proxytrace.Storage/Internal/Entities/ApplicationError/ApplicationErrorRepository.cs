using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using Proxytrace.Domain;
using Proxytrace.Domain.ApplicationError;
using Proxytrace.Domain.Events;
using Proxytrace.Domain.Paging;

namespace Proxytrace.Storage.Internal.Entities.ApplicationError;

[UsedImplicitly]
internal class ApplicationErrorRepository
    : AbstractRepository<IApplicationError, ApplicationErrorEntity>,
      IApplicationErrorRepository
{
    public ApplicationErrorRepository(
        IMapper<IApplicationError, ApplicationErrorEntity> mapper,
        Func<StorageDbContext> contextFactory,
        ITransaction transaction,
        IEntityEventService entityEvents,
        AmbientDbContext ambient) : base(mapper, contextFactory, transaction, entityEvents, ambient)
    {
    }

    public async Task<PagedResult<IApplicationError>> GetPagedNewestFirstAsync(
        int page,
        int pageSize,
        ApplicationErrorLevel? level,
        string? search,
        DateTimeOffset? from,
        DateTimeOffset? to,
        CancellationToken cancellationToken = default)
    {
        (page, pageSize) = Paging.Clamp(page, pageSize);

        var query = contextFactory().Set<ApplicationErrorEntity>().AsNoTracking();
        if (level.HasValue)
        {
            query = query.Where(e => e.Level == level.Value);
        }

        if (from.HasValue)
        {
            query = query.Where(e => e.CreatedAt >= from.Value);
        }

        if (to.HasValue)
        {
            query = query.Where(e => e.CreatedAt <= to.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            // Lower both sides and escape the user's wildcards — see LikePattern. A bare
            // EF.Functions.Like over the raw column is case-sensitive on Postgres but
            // case-insensitive on the in-memory test provider, so searching "TimeoutException"
            // silently missed rows logged as "timeoutexception" in production only.
            var pattern = LikePattern.Contains(search);
            query = query.Where(e =>
                EF.Functions.Like(e.Message.ToLower(), pattern, LikePattern.EscapeCharacter) ||
                (e.StackTrace != null
                 && EF.Functions.Like(e.StackTrace.ToLower(), pattern, LikePattern.EscapeCharacter)));
        }

        int total = await query.CountAsync(cancellationToken);
        var stored = await query
            .OrderByDescending(e => e.CreatedAt)
            .Skip(Paging.Offset(page, pageSize))
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var items = await Map(stored, cancellationToken);
        return new PagedResult<IApplicationError>(items, total, page, pageSize);
    }

    public async Task<int> RemoveOlderThanAsync(DateTimeOffset cutoffDate, CancellationToken cancellationToken = default)
    {
        var context = contextFactory();
        var query = context.Set<ApplicationErrorEntity>().Where(x => x.CreatedAt <= cutoffDate);

        if (context.Database.IsRelational())
            return await query.ExecuteDeleteAsync(cancellationToken);

        var toRemove = await query.ToListAsync(cancellationToken);
        context.Set<ApplicationErrorEntity>().RemoveRange(toRemove);
        return await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<int> TrimToNewestAsync(int max, CancellationToken cancellationToken = default)
    {
        var context = contextFactory();
        var contextSet = context.Set<ApplicationErrorEntity>();

        // The (max+1)-th newest row — the first that should be dropped. Page on the stable
        // (CreatedAt, Id) key (Id is the tiebreaker) so the boundary is deterministic even when a
        // burst of rows shares the exact same CreatedAt (timestamps truncate to the same µs). The
        // same total order is used by AuditLogRepository's paging for the same reason.
        var boundary = await contextSet
            .AsNoTracking()
            .OrderByDescending(e => e.CreatedAt)
            .ThenByDescending(e => e.Id)
            .Skip(max)
            .Select(e => new { e.CreatedAt, e.Id })
            .FirstOrDefaultAsync(cancellationToken);

        if (boundary is null)
        {
            return 0; // fewer than `max` rows — nothing to trim
        }

        // Delete everything at or below the boundary in that same total order: strictly older
        // timestamps, plus the tied-timestamp rows whose Id does not sort after the boundary's.
        // Keying off the boundary Id (not the bare timestamp) keeps exactly `max` rows instead of
        // wiping every row that shares the cutoff timestamp. Guid has no `<=` operator, so compare
        // via CompareTo — Npgsql translates it to the native uuid `<=` and the in-memory provider
        // uses Guid's IComparable, so each provider matches its own ThenByDescending(Id) order.
        var boundaryCreatedAt = boundary.CreatedAt;
        var boundaryId = boundary.Id;
        var query = contextSet.Where(x =>
            x.CreatedAt < boundaryCreatedAt ||
            (x.CreatedAt == boundaryCreatedAt && x.Id.CompareTo(boundaryId) <= 0));

        if (context.Database.IsRelational())
            return await query.ExecuteDeleteAsync(cancellationToken);

        var toRemove = await query.ToListAsync(cancellationToken);
        contextSet.RemoveRange(toRemove);
        return await context.SaveChangesAsync(cancellationToken);
    }
}
