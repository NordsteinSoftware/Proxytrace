using System.Collections.Concurrent;
using Autofac;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Proxytrace.Domain.ModelEndpoint;
using Proxytrace.Domain.TestRunSchedule;
using Proxytrace.Domain.TestSuite;

namespace Proxytrace.Domain.Tests;

[TestClass]
public sealed class TestRunScheduleValidationTests : DomainTest<Module>
{
    // The storage (five-file) implementation for ITestRunSchedule is out of scope for this phase,
    // so there is no real IRepository<ITestRunSchedule> registration yet. These domain tests
    // isolate the entity from persistence with an in-memory substitute repository (per the test
    // skill's "substitute IRepository<T> to isolate the SUT from persistence" guidance).
    private static IRepository<ITestRunSchedule> BuildRepository(ConcurrentDictionary<Guid, ITestRunSchedule> store)
    {
        var repo = Substitute.For<IRepository<ITestRunSchedule>>();
        repo.AddAsync(Arg.Any<ITestRunSchedule>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var entity = call.Arg<ITestRunSchedule>();
                ArgumentNullException.ThrowIfNull(entity);
                store[entity.Id] = entity;
                return Task.FromResult(entity);
            });
        repo.UpdateAsync(Arg.Any<ITestRunSchedule>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var entity = call.Arg<ITestRunSchedule>();
                ArgumentNullException.ThrowIfNull(entity);
                store[entity.Id] = entity;
                return Task.FromResult(entity);
            });
        repo.GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(store[call.Arg<Guid>()]));
        return repo;
    }

    private IServiceProvider GetServicesWithRepository()
    {
        var backing = new ConcurrentDictionary<Guid, ITestRunSchedule>();
        return GetServices(builder =>
            builder.RegisterInstance(BuildRepository(backing)).As<IRepository<ITestRunSchedule>>());
    }

    // ── factory / construction ────────────────────────────────────────────────

    [TestMethod]
    public async Task CreateNew_WithValidInputs_CreatesSchedule()
    {
        IServiceProvider services = GetServicesWithRepository();
        var factory = services.GetRequiredService<ITestRunSchedule.CreateNew>();
        var suite = await GetOrCreate<ITestSuite>(services);
        var endpoint = await GetOrCreate<IModelEndpoint>(services);

        var anchor = DateTimeOffset.UtcNow;
        var schedule = factory("Nightly", suite, [endpoint], TimeSpan.FromHours(1), true, anchor);

        schedule.Should().NotBeNull();
        schedule.Name.Should().Be("Nightly");
        schedule.Suite.Should().Be(suite);
        schedule.Endpoints.Should().ContainSingle().Which.Should().Be(endpoint);
        schedule.Interval.Should().Be(TimeSpan.FromHours(1));
        schedule.IsEnabled.Should().BeTrue();
        schedule.AnchorAt.Should().Be(anchor);
        schedule.LastRunAt.Should().BeNull();
        schedule.NextRunAt.Should().BeCloseTo(schedule.CreatedAt + TimeSpan.FromHours(1), TimeSpan.FromSeconds(5));
        schedule.Id.Should().NotBe(Guid.Empty);
        schedule.CreatedAt.Should().NotBe(default);
        schedule.UpdatedAt.Should().NotBe(default);
    }

    [TestMethod]
    public async Task CreateNew_WithFutureAnchor_FirstRunIsTheAnchor()
    {
        IServiceProvider services = GetServicesWithRepository();
        var factory = services.GetRequiredService<ITestRunSchedule.CreateNew>();
        var suite = await GetOrCreate<ITestSuite>(services);
        var endpoint = await GetOrCreate<IModelEndpoint>(services);

        // Anchor three hours out, daily cadence → the first fire is exactly the anchor (k = 0).
        var anchor = DateTimeOffset.UtcNow.AddHours(3);
        var schedule = factory("Nightly", suite, [endpoint], TimeSpan.FromDays(1), true, anchor);

        schedule.NextRunAt.Should().Be(anchor);
    }

    [TestMethod]
    public async Task CreateNew_WithPastAnchor_AlignsNextRunToTheAnchorTimeOfDay()
    {
        IServiceProvider services = GetServicesWithRepository();
        var factory = services.GetRequiredService<ITestRunSchedule.CreateNew>();
        var suite = await GetOrCreate<ITestSuite>(services);
        var endpoint = await GetOrCreate<IModelEndpoint>(services);

        // Anchor 25h in the past with a daily cadence: the next fire is a whole number of days after
        // the anchor (so the same clock time) and strictly in the future.
        var anchor = DateTimeOffset.UtcNow.AddHours(-25);
        var day = TimeSpan.FromDays(1);
        var schedule = factory("Nightly", suite, [endpoint], day, true, anchor);

        schedule.NextRunAt.Should().BeAfter(DateTimeOffset.UtcNow);
        ((schedule.NextRunAt - anchor).Ticks % day.Ticks).Should().Be(0);
    }

    [TestMethod]
    public async Task CreateNew_WithNonUtcOffsetPastAnchor_FirstRunIsStrictlyInTheFuture()
    {
        IServiceProvider services = GetServicesWithRepository();
        var factory = services.GetRequiredService<ITestRunSchedule.CreateNew>();
        var suite = await GetOrCreate<ITestSuite>(services);
        var endpoint = await GetOrCreate<IModelEndpoint>(services);

        // Same instant, expressed at +02:00: DateTimeOffset.Ticks is the wall-clock reading, so it
        // reads two hours higher than UtcTicks. Offset-naive alignment therefore loses a whole step
        // and puts the first fire an hour in the PAST — which the scheduler then re-derives on every
        // 60s poll, firing (and LLM-billing) the suite once a minute.
        var day = TimeSpan.FromDays(1);
        var anchor = DateTimeOffset.UtcNow.AddDays(-30).AddHours(-1).ToOffset(TimeSpan.FromHours(2));

        var schedule = factory("Nightly", suite, [endpoint], day, true, anchor);

        schedule.NextRunAt.Should().BeAfter(schedule.CreatedAt);
        schedule.NextRunAt.Should().BeAfter(DateTimeOffset.UtcNow);
        ((schedule.NextRunAt - anchor).Ticks % day.Ticks).Should().Be(0);
    }

    [TestMethod]
    public async Task CreateNew_IdsAreUniquePerInstance()
    {
        IServiceProvider services = GetServicesWithRepository();
        var factory = services.GetRequiredService<ITestRunSchedule.CreateNew>();
        var suite = await GetOrCreate<ITestSuite>(services);
        var endpoint = await GetOrCreate<IModelEndpoint>(services);

        var now = DateTimeOffset.UtcNow;
        var first = factory("Nightly", suite, [endpoint], TimeSpan.FromHours(1), true, now);
        var second = factory("Nightly", suite, [endpoint], TimeSpan.FromHours(1), true, now);

        first.Id.Should().NotBe(second.Id);
    }

    [TestMethod]
    public async Task CreateExisting_RestoresAllProperties()
    {
        IServiceProvider services = GetServicesWithRepository();
        var createNew = services.GetRequiredService<ITestRunSchedule.CreateNew>();
        var createExisting = services.GetRequiredService<ITestRunSchedule.CreateExisting>();
        var suite = await GetOrCreate<ITestSuite>(services);
        var endpoint = await GetOrCreate<IModelEndpoint>(services);
        var original = createNew("Nightly", suite, [endpoint], TimeSpan.FromHours(2), true, DateTimeOffset.UtcNow);

        var schedule = createExisting(
            original.Name, original.Suite, original.Endpoints, original.Interval, original.IsEnabled,
            original.AnchorAt, original.NextRunAt, original.LastRunAt, original);

        schedule.Id.Should().Be(original.Id);
        schedule.Name.Should().Be(original.Name);
        schedule.Suite.Should().Be(original.Suite);
        schedule.Interval.Should().Be(original.Interval);
        schedule.IsEnabled.Should().Be(original.IsEnabled);
        schedule.AnchorAt.Should().Be(original.AnchorAt);
        schedule.NextRunAt.Should().Be(original.NextRunAt);
        schedule.LastRunAt.Should().Be(original.LastRunAt);
        schedule.CreatedAt.Should().Be(original.CreatedAt);
        schedule.UpdatedAt.Should().Be(original.UpdatedAt);
    }

    // ── validation failures ──────────────────────────────────────────────────

    [TestMethod]
    public async Task CreateNew_WithWhitespaceName_ThrowsValidationException()
    {
        IServiceProvider services = GetServicesWithRepository();
        var factory = services.GetRequiredService<ITestRunSchedule.CreateNew>();
        var suite = await GetOrCreate<ITestSuite>(services);
        var endpoint = await GetOrCreate<IModelEndpoint>(services);

        var action = () => factory("   ", suite, [endpoint], TimeSpan.FromHours(1), true, DateTimeOffset.UtcNow);

        action.Should().Throw<Exception>();
    }

    [TestMethod]
    public async Task CreateNew_WithSubMinuteInterval_ThrowsValidationException()
    {
        IServiceProvider services = GetServicesWithRepository();
        var factory = services.GetRequiredService<ITestRunSchedule.CreateNew>();
        var suite = await GetOrCreate<ITestSuite>(services);
        var endpoint = await GetOrCreate<IModelEndpoint>(services);

        var action = () => factory("Nightly", suite, [endpoint], TimeSpan.FromSeconds(30), true, DateTimeOffset.UtcNow);

        action.Should().Throw<Exception>();
    }

    [TestMethod]
    public async Task CreateNew_WithEmptyEndpoints_ThrowsValidationException()
    {
        IServiceProvider services = GetServicesWithRepository();
        var factory = services.GetRequiredService<ITestRunSchedule.CreateNew>();
        var suite = await GetOrCreate<ITestSuite>(services);

        var action = () => factory("Nightly", suite, [], TimeSpan.FromHours(1), true, DateTimeOffset.UtcNow);

        action.Should().Throw<Exception>();
    }

    [TestMethod]
    public async Task CreateNew_WithThreeEndpoints_CreatesSchedule()
    {
        IServiceProvider services = GetServicesWithRepository();
        var factory = services.GetRequiredService<ITestRunSchedule.CreateNew>();
        var suite = await GetOrCreate<ITestSuite>(services);
        var endpoints = await CreateEndpoints(services, 3);

        var schedule = factory("Nightly", suite, endpoints, TimeSpan.FromHours(1), true, DateTimeOffset.UtcNow);

        schedule.Endpoints.Should().HaveCount(3);
    }

    [TestMethod]
    public async Task CreateNew_WithMoreThanThreeEndpoints_ThrowsValidationException()
    {
        IServiceProvider services = GetServicesWithRepository();
        var factory = services.GetRequiredService<ITestRunSchedule.CreateNew>();
        var suite = await GetOrCreate<ITestSuite>(services);
        var endpoints = await CreateEndpoints(services, 4);

        var action = () => factory("Nightly", suite, endpoints, TimeSpan.FromHours(1), true, DateTimeOffset.UtcNow);

        action.Should().Throw<Exception>();
    }

    [TestMethod]
    public async Task Update_WithMoreThanThreeEndpoints_ThrowsValidationException()
    {
        IServiceProvider services = GetServicesWithRepository();
        var schedule = await CreateSchedule(services, interval: TimeSpan.FromHours(1));
        var endpoints = await CreateEndpoints(services, 4);

        await FluentActions
            .Invoking(() => schedule.Update(
                "Nightly", endpoints, TimeSpan.FromHours(1), true, schedule.AnchorAt, DateTimeOffset.UtcNow,
                CancellationToken))
            .Should().ThrowAsync<Exception>();
    }

    // ── RecordFired ───────────────────────────────────────────────────────────

    [TestMethod]
    public async Task RecordFired_WithManyMissedIntervals_CollapsesIntoSingleForwardJump()
    {
        IServiceProvider services = GetServicesWithRepository();
        var schedule = await CreateSchedule(services, interval: TimeSpan.FromHours(1));

        // NextRunAt starts at CreatedAt + 1h (in the recent past). Fire well into the future so
        // many whole intervals have elapsed.
        var now = DateTimeOffset.UtcNow.AddDays(10);

        var fired = await schedule.RecordFired(now, CancellationToken);

        fired.LastRunAt.Should().Be(now);
        fired.NextRunAt.Should().BeAfter(now);
        (fired.NextRunAt - now).Should().BeLessThanOrEqualTo(TimeSpan.FromHours(1));
    }

    [TestMethod]
    public async Task RecordFired_WithNonUtcOffsetAnchor_AdvancesNextRunStrictlyAfterNow()
    {
        IServiceProvider services = GetServicesWithRepository();
        var factory = services.GetRequiredService<ITestRunSchedule.CreateNew>();
        var repo = services.GetRequiredService<IRepository<ITestRunSchedule>>();
        var suite = await GetOrCreate<ITestSuite>(services);
        var endpoint = await GetOrCreate<IModelEndpoint>(services);

        // Daily schedule anchored at 09:00+02:00 (= 07:00Z), fired five seconds past 07:00Z. Naive
        // tick subtraction understates the gap by the 2h offset, drops a step, and yields 07:00Z —
        // five seconds in the past — so the schedule stays perpetually due.
        var anchor = new DateTimeOffset(2026, 7, 1, 9, 0, 0, TimeSpan.FromHours(2));
        var now = new DateTimeOffset(2026, 7, 27, 7, 0, 5, TimeSpan.Zero);
        var schedule = await repo.AddAsync(
            factory("Nightly", suite, [endpoint], TimeSpan.FromDays(1), true, anchor), CancellationToken);

        var fired = await schedule.RecordFired(now, CancellationToken);

        fired.NextRunAt.Should().BeAfter(now);
        fired.NextRunAt.Should().Be(new DateTimeOffset(2026, 7, 28, 9, 0, 0, TimeSpan.FromHours(2)));
    }

    [TestMethod]
    public async Task RecordFired_PersistsLastRunAndNextRun()
    {
        IServiceProvider services = GetServicesWithRepository();
        var repo = services.GetRequiredService<IRepository<ITestRunSchedule>>();
        var schedule = await CreateSchedule(services, interval: TimeSpan.FromHours(1));

        var now = DateTimeOffset.UtcNow.AddDays(1);
        await schedule.RecordFired(now, CancellationToken);

        var reloaded = await repo.GetAsync(schedule.Id, CancellationToken);
        reloaded.LastRunAt.Should().Be(now);
        reloaded.NextRunAt.Should().BeAfter(now);
    }

    // ── enable / disable ──────────────────────────────────────────────────────

    [TestMethod]
    public async Task DisableThenEnable_TogglesIsEnabled()
    {
        IServiceProvider services = GetServicesWithRepository();
        var schedule = await CreateSchedule(services, interval: TimeSpan.FromHours(1));

        var disabled = await schedule.Disable(CancellationToken);
        disabled.IsEnabled.Should().BeFalse();

        var enabled = await disabled.Enable(CancellationToken);
        enabled.IsEnabled.Should().BeTrue();
    }

    [TestMethod]
    public async Task Disable_PersistsDisabledState()
    {
        IServiceProvider services = GetServicesWithRepository();
        var repo = services.GetRequiredService<IRepository<ITestRunSchedule>>();
        var schedule = await CreateSchedule(services, interval: TimeSpan.FromHours(1));

        await schedule.Disable(CancellationToken);

        var reloaded = await repo.GetAsync(schedule.Id, CancellationToken);
        reloaded.IsEnabled.Should().BeFalse();
    }

    // ── update ────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task Update_ChangesNameIntervalAndEnabled()
    {
        IServiceProvider services = GetServicesWithRepository();
        var endpoint = await GetOrCreate<IModelEndpoint>(services);
        var schedule = await CreateSchedule(services, interval: TimeSpan.FromHours(1));

        var updated = await schedule.Update(
            "Renamed", [endpoint], TimeSpan.FromHours(6), false, schedule.AnchorAt, DateTimeOffset.UtcNow,
            CancellationToken);

        updated.Name.Should().Be("Renamed");
        updated.Interval.Should().Be(TimeSpan.FromHours(6));
        updated.IsEnabled.Should().BeFalse();
    }

    [TestMethod]
    public async Task Update_WithNewAnchor_RecomputesNextRunToTheNewAnchor()
    {
        IServiceProvider services = GetServicesWithRepository();
        var endpoint = await GetOrCreate<IModelEndpoint>(services);
        var schedule = await CreateSchedule(services, interval: TimeSpan.FromHours(1));

        // Re-anchor to two hours out with a daily cadence → next run is exactly the new anchor.
        var newAnchor = DateTimeOffset.UtcNow.AddHours(2);
        var updated = await schedule.Update(
            "Nightly", [endpoint], TimeSpan.FromDays(1), true, newAnchor, DateTimeOffset.UtcNow, CancellationToken);

        updated.AnchorAt.Should().Be(newAnchor);
        updated.NextRunAt.Should().Be(newAnchor);
    }

    [TestMethod]
    public async Task Update_WithNonUtcOffsetPastAnchor_RecomputesNextRunStrictlyAfterNow()
    {
        IServiceProvider services = GetServicesWithRepository();
        var endpoint = await GetOrCreate<IModelEndpoint>(services);
        var schedule = await CreateSchedule(services, interval: TimeSpan.FromHours(1));

        // Re-anchor onto a +02:00 wall-clock time whose instant is a whole number of days before
        // `now`; the third AlignForward call site must be offset-aware too.
        var anchor = new DateTimeOffset(2026, 7, 1, 9, 0, 0, TimeSpan.FromHours(2));
        var now = new DateTimeOffset(2026, 7, 27, 7, 0, 5, TimeSpan.Zero);

        var updated = await schedule.Update(
            "Nightly", [endpoint], TimeSpan.FromDays(1), true, anchor, now, CancellationToken);

        updated.NextRunAt.Should().BeAfter(now);
    }

    [TestMethod]
    public async Task Update_WhenCadenceUnchanged_PreservesOverdueNextRun()
    {
        IServiceProvider services = GetServicesWithRepository();
        var createNew = services.GetRequiredService<ITestRunSchedule.CreateNew>();
        var createExisting = services.GetRequiredService<ITestRunSchedule.CreateExisting>();
        var suite = await GetOrCreate<ITestSuite>(services);
        var endpoint = await GetOrCreate<IModelEndpoint>(services);

        var anchor = DateTimeOffset.UtcNow.AddHours(-2);
        var original = createNew("Nightly", suite, [endpoint], TimeSpan.FromHours(1), true, anchor);

        // Reconstitute with an OVERDUE NextRunAt — a fire is due but the tick hasn't run, or a prior
        // run is still in flight (same anchor + interval).
        var overdueNext = DateTimeOffset.UtcNow.AddMinutes(-30);
        var overdue = createExisting(
            original.Name, original.Suite, original.Endpoints, original.Interval, original.IsEnabled,
            original.AnchorAt, overdueNext, original.LastRunAt, original);

        // A rename / enable-toggle (cadence unchanged) must NOT advance the overdue NextRunAt — else
        // the pending fire is silently dropped.
        var renamed = await overdue.Update(
            "Renamed", [endpoint], original.Interval, true, original.AnchorAt, DateTimeOffset.UtcNow,
            CancellationToken);

        renamed.Name.Should().Be("Renamed");
        renamed.NextRunAt.Should().Be(overdueNext);
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private async Task<ITestRunSchedule> CreateSchedule(IServiceProvider services, TimeSpan interval)
    {
        var factory = services.GetRequiredService<ITestRunSchedule.CreateNew>();
        var repo = services.GetRequiredService<IRepository<ITestRunSchedule>>();
        var suite = await GetOrCreate<ITestSuite>(services);
        var endpoint = await GetOrCreate<IModelEndpoint>(services);

        var schedule = factory("Nightly", suite, [endpoint], interval, true, DateTimeOffset.UtcNow);
        return await repo.AddAsync(schedule, CancellationToken);
    }

    private async Task<IReadOnlyList<IModelEndpoint>> CreateEndpoints(IServiceProvider services, int count)
    {
        var generator = services.GetRequiredService<IDomainEntityGenerator<IModelEndpoint>>();
        var endpoints = new List<IModelEndpoint>();
        for (var i = 0; i < count; i++)
            endpoints.Add(await generator.CreateAsync(CancellationToken));
        return endpoints;
    }
}
