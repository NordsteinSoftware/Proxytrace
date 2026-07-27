using System.Collections.Concurrent;
using JetBrains.Annotations;
using Proxytrace.Domain;

namespace Proxytrace.Storage.Internal;

[UsedImplicitly]
internal sealed class EntityCache<TDomainEntity> : IEntityCache<TDomainEntity>
    where TDomainEntity : IDomainEntity
{
    // Background safety net against missed invalidations from out-of-band writes
    // (e.g. a SQL migration, another process). Write-through invalidation is the
    // primary correctness mechanism; TTL just bounds staleness if that ever fails.
    private readonly TimeSpan defaultTtl = TimeSpan.FromMinutes(5);

    private readonly TimeSpan ttl;
    private readonly TimeProvider clock;

    // Shared across every lifetime scope, so a write in one scope invalidates the copies held by
    // all the others — including the root-scope cache the singleton ingestion path reads through.
    // See EntityCacheVersions for why the entries themselves must stay scope-local.
    private readonly EntityCacheVersions<TDomainEntity> versions;

    private readonly ConcurrentDictionary<Guid, Entry> entries = new();
    private Snapshot? allSnapshot;

    // Single ctor so Autofac never has to choose. Defaults to the system clock and the
    // module-default TTL; tests construct directly with a fake TimeProvider/short TTL.
    public EntityCache(
        EntityCacheVersions<TDomainEntity> versions,
        TimeProvider? clock = null,
        TimeSpan? ttl = null)
    {
        this.versions = versions;
        this.clock = clock ?? TimeProvider.System;
        this.ttl = ttl ?? defaultTtl;
    }

    public TDomainEntity? TryGet(Guid id)
    {
        if (!entries.TryGetValue(id, out Entry? entry))
        {
            return default;
        }

        // A write in any scope bumps the shared version, so a version mismatch means this copy is
        // stale even though our own scope never saw the invalidation.
        if (IsExpired(entry.CachedAt) || entry.Version != versions.VersionOf(id))
        {
            entries.TryRemove(id, out _);
            return default;
        }

        return entry.Entity;
    }

    public void Set(TDomainEntity entity)
        => entries[entity.Id] = new Entry(entity, clock.GetUtcNow(), versions.VersionOf(entity.Id));

    public void Invalidate(Guid id)
    {
        versions.Invalidate(id);
        entries.TryRemove(id, out _);
        Volatile.Write(ref allSnapshot, null);
    }

    public IReadOnlyList<TDomainEntity>? TryGetAll()
    {
        Snapshot? snap = Volatile.Read(ref allSnapshot);
        if (snap is null)
        {
            return null;
        }

        if (IsExpired(snap.CachedAt) || snap.AllVersion != versions.AllVersion)
        {
            Volatile.Write(ref allSnapshot, null);
            return null;
        }

        return snap.Entities;
    }

    public void SetAll(IReadOnlyList<TDomainEntity> entities)
    {
        DateTimeOffset now = clock.GetUtcNow();
        long allVersion = versions.AllVersion;
        foreach (TDomainEntity entity in entities)
        {
            entries[entity.Id] = new Entry(entity, now, versions.VersionOf(entity.Id));
        }
        Volatile.Write(ref allSnapshot, new Snapshot(entities, now, allVersion));
    }

    public void InvalidateAll()
    {
        versions.InvalidateAll();
        Volatile.Write(ref allSnapshot, null);
    }

    private bool IsExpired(DateTimeOffset cachedAt)
        => clock.GetUtcNow() - cachedAt > ttl;

    private sealed record Entry(TDomainEntity Entity, DateTimeOffset CachedAt, long Version);
    private sealed record Snapshot(IReadOnlyList<TDomainEntity> Entities, DateTimeOffset CachedAt, long AllVersion);
}
