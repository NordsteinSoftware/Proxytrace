using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Proxytrace.Application.TestRun.Internal;
using Proxytrace.Domain;
using Proxytrace.Domain.ModelEndpoint;
using Proxytrace.Domain.TestRun;
using Proxytrace.Domain.TestRunGroup;
using Proxytrace.Testing;

namespace Proxytrace.Application.Tests;

[TestClass]
public sealed class OrphanedTestRunReaperTests : BaseTest<Module>
{
    private static async Task<(ITestRunGroup group, ITestRun run)> SeedRunningGroupAsync(IServiceProvider services, CancellationToken ct)
    {
        var groupGenerator = services.GetRequiredService<IDomainEntityGenerator<ITestRunGroup>>();
        var endpointGenerator = services.GetRequiredService<IDomainEntityGenerator<IModelEndpoint>>();
        var createRun = services.GetRequiredService<ITestRun.CreateNew>();
        var runRepo = services.GetRequiredService<ITestRunRepository>();

        var group = await groupGenerator.CreateAsync(ct);
        group = await group.SetRunning(ct);
        var endpoint = await endpointGenerator.GetOrCreateAsync(ct);
        var run = await runRepo.AddAsync(createRun(group, endpoint, 0), ct); // stays Pending
        return (group, run);
    }

    [TestMethod]
    public async Task ReapAsync_MarksOrphanedRunningGroupAndItsRunsCancelled()
    {
        var services = GetServices();
        var groupRepo = services.GetRequiredService<ITestRunGroupRepository>();
        var runRepo = services.GetRequiredService<ITestRunRepository>();
        var (group, _) = await SeedRunningGroupAsync(services, CancellationToken);

        await OrphanedTestRunReaperHostedService.ReapAsync(groupRepo, runRepo, NullLogger.Instance, CancellationToken);

        var reloadedGroup = await groupRepo.GetAsync(group.Id, CancellationToken);
        reloadedGroup.Status.Should().Be(TestRunStatus.Cancelled);

        var reloadedRuns = await runRepo.GetByGroupAsync(group.Id, CancellationToken);
        reloadedRuns.Should().NotBeEmpty().And.OnlyContain(r => r.Status == TestRunStatus.Cancelled);
    }

    [TestMethod]
    public async Task ReapAsync_LeavesAlreadyTerminalGroupsUntouched()
    {
        // A group that already settled (here, cancelled) is not selected by the non-terminal status
        // filter, so a second sweep neither re-cancels it nor throws.
        var services = GetServices();
        var groupRepo = services.GetRequiredService<ITestRunGroupRepository>();
        var runRepo = services.GetRequiredService<ITestRunRepository>();
        var (group, _) = await SeedRunningGroupAsync(services, CancellationToken);
        await group.SetCancelled(CancellationToken);

        await OrphanedTestRunReaperHostedService.ReapAsync(groupRepo, runRepo, NullLogger.Instance, CancellationToken);

        var reloaded = await groupRepo.GetAsync(group.Id, CancellationToken);
        reloaded.Status.Should().Be(TestRunStatus.Cancelled);
    }

    [TestMethod]
    public async Task ReapAsync_CancelsARunLeftNonTerminalUnderAFinishedGroup()
    {
        // The group-scoped sweep selects by *group* status, so a run stranded under a group that
        // already completed is invisible to it — which is exactly the row #452 produced, and it
        // survived every restart. Rows written before that fix still need healing.
        var services = GetServices();
        var groupRepo = services.GetRequiredService<ITestRunGroupRepository>();
        var runRepo = services.GetRequiredService<ITestRunRepository>();
        var (group, run) = await SeedRunningGroupAsync(services, CancellationToken);
        await group.SetCompleted(CancellationToken);

        await OrphanedTestRunReaperHostedService.ReapAsync(groupRepo, runRepo, NullLogger.Instance, CancellationToken);

        var reloadedRun = await runRepo.GetAsync(run.Id, CancellationToken);
        reloadedRun.Status.Should().Be(TestRunStatus.Cancelled);

        var reloadedGroup = await groupRepo.GetAsync(group.Id, CancellationToken);
        reloadedGroup.Status.Should().Be(TestRunStatus.Completed, "the finished group is not touched");
    }
}
