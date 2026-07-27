using System.Collections.Concurrent;
using JetBrains.Annotations;
using Proxytrace.Domain;

namespace Proxytrace.Storage.Internal;

/// <summary>
/// Process-wide invalidation registry for <see cref="IEntityCache{TDomainEntity}"/>, one per domain
/// entity type. Registered as a <b>singleton</b> — unlike the caches themselves, which are
/// per-lifetime-scope.
/// </summary>
/// <remarks>
/// <para>
/// The cache cannot be a singleton: every domain entity holds the <see cref="IRepository{T}"/> it
/// was materialized from, and that repository's <c>Func&lt;StorageDbContext&gt;</c> closes over the
/// lifetime scope it was resolved in. Caching an entity in the root container would therefore hand
/// later callers an entity whose repository points at a <b>disposed</b> request scope, so
/// <c>UpdateAsync</c>/<c>ReloadAsync</c> on it would throw. That is why the cache registration is
/// <c>InstancePerLifetimeScope</c> (fc4b5f72).
/// </para>
/// <para>
/// But scope-local caches also made write-through invalidation scope-local, which is the actual
/// defect: the singleton <c>IngestionExecutor</c> resolves its repositories — and therefore its
/// cache — from the <b>root</b> scope, while an admin's write invalidates only its own
/// <b>request</b> scope. Rotating a provider's upstream API key in the admin UI left ingestion
/// authenticating against the stale key until the 5-minute TTL expired.
/// </para>
/// <para>
/// This registry closes that gap without reintroducing the lifetime bug: cached <i>instances</i>
/// stay scope-local, while the <i>validity</i> of every entry is decided by a counter shared across
/// all scopes. A write bumps the counter; every scope's copy of that entry is stale on its next read.
/// </para>
/// </remarks>
[UsedImplicitly]
internal sealed class EntityCacheVersions<TDomainEntity>
    where TDomainEntity : IDomainEntity
{
    // Bound on the per-id version map. These are slow-changing reference entities (models,
    // providers, endpoints, projects, agents, evaluators, API keys, users), so the map stays small
    // in practice; the cap only guards against unbounded growth over a long-lived process. Dropping
    // a version is always safe — a missing version reads back as 0, which no live entry can match,
    // so an eviction can only cause an extra cache miss, never a stale hit.
    private const int MaxTrackedIds = 10_000;

    private readonly ConcurrentDictionary<Guid, long> idVersions = new();
    private long allVersion;

    /// <summary>Current version of the "all entities" snapshot. Any invalidation bumps it.</summary>
    public long AllVersion => Interlocked.Read(ref allVersion);

    /// <summary>Current version of <paramref name="id"/>; 0 when it has never been invalidated.</summary>
    public long VersionOf(Guid id) => idVersions.TryGetValue(id, out long version) ? version : 0;

    /// <summary>
    /// Marks <paramref name="id"/> stale in every lifetime scope. Also bumps <see cref="AllVersion"/>,
    /// because a single-entity write invalidates any snapshot that contained it.
    /// </summary>
    public void Invalidate(Guid id)
    {
        if (idVersions.Count >= MaxTrackedIds && !idVersions.ContainsKey(id))
        {
            // Over the cap: drop the whole map rather than growing it. Every tracked entry falls back
            // to version 0 and so misses, which the AllVersion bump below already implies anyway.
            idVersions.Clear();
        }

        idVersions.AddOrUpdate(id, 1, static (_, version) => version + 1);
        Interlocked.Increment(ref allVersion);
    }

    /// <summary>Marks the "all entities" snapshot stale in every lifetime scope.</summary>
    public void InvalidateAll() => Interlocked.Increment(ref allVersion);
}
