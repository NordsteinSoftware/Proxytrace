using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Proxytrace.Application.Statistics.TestRun.Internal;
using Proxytrace.Domain;
using Proxytrace.Domain.ModelEndpoint;
using Proxytrace.Domain.Statistics;
using Proxytrace.Domain.Statistics.TestRun;
using Proxytrace.Domain.TestRun;
using Proxytrace.Domain.TestRunGroup;
using Proxytrace.Domain.TestSuite;
using Proxytrace.Testing;

namespace Proxytrace.Application.Tests.Statistics;

/// <summary>
/// Internal A/B validation runs must not reach the user-facing statistics.
/// </summary>
/// <remarks>
/// <c>IsSystemRun</c> already hides them from the run list, so a user can neither see nor inspect
/// them — but their results were still folded into the pass-rate figures and the anomaly baseline.
/// People saw pass rates move because of runs that, as far as the UI is concerned, never happened.
/// </remarks>
[TestClass]
public sealed class TestRunStatsProjectorTests : BaseTest<Module>
{
    [TestMethod]
    public async Task Project_ForAUserRun_WritesStats()
    {
        IServiceProvider services = GetServices();
        var run = await CreateRunAsync(services, isSystemRun: false);

        await ProjectorFor(services).ProjectAsync(run.Id, CancellationToken);

        (await ReaderFor(services).FindAsync(run.Id, CancellationToken))
            .Should().NotBeNull("an ordinary run belongs in the statistics");
    }

    [TestMethod]
    public async Task Project_ForASystemRun_WritesNoStats()
    {
        IServiceProvider services = GetServices();
        var run = await CreateRunAsync(services, isSystemRun: true);

        await ProjectorFor(services).ProjectAsync(run.Id, CancellationToken);

        (await ReaderFor(services).FindAsync(run.Id, CancellationToken))
            .Should().BeNull("a system run must not appear in user-facing statistics");
    }

    [TestMethod]
    public async Task Project_ForASystemRunWithALeftoverRow_RemovesIt()
    {
        // Rows written before system runs were excluded must be cleaned up, not merely stopped —
        // skipping alone would leave them in the statistics forever.
        IServiceProvider services = GetServices();
        var run = await CreateRunAsync(services, isSystemRun: true);
        var writer = services.GetRequiredService<IStatsWriter<TestRunStats>>();
        await writer.UpsertAsync(
            new TestRunStats(
                TestRunId: run.Id,
                AgentId: run.Group.Suite.Agent.Id,
                EndpointId: run.Endpoint.Id,
                GroupId: run.Group.Id,
                SuiteId: run.Group.Suite.Id,
                TestCases: 1,
                Passed: 1,
                TotalDuration: TimeSpan.FromSeconds(1),
                Usage: null,
                Cost: null,
                RunCompletedAt: DateTimeOffset.UtcNow),
            CancellationToken);

        (await ReaderFor(services).FindAsync(run.Id, CancellationToken)).Should().NotBeNull();

        await ProjectorFor(services).ProjectAsync(run.Id, CancellationToken);

        (await ReaderFor(services).FindAsync(run.Id, CancellationToken))
            .Should().BeNull("projecting a system run must remove any stats row it already had");
    }

    private static TestRunStatsProjector ProjectorFor(IServiceProvider services)
        => new(
            services.GetRequiredService<IStatsWriter<TestRunStats>>(),
            services.GetRequiredService<IRepository<ITestRun>>());

    private static IStatsReader<TestRunStats, TestRunStats.Filter> ReaderFor(IServiceProvider services)
        => services.GetRequiredService<IStatsReader<TestRunStats, TestRunStats.Filter>>();

    private async Task<ITestRun> CreateRunAsync(IServiceProvider services, bool isSystemRun)
    {
        var suite = await services.GetRequiredService<IDomainEntityGenerator<ITestSuite>>()
            .GetOrCreateAsync(CancellationToken);
        var endpoint = await services.GetRequiredService<IDomainEntityGenerator<IModelEndpoint>>()
            .GetOrCreateAsync(CancellationToken);

        var group = services.GetRequiredService<ITestRunGroup.CreateNew>()(suite, isSystemRun, null, 1);
        group = await services.GetRequiredService<IRepository<ITestRunGroup>>().AddAsync(group, CancellationToken);

        var run = services.GetRequiredService<ITestRun.CreateNew>()(group, endpoint, sampleIndex: 0);
        return await services.GetRequiredService<IRepository<ITestRun>>().AddAsync(run, CancellationToken);
    }
}
