using AwesomeAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Nordstein.Core.Common.Time;
using Proxytrace.Domain.CostLimitBreach;
using Proxytrace.Licensing;
using Proxytrace.Proxy.Internal;

namespace Proxytrace.Proxy.Tests;

[TestClass]
public sealed class CachedBudgetBlockProviderTests
{
    /// <summary>A test clock whose time can be advanced deterministically.</summary>
    private sealed class FixedClock : IClock
    {
        public FixedClock(DateTimeOffset start) => UtcNow = start;

        public DateTimeOffset UtcNow { get; set; }
    }

    [TestMethod]
    public async Task GetBlocksAsync_WithinTtl_HitsRepositoryOnce()
    {
        var projectId = Guid.NewGuid();
        var breaches = Substitute.For<ICostLimitBreachRepository>();
        breaches.GetActiveHardBlocksAsync(projectId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns([SomeBlock()]);

        CachedBudgetBlockProvider provider = NewProvider(breaches, TimeSpan.FromSeconds(30));

        IReadOnlyList<BudgetHardBlock> first = await provider.GetBlocksAsync(projectId, CancellationToken.None);
        IReadOnlyList<BudgetHardBlock> second = await provider.GetBlocksAsync(projectId, CancellationToken.None);

        first.Should().ContainSingle();
        second.Should().BeSameAs(first);
        await breaches.Received(1)
            .GetActiveHardBlocksAsync(projectId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task GetBlocksAsync_EmptyBlockList_IsCachedToo()
    {
        // Most projects have no budget at all — without negative caching every proxied request
        // would query the database.
        var projectId = Guid.NewGuid();
        var breaches = Substitute.For<ICostLimitBreachRepository>();
        breaches.GetActiveHardBlocksAsync(projectId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns([]);

        CachedBudgetBlockProvider provider = NewProvider(breaches, TimeSpan.FromSeconds(30));

        (await provider.GetBlocksAsync(projectId, CancellationToken.None)).Should().BeEmpty();
        (await provider.GetBlocksAsync(projectId, CancellationToken.None)).Should().BeEmpty();

        await breaches.Received(1)
            .GetActiveHardBlocksAsync(projectId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task GetBlocksAsync_ZeroTtl_RefetchesEveryCall()
    {
        var projectId = Guid.NewGuid();
        var breaches = Substitute.For<ICostLimitBreachRepository>();
        breaches.GetActiveHardBlocksAsync(projectId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns([SomeBlock()]);

        CachedBudgetBlockProvider provider = NewProvider(breaches, TimeSpan.Zero);

        await provider.GetBlocksAsync(projectId, CancellationToken.None);
        await provider.GetBlocksAsync(projectId, CancellationToken.None);

        await breaches.Received(2)
            .GetActiveHardBlocksAsync(projectId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task GetBlocksAsync_UnlicensedFeature_ReturnsEmptyWithoutRepositoryCall()
    {
        var breaches = Substitute.For<ICostLimitBreachRepository>();
        CachedBudgetBlockProvider provider = NewProvider(breaches, TimeSpan.FromSeconds(30), featureEnabled: false);

        IReadOnlyList<BudgetHardBlock> blocks = await provider.GetBlocksAsync(Guid.NewGuid(), CancellationToken.None);

        blocks.Should().BeEmpty();
        await breaches.DidNotReceive()
            .GetActiveHardBlocksAsync(Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task GetBlocksAsync_RepositoryError_FailsOpenAndDoesNotCacheTheFailure()
    {
        var projectId = Guid.NewGuid();
        var breaches = Substitute.For<ICostLimitBreachRepository>();
        breaches.GetActiveHardBlocksAsync(projectId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("db down"));

        CachedBudgetBlockProvider provider = NewProvider(breaches, TimeSpan.FromSeconds(30));

        // Fail-open: a budget is a cost control, not a security control — a database blip must not
        // take an organisation's LLM traffic down.
        (await provider.GetBlocksAsync(projectId, CancellationToken.None)).Should().BeEmpty();

        // The failure is NOT cached — once the database recovers, blocking resumes immediately.
        breaches.GetActiveHardBlocksAsync(projectId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns([SomeBlock()]);
        (await provider.GetBlocksAsync(projectId, CancellationToken.None)).Should().ContainSingle();
    }

    [TestMethod]
    public async Task GetBlocksAsync_QueriesTheCurrentCalendarMonthInUtc()
    {
        var projectId = Guid.NewGuid();
        var breaches = Substitute.For<ICostLimitBreachRepository>();
        breaches.GetActiveHardBlocksAsync(projectId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns([]);

        var clock = new FixedClock(new DateTimeOffset(2026, 7, 26, 20, 45, 0, TimeSpan.Zero));
        CachedBudgetBlockProvider provider = NewProvider(breaches, TimeSpan.FromSeconds(30), clock: clock);

        await provider.GetBlocksAsync(projectId, CancellationToken.None);

        await breaches.Received(1).GetActiveHardBlocksAsync(
            projectId,
            new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task GetBlocksAsync_AfterMonthRollover_RefetchesDespiteALiveCacheEntry()
    {
        var projectId = Guid.NewGuid();
        var breaches = Substitute.For<ICostLimitBreachRepository>();
        breaches.GetActiveHardBlocksAsync(projectId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns([SomeBlock()]);

        var clock = new FixedClock(new DateTimeOffset(2026, 7, 31, 23, 59, 0, TimeSpan.Zero));
        CachedBudgetBlockProvider provider = NewProvider(breaches, TimeSpan.FromHours(1), clock: clock);

        await provider.GetBlocksAsync(projectId, CancellationToken.None);

        // The month is part of the cache key, so a rollover inside the TTL cannot keep serving last
        // month's blocks — the reset must lift them on the 1st.
        clock.UtcNow = new DateTimeOffset(2026, 8, 1, 0, 1, 0, TimeSpan.Zero);
        await provider.GetBlocksAsync(projectId, CancellationToken.None);

        await breaches.Received(1).GetActiveHardBlocksAsync(
            projectId, new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero), Arg.Any<CancellationToken>());
    }

    private static CachedBudgetBlockProvider NewProvider(
        ICostLimitBreachRepository breaches,
        TimeSpan ttl,
        bool featureEnabled = true,
        IClock? clock = null)
    {
        var license = Substitute.For<ILicenseService>();
        license.IsFeatureEnabled(LicenseFeature.CostControls).Returns(featureEnabled);
        return new CachedBudgetBlockProvider(
            breaches,
            license,
            new MemoryCache(new MemoryCacheOptions()),
            clock ?? new FixedClock(DateTimeOffset.UtcNow),
            ttl,
            NullLogger<CachedBudgetBlockProvider>.Instance);
    }

    private static BudgetHardBlock SomeBlock()
        => new(Guid.NewGuid(), null, null);
}
