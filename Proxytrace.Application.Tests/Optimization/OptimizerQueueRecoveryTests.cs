using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Proxytrace.Application.Optimization;
using Proxytrace.Application.Optimization.Internal;
using Proxytrace.Domain;
using Proxytrace.Domain.Kiosk;
using Proxytrace.Domain.ModelEndpoint;
using Proxytrace.Domain.OptimizationTheory;
using Proxytrace.Domain.TestRun;
using Proxytrace.Domain.TestRunGroup;
using Proxytrace.Domain.TestSuite;
using Nordstein.Core.Testing;

namespace Proxytrace.Application.Tests.Optimization;

/// <summary>
/// The optimizer's work queue is an in-process channel, so anything still in it when the process
/// stopped was lost: a deploy during a scheduled-run window silently dropped that night's
/// optimization, with nothing recorded anywhere to show it had been due.
/// </summary>
/// <remarks>
/// The theory queue has always had this recovery — it could, because a theory's status is its own
/// durable marker. A test run group had no equivalent until
/// <see cref="ITestRunGroup.OptimizationConsideredAt"/>, which is what these tests cover.
/// </remarks>
[TestClass]
public sealed class OptimizerQueueRecoveryTests : BaseTest<Module>
{
    [TestMethod]
    public async Task PendingOptimization_ListsACompletedGroupThatWasNeverConsidered()
    {
        IServiceProvider services = GetServices();
        var groups = services.GetRequiredService<ITestRunGroupRepository>();
        var group = await CreateCompletedGroupAsync(services, isSystemRun: false);

        var pending = await groups.GetPendingOptimizationAsync(50, CancellationToken);

        pending.Should().ContainSingle(g => g.Id == group.Id);
    }

    [TestMethod]
    public async Task PendingOptimization_ExcludesAGroupAlreadyConsidered()
    {
        // "Considered and found nothing" must not be confused with "never considered", or a barren
        // group would be reprocessed on every single boot for the life of the installation.
        IServiceProvider services = GetServices();
        var groups = services.GetRequiredService<ITestRunGroupRepository>();
        var group = await CreateCompletedGroupAsync(services, isSystemRun: false);

        await group.MarkOptimizationConsidered(CancellationToken);

        (await groups.GetPendingOptimizationAsync(50, CancellationToken))
            .Should().NotContain(g => g.Id == group.Id);
    }

    [TestMethod]
    public async Task PendingOptimization_ExcludesSystemRuns()
    {
        // System runs are the optimizer's own A/B runs. Recovering them would make the optimizer
        // feed itself.
        IServiceProvider services = GetServices();
        var groups = services.GetRequiredService<ITestRunGroupRepository>();
        var group = await CreateCompletedGroupAsync(services, isSystemRun: true);

        (await groups.GetPendingOptimizationAsync(50, CancellationToken))
            .Should().NotContain(g => g.Id == group.Id);
    }

    [TestMethod]
    public async Task PendingOptimization_ExcludesAGroupStillRunning()
    {
        // A group that has not finished has not produced the evidence the optimizer reads, and will
        // be enqueued normally when it does.
        IServiceProvider services = GetServices();
        var groups = services.GetRequiredService<ITestRunGroupRepository>();
        var suite = await services.GetRequiredService<IDomainEntityGenerator<ITestSuite>>().GetOrCreateAsync(CancellationToken);
        var pendingGroup = services.GetRequiredService<ITestRunGroup.CreateNew>()(suite, false, null, 1);
        pendingGroup = await groups.AddAsync(pendingGroup, CancellationToken);

        (await groups.GetPendingOptimizationAsync(50, CancellationToken))
            .Should().NotContain(g => g.Id == pendingGroup.Id);
    }

    [TestMethod]
    public async Task Recovery_ReQueuesTheBacklogAndMarksEachGroupConsidered()
    {
        IServiceProvider services = GetServices();
        var groups = services.GetRequiredService<ITestRunGroupRepository>();
        var group = await CreateCompletedGroupAsync(services, isSystemRun: false);

        var optimizer = Substitute.For<IOptimizer>();
        optimizer.DiscoverTheories(Arg.Any<ITestRunGroup>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<IOptimizationTheory>>([]));

        var service = new OptimizerService(
            optimizer,
            groups,
            Substitute.For<ITheoryValidationService>(),
            new KioskOptions { Enabled = false },
            NullLogger<OptimizerService>.Instance);

        await service.StartAsync(CancellationToken);
        try
        {
            // The backlog is drained by the same loop that serves live enqueues, so wait for the
            // marker rather than for a fixed delay.
            await WaitForConsideredAsync(groups, group.Id);
        }
        finally
        {
            await service.StopAsync(CancellationToken);
        }

        var reloaded = await groups.GetAsync(group.Id, CancellationToken);
        reloaded.OptimizationConsideredAt.Should().NotBeNull(
            "a recovered group must be marked, even when discovery produced no theories");
        await optimizer.Received(1).DiscoverTheories(
            Arg.Is<ITestRunGroup>(g => g != null && g.Id == group.Id), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Recovery_IsSkippedInKioskMode()
    {
        // Kiosk storage is in-memory and freshly demo-seeded on every start, so the only backlog
        // recovery could find is the seeded groups — re-queuing those would fire real A/B runs, and
        // real spend, on every boot when a live endpoint is configured.
        IServiceProvider services = GetServices();
        var groups = services.GetRequiredService<ITestRunGroupRepository>();
        var group = await CreateCompletedGroupAsync(services, isSystemRun: false);

        var optimizer = Substitute.For<IOptimizer>();
        var service = new OptimizerService(
            optimizer,
            groups,
            Substitute.For<ITheoryValidationService>(),
            new KioskOptions { Enabled = true },
            NullLogger<OptimizerService>.Instance);

        await service.RecoverPendingGroupsAsync(CancellationToken);

        await optimizer.DidNotReceiveWithAnyArgs().DiscoverTheories(default!, default);
        (await groups.GetAsync(group.Id, CancellationToken)).OptimizationConsideredAt.Should().BeNull();
    }

    private async Task WaitForConsideredAsync(ITestRunGroupRepository groups, Guid groupId)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var current = await groups.GetAsync(groupId, CancellationToken);
            if (current.OptimizationConsideredAt is not null) return;
            await Task.Delay(20, CancellationToken);
        }
    }

    private async Task<ITestRunGroup> CreateCompletedGroupAsync(IServiceProvider services, bool isSystemRun)
    {
        var suite = await services.GetRequiredService<IDomainEntityGenerator<ITestSuite>>().GetOrCreateAsync(CancellationToken);
        var endpoint = await services.GetRequiredService<IDomainEntityGenerator<IModelEndpoint>>().GetOrCreateAsync(CancellationToken);
        var groups = services.GetRequiredService<ITestRunGroupRepository>();

        var group = await groups.AddAsync(
            services.GetRequiredService<ITestRunGroup.CreateNew>()(suite, isSystemRun, null, 1),
            CancellationToken);

        // A group only reaches a terminal status through its runs, so give it one and settle both.
        var run = await services.GetRequiredService<IRepository<ITestRun>>().AddAsync(
            services.GetRequiredService<ITestRun.CreateNew>()(group, endpoint, sampleIndex: 0),
            CancellationToken);
        await run.SetRunning(CancellationToken);

        group = await group.ReloadAsync(CancellationToken);
        group = await group.SetRunning(CancellationToken);
        return await group.SetCompleted(CancellationToken);
    }
}
