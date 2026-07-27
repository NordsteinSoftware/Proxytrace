using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Proxytrace.Domain.TestRun;
using Proxytrace.Domain.TestRunGroup;
using Proxytrace.Domain.TestSuite;

namespace Proxytrace.Domain.Tests;

[TestClass]
public sealed class TestRunGroupValidationTests : DomainTest<Module>
{
    // ── factory / construction ────────────────────────────────────────────────

    [TestMethod]
    public async Task CreateNew_WithValidSuite_CreatesGroup()
    {
        IServiceProvider services = GetServices();
        var factory = services.GetRequiredService<ITestRunGroup.CreateNew>();
        var suite = await GetOrCreate<ITestSuite>(services);

        var group = factory(suite, false, null, sampleCount: 1);

        group.Should().NotBeNull();
        group.Suite.Should().Be(suite);
        group.Status.Should().Be(TestRunStatus.Pending);
        group.CompletedAt.Should().BeNull();
        group.IsSystemRun.Should().BeFalse();
        group.ScheduleId.Should().BeNull();
        group.Id.Should().NotBe(Guid.Empty);
        group.CreatedAt.Should().NotBe(default);
        group.UpdatedAt.Should().NotBe(default);
    }

    [TestMethod]
    public async Task CreateNew_WithNullSuite_ThrowsValidationException()
    {
        IServiceProvider services = GetServices();
        var factory = services.GetRequiredService<ITestRunGroup.CreateNew>();

        var action = () => factory.DynamicInvoke([null]);
        action.Should().Throw<Exception>();
    }

    [TestMethod]
    public async Task CreateNew_WithIsSystemRun_FlagsGroupAsSystemRun()
    {
        IServiceProvider services = GetServices();
        var factory = services.GetRequiredService<ITestRunGroup.CreateNew>();
        var suite = await GetOrCreate<ITestSuite>(services);

        var group = factory(suite, true, null, sampleCount: 1);

        group.IsSystemRun.Should().BeTrue();
    }

    [TestMethod]
    public async Task CreateNew_WithScheduleId_TagsGroupWithSchedule()
    {
        IServiceProvider services = GetServices();
        var factory = services.GetRequiredService<ITestRunGroup.CreateNew>();
        var suite = await GetOrCreate<ITestSuite>(services);
        var scheduleId = Guid.NewGuid();

        var group = factory(suite, false, scheduleId, sampleCount: 1);

        group.ScheduleId.Should().Be(scheduleId);
    }

    [TestMethod]
    public async Task CreateNew_WithSampleCount_SetsSampleCount()
    {
        IServiceProvider services = GetServices();
        var factory = services.GetRequiredService<ITestRunGroup.CreateNew>();
        var suite = await GetOrCreate<ITestSuite>(services);

        var group = factory(suite, false, null, sampleCount: ITestRunGroup.MaxSampleCount);

        group.SampleCount.Should().Be(ITestRunGroup.MaxSampleCount);
    }

    [TestMethod]
    public async Task AddAsync_WithSampleCountBelowOne_FailsValidation()
    {
        IServiceProvider services = GetServices();
        var factory = services.GetRequiredService<ITestRunGroup.CreateNew>();
        var repo = services.GetRequiredService<IRepository<ITestRunGroup>>();
        var suite = await GetOrCreate<ITestSuite>(services);

        await FluentActions
            .Invoking(() => repo.AddAsync(factory(suite, false, null, sampleCount: 0), CancellationToken))
            .Should().ThrowAsync<Exception>();
    }

    [TestMethod]
    public async Task AddAsync_WithSampleCountAboveMax_FailsValidation()
    {
        IServiceProvider services = GetServices();
        var factory = services.GetRequiredService<ITestRunGroup.CreateNew>();
        var repo = services.GetRequiredService<IRepository<ITestRunGroup>>();
        var suite = await GetOrCreate<ITestSuite>(services);

        await FluentActions
            .Invoking(() => repo.AddAsync(factory(suite, false, null, sampleCount: ITestRunGroup.MaxSampleCount + 1), CancellationToken))
            .Should().ThrowAsync<Exception>();
    }

