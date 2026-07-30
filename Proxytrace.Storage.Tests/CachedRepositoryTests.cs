using System.ComponentModel.DataAnnotations;
using Autofac;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Proxytrace.Domain;
using Proxytrace.Domain.AgentCall;
using Proxytrace.Domain.Model;
using Proxytrace.Domain.User;
using Proxytrace.Storage.Internal;
using Proxytrace.Storage.Internal.Entities.Model;
using Proxytrace.Testing;

namespace Proxytrace.Storage.Tests;

[TestClass]
public sealed class CachedRepositoryTests : BaseTest<Module>
{
    [TestMethod]
    public async Task FindAsync_SecondCall_ReturnsCachedValueEvenIfDbChangedUnderneath()
    {
        IServiceProvider services = GetServices();
        var repository = services.GetRequiredService<IRepository<IModel>>();
        var generator = services.GetRequiredService<IDomainEntityGenerator<IModel>>();

        IModel created = await generator.CreateAsync(CancellationToken);

        // First read — populates the cache.
        IModel first = await repository.FindAsync(created.Id, CancellationToken)
            ?? throw new InvalidOperationException("Expected first FindAsync to return entity.");
        first.Name.Should().Be(created.Name);

        // Mutate the underlying row directly via a fresh DbContext, bypassing the cache.
        await using (var ctx = services.GetRequiredService<StorageDbContext>())
        {
            var stored = await ctx.Set<ModelEntity>().FirstAsync(e => e.Id == created.Id, CancellationToken);
            ctx.Entry(stored).CurrentValues.SetValues(stored with { Name = "CHANGED-IN-DB" });
            await ctx.SaveChangesAsync(CancellationToken);
        }

        // Second read — must come from the cache (proves the cache is actually serving reads).
        IModel second = await repository.FindAsync(created.Id, CancellationToken)
            ?? throw new InvalidOperationException("Expected second FindAsync to return cached entity.");
        second.Name.Should().Be(created.Name);
    }

    [TestMethod]
    public async Task UpdateAsync_AfterCachePopulated_ReturnsUpdatedValue()
    {
        IServiceProvider services = GetServices();
        var repository = services.GetRequiredService<IRepository<IModel>>();
        var generator = services.GetRequiredService<IDomainEntityGenerator<IModel>>();
        var createExisting = services.GetRequiredService<IModel.CreateExisting>();

        IModel created = await generator.CreateAsync(CancellationToken);
        // Populate cache.
        await repository.FindAsync(created.Id, CancellationToken);

        IModel updated = createExisting("renamed", created);
        await repository.UpdateAsync(updated, CancellationToken);

        IModel after = await repository.FindAsync(created.Id, CancellationToken)
            ?? throw new InvalidOperationException("Expected FindAsync after update to return entity.");
        after.Name.Should().Be("renamed");
    }

    [TestMethod]
    public async Task RemoveAsync_AfterCachePopulated_SubsequentFindReturnsNull()
    {
        IServiceProvider services = GetServices();
        var repository = services.GetRequiredService<IRepository<IModel>>();
        var generator = services.GetRequiredService<IDomainEntityGenerator<IModel>>();

        IModel created = await generator.CreateAsync(CancellationToken);
        await repository.FindAsync(created.Id, CancellationToken);

        await repository.RemoveAsync(created.Id, CancellationToken);

        IModel? after = await repository.FindAsync(created.Id, CancellationToken);
        after.Should().BeNull();
    }

    [TestMethod]
    public async Task GetAllAsync_AfterPopulating_ReturnsCachedSnapshotEvenIfDbChanges()
    {
        IServiceProvider services = GetServices();
        var repository = services.GetRequiredService<IRepository<IModel>>();
        var generator = services.GetRequiredService<IDomainEntityGenerator<IModel>>();

        await generator.CreateAsync(CancellationToken);
        await generator.CreateAsync(CancellationToken);

        IReadOnlyList<IModel> first = await repository.GetAllAsync(CancellationToken);
        first.Should().HaveCount(2);

        // Insert directly via the DbContext so the repository (and its cache) don't observe it.
        await using (var ctx = services.GetRequiredService<StorageDbContext>())
        {
            var now = DateTimeOffset.UtcNow.AddSeconds(-1);
            ctx.Set<ModelEntity>().Add(new ModelEntity
            {
                Id = Guid.NewGuid(),
                Name = "smuggled",
                CreatedAt = now,
                UpdatedAt = now,
            });
            await ctx.SaveChangesAsync(CancellationToken);
        }

        IReadOnlyList<IModel> second = await repository.GetAllAsync(CancellationToken);
        second.Should().HaveCount(2);
    }

