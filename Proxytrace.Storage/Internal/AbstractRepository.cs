using System.ComponentModel.DataAnnotations;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;
using Proxytrace.Domain;
using Nordstein.Core.Domain.Events;
using Nordstein.Core.Domain.Exceptions;
using Nordstein.Core.Domain.Paging;
using Proxytrace.Storage.Internal.Entities;

namespace Proxytrace.Storage.Internal;

internal abstract class AbstractRepository<TDomainEntity, TStoredEntity> : IRepository<TDomainEntity>
    where TDomainEntity : class, IDomainEntity
    where TStoredEntity : Entity
{
    protected readonly Func<StorageDbContext> contextFactory;
    protected readonly ITransaction transaction;
    protected readonly IMapper<TDomainEntity, TStoredEntity> mapper;
    protected readonly AmbientDbContext ambient;
    private readonly IEntityCache<TDomainEntity>? cache;
    private readonly IEntityEventService entityEvents;

    protected AbstractRepository(
        IMapper<TDomainEntity, TStoredEntity> mapper,
        Func<StorageDbContext> contextFactory,
        ITransaction transaction,
        IEntityEventService entityEvents,
        AmbientDbContext ambient,
        IEntityCache<TDomainEntity>? cache = null)
    {
        this.mapper = mapper;
        this.contextFactory = contextFactory;
        this.transaction = transaction;
        this.entityEvents = entityEvents;
        this.ambient = ambient;
        this.cache = cache;
    }

    protected void Notify(Guid id, EntityChangeType change)
    {
        var changedEvent = new EntityChangedEvent(id, typeof(TDomainEntity), change);

        // Defer the notification until the outermost transaction commits. When a write runs inside a
        // larger logical unit (a nested InvokeAsync that does not commit), firing immediately would
        // tell SSE broadcasters / cache invalidators / adoption tracking about a row that a later
        // step could still roll back. When no transaction is active the action runs immediately.
        ambient.RegisterPostCommit(() => entityEvents.Notify(changedEvent));
    }

    /// <summary>
    /// Invalidate a single cached entity — now, and again once the outermost transaction commits.
    /// Use after any write, including bypass writes that do not go through the standard Add/Update
    /// path.
    /// </summary>
    /// <remarks>
    /// Invalidating only inside the transaction leaves a window. <see cref="CanUseCache"/> suppresses
    /// the cache for the *writing* flow only — <c>ambient.IsActive</c> is <c>AsyncLocal</c> — so
    /// between the invalidation and the commit a concurrent reader on another flow can miss the
    /// cache, read the still **pre-commit** row and <c>Set()</c> it back. Nothing invalidated again
    /// afterwards, so that stale entity was served until the 5-minute TTL expired. It carries a stale
    /// <c>UpdatedAt</c>, which is the optimistic-concurrency token, so every write made against it in
    /// the meantime failed the pre-check in <see cref="UpdateCoreAsync"/> — turning a millisecond
    /// race into minutes of failing writes on hot, cached, ingest-mutated entities like
    /// <c>IAgent</c> (#450). Repeating the invalidation after the commit closes the window.
    /// </remarks>
    protected void InvalidateCacheEntry(Guid id)
    {
        cache?.Invalidate(id);

        if (ambient.IsActive)
            ambient.RegisterPostCommit(() => cache?.Invalidate(id));
    }

    /// <summary>
    /// Drop the whole cache — now, and again once the outermost transaction commits, for the reason
    /// described on <see cref="InvalidateCacheEntry"/>.
    /// </summary>
    protected void InvalidateCache()
    {
        cache?.InvalidateAll();

        if (ambient.IsActive)
            ambient.RegisterPostCommit(() => cache?.InvalidateAll());
    }

    // The cache must never be read from or populated while an ambient transaction is active:
    // values read inside a transaction can reflect uncommitted writes, and populating from a
    // transaction that later rolls back would leave phantom data in the cache. Invalidation is
    // always safe and is performed unconditionally on writes.
    private bool CanUseCache
        => cache is not null && !ambient.IsActive;

    /// <inheritdoc />
    public async Task<TDomainEntity?> FindAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (CanUseCache)
        {
            TDomainEntity? cached = cache?.TryGet(id);
            if (cached is not null)
            {
                return cached;
            }
        }

        TStoredEntity? stored = await FindAsync(contextFactory(), id, cancellationToken);
        if (stored is null)
        {
            return null;
        }

        TDomainEntity domain = await mapper.Map(stored, cancellationToken);
        if (CanUseCache)
        {
            cache?.Set(domain);
        }
        return domain;
    }
    
    /// <summary>
    /// Finds the stored entity by its id
    /// Returns null if not found
    /// </summary>
    private ValueTask<TStoredEntity?> FindAsync(StorageDbContext context, Guid id, CancellationToken cancellationToken = default)
        => context.Set<TStoredEntity>().FindAsync([id], cancellationToken);

    /// <inheritdoc />
    public Task<bool> ContainsAsync(Guid id, CancellationToken cancellationToken = default)
        => Contains(id, contextFactory(), cancellationToken);
    
    private async Task<bool> Contains(Guid id, DbContext context, CancellationToken cancellationToken = default)
        => await context
            .Set<TStoredEntity>()
            .AsNoTracking()
            .AnyAsync(e => e.Id == id, cancellationToken);

    /// <inheritdoc />
    public async Task<int> CountAsync(CancellationToken cancellationToken = default)
        => await contextFactory()
            .Set<TStoredEntity>()
            .AsNoTracking()
            .CountAsync(cancellationToken);

    public async IAsyncEnumerable<TDomainEntity> EnumerateAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        IAsyncEnumerable<TStoredEntity> enumerable =  contextFactory()
            .Set<TStoredEntity>()
            .AsNoTracking()
            .AsAsyncEnumerable();

        await foreach (TStoredEntity stored in enumerable.WithCancellation(cancellationToken))
        {
            TDomainEntity domain = await mapper.Map(stored, cancellationToken);
            if (CanUseCache)
            {
                cache?.Set(domain);
            }
            yield return domain;
        }
    }

    /// <inheritdoc />
    public virtual async Task<IReadOnlyList<TDomainEntity>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        if (CanUseCache && cache?.TryGetAll() is { } snapshot)
        {
            return snapshot;
        }

        // Push list-level exclusions (e.g. archived rows) into the query via FilterListQuery — the
        // same hook GetPagedAsync uses — so the database returns only the rows GetAllAsync should
        // surface and the cached snapshot stores exactly that set, rather than materializing the
        // whole table and filtering (and caching the unfiltered set) afterwards (#253).
        var query = FilterListQuery(contextFactory().Set<TStoredEntity>().AsNoTracking());
        var stored = await query.ToListAsync(cancellationToken);
        IReadOnlyList<TDomainEntity> mapped = await Map(stored, cancellationToken);

        if (CanUseCache)
        {
            cache?.SetAll(mapped);
        }
        return mapped;
    }

    /// <summary>
    /// Hook for list queries to exclude rows that should not appear in pickers/listings (e.g.
    /// archived entities). By-key lookups never call this, so history keeps resolving. Default: no
    /// filtering; <see cref="ArchivableRepository{TDomainEntity,TStoredEntity}"/> overrides it.
    /// </summary>
    protected virtual IQueryable<TStoredEntity> FilterListQuery(IQueryable<TStoredEntity> query) => query;

    /// <inheritdoc />
    public async Task<PagedResult<TDomainEntity>> GetPagedAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        (page, pageSize) = Paging.Clamp(page, pageSize);

        var query = FilterListQuery(contextFactory().Set<TStoredEntity>().AsNoTracking());
        int total = await query.CountAsync(cancellationToken);
        var stored = await query
            .OrderByDescending(e => e.CreatedAt)
            .Skip(Paging.Offset(page, pageSize))
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        IReadOnlyList<TDomainEntity> mapped = await Map(stored, cancellationToken);
        return new PagedResult<TDomainEntity>(mapped, total, page, pageSize);
    }

    public async Task<IReadOnlyList<TDomainEntity>> GetManyAsync(
        IReadOnlyCollection<Guid> primaryKeys,
        bool ignoreMissing = false,
        CancellationToken cancellationToken = default)
    {
        primaryKeys = primaryKeys.Distinct().ToArray();

        if (CanUseCache)
        {
            var hits = new List<TDomainEntity>(primaryKeys.Count);
            var misses = new List<Guid>();
            foreach (Guid id in primaryKeys)
            {
                TDomainEntity? cached = cache?.TryGet(id);
                if (cached is not null)
                {
                    hits.Add(cached);
                }
                else
                {
                    misses.Add(id);
                }
            }

            if (misses.Count == 0)
            {
                return hits;
            }

            List<TStoredEntity> missingStored = await contextFactory()
                .Set<TStoredEntity>()
                .AsNoTracking()
                .Where(e => misses.Contains(e.Id))
                .ToListAsync(cancellationToken);

            if (!ignoreMissing && missingStored.Count != misses.Count)
            {
                throw new EntitiesNotFoundException(
                    ids: misses.Except(missingStored.Select(e => e.Id)).ToArray(),
                    entityType: typeof(TDomainEntity));
            }

            IReadOnlyList<TDomainEntity> missingMapped = await Map(missingStored, cancellationToken);
            foreach (TDomainEntity entity in missingMapped)
            {
                cache?.Set(entity);
            }

            hits.AddRange(missingMapped);
            return hits;
        }

        List<TStoredEntity> stored = await contextFactory()
            .Set<TStoredEntity>()
            .AsNoTracking()
            .Where(e => primaryKeys.Contains(e.Id))
            .ToListAsync(cancellationToken);

        if (!ignoreMissing && stored.Count != primaryKeys.Count)
        {
            throw new EntitiesNotFoundException(
                ids: primaryKeys.Except(stored.Select(e => e.Id)).ToArray(),
                entityType: typeof(TDomainEntity));
        }

        return await Map(stored, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<TDomainEntity?> FindFirstAsync(CancellationToken cancellationToken = default)
    {
        // Order by CreatedAt so "first" is deterministic (the oldest/primary row) rather than
        // whatever heap order the database returns; callers treat this as the canonical default.
        TStoredEntity? result = await contextFactory()
            .Set<TStoredEntity>()
            .AsNoTracking()
            .OrderBy(e => e.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        return await Map(result, cancellationToken);
    }

    /// <inheritdoc />
    public virtual async Task<TDomainEntity> AddAsync(TDomainEntity entity, CancellationToken cancellationToken = default)
    {
        TDomainEntity result = await transaction.InvokeAsync(() => AddCoreAsync(entity, cancellationToken));
        Notify(result.Id, EntityChangeType.Added);
        return result;
    }

    // Runs inside transaction.InvokeAsync, so the ambient transactional context is always active.
    private async Task<TDomainEntity> AddCoreAsync(
        TDomainEntity entity,
        CancellationToken cancellationToken)
    {
        StorageDbContext context = ambient.RequireContext();

        Validator.ValidateObject(entity, new ValidationContext(entity), validateAllProperties: true);

        bool exists = await Contains(entity.Id, context, cancellationToken);
        if (exists)
        {
            throw new EntityAlreadyExistsException(entity.Id, typeof(TDomainEntity));
        }

        TStoredEntity stored = await mapper.Map(entity, cancellationToken);
        EntityEntry<TStoredEntity> entry = context.Set<TStoredEntity>().Add(stored);
        await context.SaveChangesAsync(cancellationToken);
        InvalidateCacheEntry(entity.Id);
        return await mapper.Map(entry.Entity, cancellationToken);
    }

    /// <inheritdoc />
    public async Task AddRangeAsync(
        IReadOnlyCollection<TDomainEntity> entities,
        CancellationToken cancellationToken = default)
    {
        if (entities.Count == 0)
        {
            return;
        }

        await transaction.InvokeAsync(async () =>
        {
            StorageDbContext context = ambient.RequireContext();

            foreach (TDomainEntity entity in entities)
            {
                Validator.ValidateObject(entity, new ValidationContext(entity), validateAllProperties: true);
            }

            Guid[] ids = entities.Select(e => e.Id).ToArray();
            Guid existingId = await context
                .Set<TStoredEntity>()
                .AsNoTracking()
                .Where(e => ids.Contains(e.Id))
                .Select(e => e.Id)
                .FirstOrDefaultAsync(cancellationToken);
            if (existingId != Guid.Empty)
            {
                throw new EntityAlreadyExistsException(existingId, typeof(TDomainEntity));
            }

            var stored = new List<TStoredEntity>(entities.Count);
            foreach (TDomainEntity entity in entities)
            {
                stored.Add(await mapper.Map(entity, cancellationToken));
            }

            context.Set<TStoredEntity>().AddRange(stored);
            await context.SaveChangesAsync(cancellationToken);
        });

        foreach (TDomainEntity entity in entities)
        {
            InvalidateCacheEntry(entity.Id);
            Notify(entity.Id, EntityChangeType.Added);
        }
    }

    /// <inheritdoc />
    public virtual async Task<TDomainEntity> UpsertAsync(TDomainEntity entity, CancellationToken cancellationToken = default)
    {
        (TDomainEntity result, EntityChangeType change) = await transaction.InvokeAsync(async () =>
        {
            StorageDbContext context = ambient.RequireContext();

            TStoredEntity? existing = await FindAsync(context, entity.Id, cancellationToken);

            return existing is null
                ? (await AddCoreAsync(entity, cancellationToken), EntityChangeType.Added)
                : (await UpdateCoreAsync(entity, cancellationToken), EntityChangeType.Updated);
        });

        Notify(result.Id, change);
        return result;
    }

    /// <inheritdoc />
    public async Task<TDomainEntity> UpdateAsync(TDomainEntity entity, CancellationToken cancellationToken = default)
    {
        TDomainEntity result = await transaction.InvokeAsync(() => UpdateCoreAsync(entity, cancellationToken));
        Notify(result.Id, EntityChangeType.Updated);
        return result;
    }

    // Runs inside transaction.InvokeAsync, so the ambient transactional context is always active.
    private async Task<TDomainEntity> UpdateCoreAsync(
        TDomainEntity entity,
        CancellationToken cancellationToken)
    {
        StorageDbContext context = ambient.RequireContext();

        Validator.ValidateObject(entity, new ValidationContext(entity), validateAllProperties: true);

        // Find the existing entity with tracking enabled
        TStoredEntity? existing = await FindAsync(context, entity.Id, cancellationToken);

        if (existing is null)
        {
            throw new EntityNotFoundException(entity.Id, typeof(TDomainEntity));
        }

        // check for optimistic concurrency conflict — compared at the database's persisted
        // (microsecond) precision so a full-precision in-memory token (e.g. the entity AddAsync
        // returns before any DB round-trip truncates it) does not spuriously conflict on the first
        // update after an insert. See ConcurrencyTokenExtensions.
        if (!existing.UpdatedAt.MatchesConcurrencyToken(entity.UpdatedAt))
        {
            throw new OptimisticConcurrencyException(entity.Id, typeof(TDomainEntity));
        }

        // Map the updated domain entity to storage entity, then stamp UpdatedAt centrally
        // so callers don't have to bump it (and don't fight the concurrency check above).
        TStoredEntity mapped = await mapper.Map(entity, cancellationToken);
        TStoredEntity updated = mapped with { UpdatedAt = DateTimeOffset.UtcNow };

        // Update the tracked entity using EF Core's proper update mechanism
        EntityEntry<TStoredEntity> entry = context.Entry(existing);
        entry.CurrentValues.SetValues(updated);
        RealignConcurrencyToken(entry);

        // Handle owned entities manually
        UpdateOwnedEntities(entry, updated);

        await UpdateRelationsAsync(context, updated, cancellationToken);

        // UpdatedAt is configured as an EF concurrency token (see StorageDbContext.OnModelCreating),
        // so SaveChanges emits `UPDATE ... WHERE UpdatedAt = @original` and a zero-rowcount result —
        // a concurrent writer won the race — surfaces as DbUpdateConcurrencyException. The database
        // WHERE-clause check catches a genuine race that the in-app pre-check above cannot: both
        // writers read the same token, but another committed first.
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new OptimisticConcurrencyException(entity.Id, typeof(TDomainEntity), ex);
        }
        InvalidateCacheEntry(entity.Id);

        return await this.GetAsync(entity.Id, cancellationToken);
    }

    protected virtual Task UpdateRelationsAsync(
        StorageDbContext context, 
        TStoredEntity storedEntity,
        CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public virtual async Task<bool> RemoveAsync(Guid id, CancellationToken cancellationToken = default)
    {
        bool removed = await transaction.InvokeAsync(async () =>
        {
            StorageDbContext context = ambient.RequireContext();
            TStoredEntity? existing = await FindAsync(context, id, cancellationToken);
            if (existing is null)
            {
                return false;
            }

            context.Set<TStoredEntity>().Remove(existing);
            try
            {
                await context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                // UpdatedAt is a concurrency token, so the DELETE carries `WHERE UpdatedAt = @original`.
                // A concurrent writer modified or removed the row after our read, so it no longer
                // matches the version we loaded — treat it as "not removed by us" rather than throwing.
                return false;
            }
            InvalidateCacheEntry(id);
            return true;
        });

        if (removed)
        {
            Notify(id, EntityChangeType.Removed);
        }
        return removed;
    }

    /// <summary>
    /// Removes every row of this entity type.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Used by the admin data reset, which invokes it on <c>AgentCallEntity</c> — the highest-volume
    /// table in the install. <c>RemoveRange(set)</c> loaded every row into the change tracker first,
    /// so resetting an install with millions of traces meant materializing all of them just to
    /// delete them. The relational path issues a single server-side <c>DELETE</c> instead.
    /// </para>
    /// <para>
    /// The in-memory provider (kiosk/tests) cannot translate <c>ExecuteDelete</c>, so it keeps the
    /// materializing path — cheap on those datasets. See the same relational/in-memory split in
    /// <c>AgentCallRepository.RemoveOlderThanAsync</c>.
    /// </para>
    /// <para>
    /// Change notifications are still emitted per row, so ids are read before the delete. That read
    /// is a single indexed column projection rather than a full-entity materialization, and callers
    /// depend on the events (SSE, cache invalidation), so it stays.
    /// </para>
    /// </remarks>
    public async Task RemoveAllAsync(CancellationToken cancellationToken = default)
    {
        Guid[] removedIds = await transaction.InvokeAsync(async () =>
        {
            StorageDbContext context = ambient.RequireContext();
            DbSet<TStoredEntity> set = context.Set<TStoredEntity>();
            Guid[] ids = await set.AsNoTracking().Select(e => e.Id).ToArrayAsync(cancellationToken);

            if (context.Database.IsRelational())
            {
                await set.ExecuteDeleteAsync(cancellationToken);
            }
            else
            {
                set.RemoveRange(set);
                await context.SaveChangesAsync(cancellationToken);
            }

            InvalidateCache();
            return ids;
        });

        foreach (Guid id in removedIds)
        {
            Notify(id, EntityChangeType.Removed);
        }
    }

    /// <summary>
    /// Realigns the <c>UpdatedAt</c> concurrency-token original value to the precision PostgreSQL
    /// persists (microseconds) before a tracked update/archive is saved. A row inserted or updated
    /// earlier in this same context is still tracked at .NET's 100-nanosecond precision, which would
    /// otherwise spuriously fail the DB-level <c>WHERE UpdatedAt = @original</c> check against the
    /// microsecond value the database actually stored. Call after <c>SetValues</c> (which mutates the
    /// current value but leaves the original intact). See <see cref="ConcurrencyTokenExtensions"/>.
    /// <para>
    /// Only the relational provider truncates <c>UpdatedAt</c> to microseconds on persist; the EF Core
    /// in-memory provider stores the full .NET 100-nanosecond value verbatim. Truncating the tracked
    /// original value there would make EF's own concurrency check compare a truncated original against
    /// the full-precision stored value and throw <c>DbUpdateConcurrencyException</c> on every ordinary
    /// single-actor update — so the realignment is skipped on non-relational providers.
    /// </para>
    /// </summary>
    protected static void RealignConcurrencyToken(EntityEntry<TStoredEntity> entry)
    {
        if (!entry.Context.Database.IsRelational())
        {
            return;
        }

        PropertyEntry<TStoredEntity, DateTimeOffset> updatedAt = entry.Property(e => e.UpdatedAt);
        updatedAt.OriginalValue = updatedAt.OriginalValue.TruncateToMicroseconds();
    }

    /// <summary>
    /// Updates owned entities manually since SetValues doesn't handle them properly
    /// </summary>
    private void UpdateOwnedEntities(EntityEntry<TStoredEntity> entry, TStoredEntity updated)
    {
        var entityType = entry.Metadata;

        IEnumerable<INavigation> ownedNavigations = entityType
            .GetNavigations()
            .Where(n => n.ForeignKey.IsOwnership);
        foreach (var navigation in ownedNavigations)
        {
            string ownedProperty = navigation.Name;
            object? updatedOwnedValue = navigation.GetGetter().GetClrValue(updated);

            // Get the owned entity entry
            ReferenceEntry ownedEntry = entry.Reference(ownedProperty);

            if (updatedOwnedValue != null)
            {
                if (ownedEntry.CurrentValue is not null)
                {
                    // Update existing owned entity
                    EntityEntry ownedEntityEntry = ownedEntry.TargetEntry 
                                                    ?? throw new InvalidOperationException($"Owned entity entry for navigation '{ownedProperty}' is null.");
                    ownedEntityEntry.CurrentValues.SetValues(updatedOwnedValue);
                }
                else
                {
                    // Set new owned entity
                    ownedEntry.CurrentValue = updatedOwnedValue;
                }
            }
            else
            {
                // Set to null
                ownedEntry.CurrentValue = null;
            }
        }
    }

    /// <summary>
    /// Maps to the domain entity
    /// </summary>
    protected async Task<TDomainEntity?> Map(TStoredEntity? stored, CancellationToken cancellationToken = default)
        => stored is not null ? await mapper.Map(stored, cancellationToken) : null;
    
    /// <summary>
    /// Maps to the domain entity
    /// </summary>
    protected async Task<IReadOnlyList<TDomainEntity>> Map(IReadOnlyList<TStoredEntity> stored, CancellationToken cancellationToken = default)
    {
        // Sequential, not concurrent: a mapper may resolve related entities through the shared
        // ambient transaction context, and a DbContext does not allow concurrent operations.
        var mapped = new List<TDomainEntity>(stored.Count);
        foreach (TStoredEntity entity in stored)
        {
            mapped.Add(await mapper.Map(entity, cancellationToken));
        }
        return mapped;
    }

    /// <summary>
    /// Maps to the stored entity
    /// </summary>
    protected async Task<TStoredEntity?> Map(TDomainEntity? domain, CancellationToken cancellationToken = default)
        => domain is not null ? await mapper.Map(domain, cancellationToken) : null;
}
