using Microsoft.EntityFrameworkCore;
using Proxytrace.Domain;
using Nordstein.Core.Domain.Events;
using Proxytrace.Storage.Internal.Entities;

namespace Proxytrace.Storage.Internal;

/// <summary>
/// Base repository for entities that soft-delete (archive) instead of hard-deleting. Provides
/// <see cref="ArchiveAsync"/>, excludes archived rows from the list/paged queries via the
/// <see cref="FilterListQuery"/> override (so the exclusion runs in SQL and the cached snapshot
/// holds only live rows), and exposes <see cref="ArchiveRelationsAsync"/> for subclasses to clean
/// up forward-looking memberships (e.g. junction rows) in the same transaction. By-id lookups stay
/// unfiltered (inherited from <see cref="AbstractRepository{TDomainEntity,TStoredEntity}"/>) so
/// history keeps resolving.
/// </summary>
internal abstract class ArchivableRepository<TDomainEntity, TStoredEntity>
    : AbstractRepository<TDomainEntity, TStoredEntity>, IArchivableRepository<TDomainEntity>
    where TDomainEntity : class, IArchivable
    where TStoredEntity : Entity, IArchivableEntity
{
    protected ArchivableRepository(
        IMapper<TDomainEntity, TStoredEntity> mapper,
        Func<StorageDbContext> contextFactory,
        ITransaction transaction,
        IEntityEventService entityEvents,
        AmbientDbContext ambient,
        IEntityCache<TDomainEntity>? cache = null)
        : base(mapper, contextFactory, transaction, entityEvents, ambient, cache)
    {
    }

    /// <summary>
    /// Whether this archivable entity may still be hard-deleted. Defaults to <c>true</c> — archiving
    /// is the *normal* path but a hard delete remains available (e.g. <c>Agent</c>/<c>Evaluator</c>
    /// cleanup). Model-style config entities whose history would be cascade-destroyed by a hard delete
    /// (<c>ModelProvider</c>, <c>ModelEndpoint</c>) override this to <c>false</c> so <see cref="ArchiveAsync"/>
    /// is the *only* delete path — see the soft-delete section of <c>docs/domain-entities.md</c>.
    /// </summary>
    protected virtual bool SupportsHardDelete => true;

    /// <summary>
    /// Refuses a hard delete on an archive-only entity (<see cref="SupportsHardDelete"/> is
    /// <c>false</c>), redirecting the caller to <see cref="ArchiveAsync"/>. This enforces the
    /// soft-delete contract in application code, complementing the database-level <c>Restrict</c> FK
    /// (which also catches raw SQL / bulk deletes). Otherwise hard-deletes as normal.
    /// </summary>
    public override Task<bool> RemoveAsync(Guid id, CancellationToken cancellationToken = default)
        => SupportsHardDelete
            ? base.RemoveAsync(id, cancellationToken)
            : throw new InvalidOperationException(
                $"{typeof(TDomainEntity).Name} is archive-only and cannot be hard-deleted; call " +
                "ArchiveAsync instead. A hard delete would cascade-remove the history that references it.");

    /// <inheritdoc />
    public async Task<bool> ArchiveAsync(Guid id, CancellationToken cancellationToken = default)
    {
        bool archived = await transaction.InvokeAsync(async () =>
        {
            StorageDbContext context = ambient.RequireContext();
            TStoredEntity? existing = await context.Set<TStoredEntity>().FindAsync([id], cancellationToken);
            if (existing is null)
                return false;

            // Already archived: report no state transition (false), so a repeated delete is a true
            // no-op — callers return 404 and skip auditing rather than recording a phantom deletion.
            if (existing.IsArchived)
                return false;

            await ArchiveRelationsAsync(context, id, cancellationToken);

            var entry = context.Entry(existing);
            entry.CurrentValues.SetValues(existing with { IsArchived = true, UpdatedAt = DateTimeOffset.UtcNow });
            RealignConcurrencyToken(entry);
            try
            {
                await context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                // UpdatedAt is a concurrency token: a concurrent writer moved the row on after our
                // read, so report no state transition rather than clobbering their change.
                return false;
            }
            InvalidateCacheEntry(id);
            return true;
        });

        if (archived)
            Notify(id, EntityChangeType.Removed);
        return archived;
    }

    /// <summary>
    /// Reverses an archive: clears the flag so the entity reappears in list/picker queries. Used when
    /// a by-key resolver (e.g. <c>GetOrCreateAsync</c>) matches an archived row that is about to be
    /// referenced by new work — leaving it archived would create a live-but-invisible "zombie".
    /// </summary>
    protected async Task UnarchiveAsync(Guid id, CancellationToken cancellationToken = default)
    {
        bool unarchived = await transaction.InvokeAsync(async () =>
        {
            StorageDbContext context = ambient.RequireContext();
            TStoredEntity? existing = await context.Set<TStoredEntity>().FindAsync([id], cancellationToken);
            if (existing is null || !existing.IsArchived)
                return false;

            var entry = context.Entry(existing);
            entry.CurrentValues.SetValues(existing with { IsArchived = false, UpdatedAt = DateTimeOffset.UtcNow });
            RealignConcurrencyToken(entry);
            try
            {
                await context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                // A concurrent writer moved the row on after our read; skip the unarchive.
                return false;
            }
            InvalidateCacheEntry(id);
            return true;
        }, cancellationToken);

        if (unarchived)
            Notify(id, EntityChangeType.Added);
    }

    /// <summary>
    /// Cleans up forward-looking references to the entity being archived (e.g. junction rows) so it
    /// is no longer used in new work, while leaving historical references intact. Runs inside the
    /// archive transaction, before the row is flagged. Default no-op.
    /// </summary>
    protected virtual Task ArchiveRelationsAsync(
        StorageDbContext context,
        Guid id,
        CancellationToken cancellationToken)
        => Task.CompletedTask;

    /// <summary>
    /// Excludes archived rows from the list/paged queries (<c>GetAllAsync</c>/<c>GetPagedAsync</c>);
    /// by-key lookups stay unfiltered so historical references keep resolving.
    /// </summary>
    protected override IQueryable<TStoredEntity> FilterListQuery(IQueryable<TStoredEntity> query)
        => query.ExcludeArchived();
}