    [TestMethod]
    public async Task GetAllAsync_AfterAdd_ReturnsFreshSnapshotIncludingNewEntity()
    {
        IServiceProvider services = GetServices();
        var repository = services.GetRequiredService<IRepository<IModel>>();
        var generator = services.GetRequiredService<IDomainEntityGenerator<IModel>>();

        await generator.CreateAsync(CancellationToken);
        IReadOnlyList<IModel> first = await repository.GetAllAsync(CancellationToken);
        first.Should().HaveCount(1);

        await generator.CreateAsync(CancellationToken);

        IReadOnlyList<IModel> second = await repository.GetAllAsync(CancellationToken);
        second.Should().HaveCount(2);
    }

    [TestMethod]
    public async Task NonCacheableEntity_RoundTripsThroughRepositoryAsBefore()
    {
        // Users are not [Cacheable]. Verify the non-cached path still works end-to-end so
        // high-volume entities are unaffected by the caching changes.
        IServiceProvider services = GetServices();
        var repository = services.GetRequiredService<IRepository<IUser>>();
        var generator = services.GetRequiredService<IDomainEntityGenerator<IUser>>();
        var createExisting = services.GetRequiredService<IUser.CreateExisting>();

        IUser created = await generator.CreateAsync(CancellationToken);
        IUser loaded = await repository.FindAsync(created.Id, CancellationToken)
            ?? throw new InvalidOperationException("Expected FindAsync to return user.");
        loaded.Email.Should().Be(created.Email);

        await repository.UpdateAsync(createExisting("renamed@example.com", created.ExternalSubject, created.PasswordHash, created.Role, created.Language, created.EmailNotificationsEnabled, created.EmailNotificationMinSeverity, created), CancellationToken);
        IUser updated = await repository.FindAsync(created.Id, CancellationToken)
            ?? throw new InvalidOperationException("Expected FindAsync after update to return user.");
        updated.Email.Should().Be("renamed@example.com");

        await repository.RemoveAsync(created.Id, CancellationToken);
        (await repository.FindAsync(created.Id, CancellationToken)).Should().BeNull();
    }

    [TestMethod]
    public void NonCacheableEntity_HasNoCacheRegistered()
    {
        // Cache registration is opt-in via [Cacheable]. Sanity-check that non-cacheable
        // domain types resolve no IEntityCache<T> binding.
        IServiceProvider services = GetServices();
        services.GetService<IEntityCache<IAgentCall>>().Should().BeNull();
        services.GetService<IEntityCache<IModel>>().Should().NotBeNull();
    }

    [TestMethod]
    public async Task FindAsync_InsideTransaction_DoesNotPopulateCache()
    {
        IServiceProvider services = GetServices();
        var repository = services.GetRequiredService<IRepository<IModel>>();
        var generator = services.GetRequiredService<IDomainEntityGenerator<IModel>>();
        var cache = services.GetRequiredService<IEntityCache<IModel>>();
        var transaction = services.GetRequiredService<ITransaction>();

        IModel created = await generator.CreateAsync(CancellationToken);
        // Be sure the cache is empty for this id (CreateAsync invalidates after the write).
        cache.TryGet(created.Id).Should().BeNull();

        // Read inside an active logical transaction. A value read while a transaction is in
        // progress could reflect uncommitted writes, so it must never be promoted to the cache.
        await transaction.InvokeAsync(async () =>
        {
            await repository.FindAsync(created.Id, CancellationToken);
        });

        cache.TryGet(created.Id).Should().BeNull();
    }