    [TestMethod]
    public async Task CreateNew_IdsAreUniquePerInstance()
    {
        IServiceProvider services = GetServices();
        var factory = services.GetRequiredService<ITestRunGroup.CreateNew>();
        var suite = await GetOrCreate<ITestSuite>(services);

        var group1 = factory(suite, false, null, sampleCount: 1);
        var group2 = factory(suite, false, null, sampleCount: 1);

        group1.Id.Should().NotBe(group2.Id);
    }

    [TestMethod]
    public async Task CreateExisting_RestoresAllProperties()
    {
        IServiceProvider services = GetServices();
        var createExisting = services.GetRequiredService<ITestRunGroup.CreateExisting>();
        var generator = services.GetRequiredService<IDomainEntityGenerator<ITestRunGroup>>();
        var existing = await generator.CreateAsync(CancellationToken);

        var group = createExisting(existing.Suite, existing.Status, existing.CompletedAt, existing.IsSystemRun, existing.ScheduleId, existing.SampleCount, existing.OptimizationConsideredAt, existing);

        group.Id.Should().Be(existing.Id);
        group.Suite.Should().Be(existing.Suite);
        group.Status.Should().Be(existing.Status);
        group.CompletedAt.Should().Be(existing.CompletedAt);
        group.IsSystemRun.Should().Be(existing.IsSystemRun);
        group.ScheduleId.Should().Be(existing.ScheduleId);
        group.OptimizationConsideredAt.Should().Be(existing.OptimizationConsideredAt);
        group.CreatedAt.Should().Be(existing.CreatedAt);
        group.UpdatedAt.Should().Be(existing.UpdatedAt);
    }

    // ── status transitions ────────────────────────────────────────────────────

    [TestMethod]
    public async Task SetRunning_FromPending_TransitionsToRunning()
    {
        IServiceProvider services = GetServices();
        var factory = services.GetRequiredService<ITestRunGroup.CreateNew>();
        var suite = await GetOrCreate<ITestSuite>(services);
        var group = factory(suite, false, null, sampleCount: 1);
        await services.GetRequiredService<IRepository<ITestRunGroup>>().AddAsync(group, CancellationToken);

        var updated = await group.SetRunning(CancellationToken);

        updated.Status.Should().Be(TestRunStatus.Running);
        updated.CompletedAt.Should().BeNull();
    }

