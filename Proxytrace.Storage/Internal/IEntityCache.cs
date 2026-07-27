using Proxytrace.Domain;

namespace Proxytrace.Storage.Internal;

/// <summary>
/// In-memory cache for a single domain entity type. Registered <b>per lifetime scope</b> in DI for
/// storage entities marked with <see cref="CacheableAttribute"/> — a cached domain entity holds the
/// repository it was materialized from, and that repository closes over its resolving scope, so a
/// singleton cache would serve entities bound to a disposed scope.
///
/// Invalidation is nevertheless process-wide: validity is decided by the shared singleton
/// <see cref="EntityCacheVersions{TDomainEntity}"/>, so a write in a request scope also invalidates
/// the root-scope copy that singleton services (e.g. the ingestion executor) read through.
///
/// Callers (in practice <see cref="AbstractRepository{TDomainEntity,TStoredEntity}"/>) are
/// responsible for transaction safety: do not read from or populate the cache while an
/// <see cref="AmbientDbContext"/> transaction is active. Invalidation is always safe.
/// </summary>
internal interface IEntityCache<TDomainEntity> where TDomainEntity : IDomainEntity
{
    /// <summary>Returns the cached entity for <paramref name="id"/>, or <c>null</c> if absent.</summary>
    TDomainEntity? TryGet(Guid id);

    /// <summary>Stores <paramref name="entity"/> by its <c>Id</c>, replacing any previous entry.</summary>
    void Set(TDomainEntity entity);

    /// <summary>Removes the entry for <paramref name="id"/> and clears any cached "all entities" snapshot.</summary>
    void Invalidate(Guid id);

    /// <summary>Returns the cached "all entities" snapshot, or <c>null</c> if none has been populated.</summary>
    IReadOnlyList<TDomainEntity>? TryGetAll();

    /// <summary>Replaces the "all entities" snapshot and refreshes the per-id entries.</summary>
    void SetAll(IReadOnlyList<TDomainEntity> entities);

    /// <summary>Drops the "all entities" snapshot. Per-id entries are left in place.</summary>
    void InvalidateAll();
}