    [TestMethod]
    public void EntityCache_TtlExpiry_EvictsStaleEntriesAndSnapshots()
    {
        // Direct unit test of the cache itself with a fake clock.
        var clock = new FakeTimeProvider(new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var cache = new EntityCache<IModel>(new EntityCacheVersions<IModel>(), clock, TimeSpan.FromMinutes(1));
        var model = new StubModel(Guid.NewGuid(), "m1");

        cache.Set(model);
        cache.SetAll([model]);
        cache.TryGet(model.Id).Should().NotBeNull();
        cache.TryGetAll().Should().NotBeNull();

        clock.Advance(TimeSpan.FromMinutes(2));

        cache.TryGet(model.Id).Should().BeNull("entry exceeds TTL");
        cache.TryGetAll().Should().BeNull("snapshot exceeds TTL");
    }

    [TestMethod]
    public async Task UpdateAsync_InvalidatesCacheUnconditionally()
    {
        // Writes always invalidate (even though they run inside transaction.InvokeAsync's
        // ambient scope) — invalidation is monotonically safe under both commit and rollback,
        // and the post-write GetAsync reload runs inside the scope so it does not repopulate.
        IServiceProvider services = GetServices();
        var repository = services.GetRequiredService<IRepository<IModel>>();
        var generator = services.GetRequiredService<IDomainEntityGenerator<IModel>>();
        var createExisting = services.GetRequiredService<IModel.CreateExisting>();
        var cache = services.GetRequiredService<IEntityCache<IModel>>();

        IModel created = await generator.CreateAsync(CancellationToken);
        await repository.FindAsync(created.Id, CancellationToken);
        cache.TryGet(created.Id).Should().NotBeNull();

        await repository.UpdateAsync(createExisting("after-update", created), CancellationToken);

        cache.TryGet(created.Id).Should().BeNull("the write must invalidate the cache entry");
    }

    [TestMethod]
    public async Task UpdateAsync_WhenAConcurrentReaderRepopulatesMidTransaction_TheEntryIsDroppedAfterCommit()
    {
        // The race behind #450: invalidation ran inside the still-open transaction, and CanUseCache
        // suppresses the cache for the *writing* flow only (ambient.IsActive is AsyncLocal). Between
        // that invalidation and the commit, a reader on another flow could miss the cache, read the
        // pre-commit row and Set() it back — with nothing invalidating afterwards, the stale entity
        // and its stale concurrency token were served until the 5-minute TTL expired, failing every
        // write made against it in between.
        IServiceProvider services = GetServices();
        var repository = services.GetRequiredService<IRepository<IModel>>();
        var generator = services.GetRequiredService<IDomainEntityGenerator<IModel>>();
        var createExisting = services.GetRequiredService<IModel.CreateExisting>();
        var cache = services.GetRequiredService<IEntityCache<IModel>>();
        var transaction = services.GetRequiredService<ITransaction>();

        IModel created = await generator.CreateAsync(CancellationToken);

        await transaction.InvokeAsync(async () =>
        {
            await repository.UpdateAsync(createExisting("after-update", created), CancellationToken);

            // Suppressing the ExecutionContext flow keeps the ambient transaction's AsyncLocal out of
            // the spawned task, so that read sees CanUseCache as true and repopulates the cache —
            // exactly what the racing reader does.
            Task<IModel?> concurrentRead;
            using (ExecutionContext.SuppressFlow())
            {
                // Started inside the suppression and awaited outside it: AsyncFlowControl must be
                // disposed on the thread that created it, which an await in between cannot promise.
                concurrentRead = Task.Run(() => repository.FindAsync(created.Id, CancellationToken));
            }

            await concurrentRead;

            cache.TryGet(created.Id).Should().NotBeNull("the concurrent reader is expected to have repopulated the cache");
        });

        cache.TryGet(created.Id).Should().BeNull("committing must invalidate again, dropping whatever the reader cached");

        IModel reloaded = await repository.FindAsync(created.Id, CancellationToken)
                          ?? throw new InvalidOperationException("Expected the updated model to be readable.");
        reloaded.Name.Should().Be("after-update");
    }

    [TestMethod]
    public async Task Write_InOneLifetimeScope_InvalidatesTheCacheHeldByAnother()
    {
        // The #8 failure: caches are per-lifetime-scope, so an admin's write invalidated only its
        // own request scope while the singleton ingestion path kept reading its ROOT-scope copy —
        // e.g. rotating a provider's upstream API key left ingestion authenticating against the
        // stale key until the 5-minute TTL expired. The cache must stay scope-local (its entries
        // hold repositories bound to the resolving scope), so invalidation is instead shared
        // process-wide via EntityCacheVersions.
        IServiceProvider services = GetServices();
        var root = services.GetRequiredService<ILifetimeScope>();
        var generator = services.GetRequiredService<IDomainEntityGenerator<IModel>>();
        var createExisting = services.GetRequiredService<IModel.CreateExisting>();

        IModel created = await generator.CreateAsync(CancellationToken);

        await using var readerScope = root.BeginLifetimeScope();
        await using var writerScope = root.BeginLifetimeScope();

        // Reader populates its own scope's cache.
        IModel before = await readerScope.Resolve<IRepository<IModel>>().FindAsync(created.Id, CancellationToken)
            ?? throw new InvalidOperationException("Expected the reader scope to load the model.");
        before.Name.Should().Be(created.Name);

        // Writer commits in a different scope entirely — it never touches the reader's cache instance.
        await writerScope.Resolve<IRepository<IModel>>()
            .UpdateAsync(createExisting("rotated", created), CancellationToken);

        IModel after = await readerScope.Resolve<IRepository<IModel>>().FindAsync(created.Id, CancellationToken)
            ?? throw new InvalidOperationException("Expected the reader scope to reload the model.");
        after.Name.Should().Be("rotated", "a write in any scope must invalidate every scope's cached copy");
    }

    [TestMethod]
    public async Task GetAllAsync_AfterAWriteInAnotherLifetimeScope_ReturnsAFreshSnapshot()
    {
        // Same cross-scope guarantee for the "all entities" snapshot, which backs the list reads.
        IServiceProvider services = GetServices();
        var root = services.GetRequiredService<ILifetimeScope>();
        var generator = services.GetRequiredService<IDomainEntityGenerator<IModel>>();

        await generator.CreateAsync(CancellationToken);

        await using var readerScope = root.BeginLifetimeScope();
        await using var writerScope = root.BeginLifetimeScope();

        IReadOnlyList<IModel> first = await readerScope.Resolve<IRepository<IModel>>().GetAllAsync(CancellationToken);
        first.Should().HaveCount(1);

        var added = await writerScope.Resolve<IDomainEntityGenerator<IModel>>().GenerateAsync(CancellationToken);
        await writerScope.Resolve<IRepository<IModel>>().AddAsync(added, CancellationToken);

        IReadOnlyList<IModel> second = await readerScope.Resolve<IRepository<IModel>>().GetAllAsync(CancellationToken);
        second.Should().HaveCount(2, "an add in any scope must drop every scope's cached snapshot");
    }

    [TestMethod]
    public void EntityCacheVersions_WhenTheTrackedIdCapIsExceeded_StillReportsEveryEntryStale()
    {
        // The per-id map is bounded. Dropping a version must fail safe: a forgotten id reads back as
        // version 0, which no live entry matches, so eviction can only cause an extra miss.
        var versions = new EntityCacheVersions<IModel>();
        var first = Guid.NewGuid();

        versions.Invalidate(first);
        long trackedVersion = versions.VersionOf(first);
        trackedVersion.Should().BeGreaterThan(0);

        for (var i = 0; i < 10_000; i++)
        {
            versions.Invalidate(Guid.NewGuid());
        }

        versions.VersionOf(first).Should().NotBe(trackedVersion,
            "an evicted id must not keep matching an entry cached under its old version");
    }

    private sealed class FakeTimeProvider : TimeProvider
    {
        private DateTimeOffset now;
        public FakeTimeProvider(DateTimeOffset start) => now = start;
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan by) => now = now.Add(by);
    }

    private sealed record StubModel(Guid Id, string Name) : IModel
    {
        public DateTimeOffset CreatedAt { get; } = DateTimeOffset.UtcNow.AddMinutes(-1);
        public DateTimeOffset UpdatedAt { get; } = DateTimeOffset.UtcNow.AddMinutes(-1);
        public IEnumerable<ValidationResult> Validate(
            ValidationContext validationContext) => [];
    }
}