    [TestMethod]
    public async Task SetCompleted_FromRunning_TransitionsToCompletedWithTimestamp()
    {
        IServiceProvider services = GetServices();
        var group = await CreateGroupInState(services, TestRunStatus.Running);

        var updated = await group.SetCompleted(CancellationToken);

        updated.Status.Should().Be(TestRunStatus.Completed);
        updated.CompletedAt.Should().NotBeNull();
        updated.CompletedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [TestMethod]
    public async Task SetFailed_FromRunning_TransitionsToFailedWithTimestamp()
    {
        IServiceProvider services = GetServices();
        var group = await CreateGroupInState(services, TestRunStatus.Running);

        var updated = await group.SetFailed(CancellationToken);

        updated.Status.Should().Be(TestRunStatus.Failed);
        updated.CompletedAt.Should().NotBeNull();
    }

    [TestMethod]
    public async Task SetCancelled_FromRunning_TransitionsToCancelledWithTimestamp()
    {
        IServiceProvider services = GetServices();
        var group = await CreateGroupInState(services, TestRunStatus.Running);

        var updated = await group.SetCancelled(CancellationToken);

        updated.Status.Should().Be(TestRunStatus.Cancelled);
        updated.CompletedAt.Should().NotBeNull();
    }

    [TestMethod]
    public async Task SetCancelled_FromPending_TransitionsToCancelled()
    {
        IServiceProvider services = GetServices();
        var factory = services.GetRequiredService<ITestRunGroup.CreateNew>();
        var suite = await GetOrCreate<ITestSuite>(services);
        var group = factory(suite, false, null, sampleCount: 1);
        await services.GetRequiredService<IRepository<ITestRunGroup>>().AddAsync(group, CancellationToken);

        var updated = await group.SetCancelled(CancellationToken);

        updated.Status.Should().Be(TestRunStatus.Cancelled);
    }

    [TestMethod]
    public async Task SetRunning_FromTerminalState_Throws()
    {
        IServiceProvider services = GetServices();
        var group = await CreateGroupInState(services, TestRunStatus.Completed);

        await FluentActions
            .Invoking(() => group.SetRunning(CancellationToken))
            .Should().ThrowAsync<Exception>();
    }

    [TestMethod]
    public async Task SetCompleted_WhenAlreadyCompleted_Throws()
    {
        IServiceProvider services = GetServices();
        var group = await CreateGroupInState(services, TestRunStatus.Completed);

        await FluentActions
            .Invoking(() => group.SetCompleted(CancellationToken))
            .Should().ThrowAsync<Exception>();
    }

    [TestMethod]
    public async Task SetFailed_WhenAlreadyCancelled_Throws()
    {
        IServiceProvider services = GetServices();
        var group = await CreateGroupInState(services, TestRunStatus.Cancelled);

        await FluentActions
            .Invoking(() => group.SetFailed(CancellationToken))
            .Should().ThrowAsync<Exception>();
    }

    [TestMethod]
    public async Task SetCancelled_WhenAlreadyFailed_Throws()
    {
        IServiceProvider services = GetServices();
        var group = await CreateGroupInState(services, TestRunStatus.Failed);

        await FluentActions
            .Invoking(() => group.SetCancelled(CancellationToken))
            .Should().ThrowAsync<Exception>();
    }

    // ── persistence ───────────────────────────────────────────────────────────

    [TestMethod]
    public async Task SetRunning_PersistsStatusChange()
    {
        IServiceProvider services = GetServices();
        var factory = services.GetRequiredService<ITestRunGroup.CreateNew>();
        var repo = services.GetRequiredService<IRepository<ITestRunGroup>>();
        var suite = await GetOrCreate<ITestSuite>(services);

        var group = await repo.AddAsync(factory(suite, false, null, sampleCount: 1), CancellationToken);
        await group.SetRunning(CancellationToken);

        var reloaded = await repo.GetAsync(group.Id, CancellationToken);
        reloaded.Status.Should().Be(TestRunStatus.Running);
    }

    [TestMethod]
    public async Task SetCompleted_PersistsStatusAndCompletedAt()
    {
        IServiceProvider services = GetServices();
        var repo = services.GetRequiredService<IRepository<ITestRunGroup>>();

        var group = await CreateGroupInState(services, TestRunStatus.Running);
        await group.SetCompleted(CancellationToken);

        var reloaded = await repo.GetAsync(group.Id, CancellationToken);
        reloaded.Status.Should().Be(TestRunStatus.Completed);
        reloaded.CompletedAt.Should().NotBeNull();
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private async Task<ITestRunGroup> CreateGroupInState(IServiceProvider services, TestRunStatus targetStatus)
    {
        var factory = services.GetRequiredService<ITestRunGroup.CreateNew>();
        var repo = services.GetRequiredService<IRepository<ITestRunGroup>>();
        var suite = await GetOrCreate<ITestSuite>(services);

        var group = await repo.AddAsync(factory(suite, false, null, sampleCount: 1), CancellationToken);

        if (targetStatus == TestRunStatus.Pending)
            return group;

        group = await group.SetRunning(CancellationToken);

        return targetStatus switch
        {
            TestRunStatus.Running => group,
            TestRunStatus.Completed => await group.SetCompleted(CancellationToken),
            TestRunStatus.Failed => await group.SetFailed(CancellationToken),
            TestRunStatus.Cancelled => await group.SetCancelled(CancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(targetStatus))
        };
    }
}
