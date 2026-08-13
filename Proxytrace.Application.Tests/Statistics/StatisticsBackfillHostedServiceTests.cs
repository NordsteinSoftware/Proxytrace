using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Proxytrace.Domain.Statistics;
using Proxytrace.Application.Statistics.Internal;
using Proxytrace.Application.Statistics.Internal.Worker;
using Proxytrace.Domain.Statistics.TestRun;
using Proxytrace.Domain;
using Proxytrace.Domain.TestRun;
using Nordstein.Core.Testing;

namespace Proxytrace.Application.Tests.Statistics;

[TestClass]
public sealed class StatisticsBackfillHostedServiceTests : BaseTest<Module>
{
    private static ITestRun MakeRun(TestRunStatus status)
    {
        var run = Substitute.For<ITestRun>();
        run.Id.Returns(Guid.NewGuid());
        run.Status.Returns(status);
        return run;
    }

    private static IStatsProjector MakeProjector(Type entityType)
    {
        var projector = Substitute.For<IStatsProjector>();
        projector.EntityType.Returns(entityType);
        return projector;
    }

    private static async Task RunAndWaitAsync(StatisticsBackfillHostedService svc)
    {
        await svc.StartAsync(CancellationToken.None);
        // Backfill is fire-and-forget on Task.Run; StopAsync waits for completion.
        await svc.StopAsync(CancellationToken.None);
    }

    [TestMethod]
    public async Task RunAsync_NoTestRunProjectors_ReturnsEarly()
    {
        var runs = Substitute.For<ITestRunRepository>();
        var reader = Substitute.For<IStatsReader<TestRunStats, TestRunStats.Filter>>();
        var nonTestRun = MakeProjector(typeof(string));
        var svc = new StatisticsBackfillHostedService(runs, reader, [nonTestRun], NullLogger<StatisticsBackfillHostedService>.Instance);

        await RunAndWaitAsync(svc);

        await runs.DidNotReceive().GetByStatusAsync(
            Arg.Any<IReadOnlyCollection<TestRunStatus>>(), Arg.Any<CancellationToken>());
        await nonTestRun.DidNotReceive().ProjectAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task RunAsync_OnlyFinalizedRunsAreProjected()
    {
        var runs = Substitute.For<ITestRunRepository>();
        var reader = Substitute.For<IStatsReader<TestRunStats, TestRunStats.Filter>>();
        var completed = MakeRun(TestRunStatus.Completed);
        var pending = MakeRun(TestRunStatus.Pending);
        var running = MakeRun(TestRunStatus.Running);
        var failed = MakeRun(TestRunStatus.Failed);
        var cancelled = MakeRun(TestRunStatus.Cancelled);
        // Status filtering now happens in SQL via GetByStatusAsync, so the repo only returns
        // terminal runs — pending/running never reach the service.
        runs.GetByStatusAsync(Arg.Any<IReadOnlyCollection<TestRunStatus>>(), Arg.Any<CancellationToken>())
            .Returns([completed, failed, cancelled]);
        reader.FindAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((TestRunStats?)null);

        var projector = MakeProjector(typeof(ITestRun));
        var svc = new StatisticsBackfillHostedService(runs, reader, [projector], NullLogger<StatisticsBackfillHostedService>.Instance);

        await RunAndWaitAsync(svc);

        await projector.Received(1).ProjectAsync(completed.Id, Arg.Any<CancellationToken>());
        await projector.Received(1).ProjectAsync(failed.Id, Arg.Any<CancellationToken>());
        await projector.Received(1).ProjectAsync(cancelled.Id, Arg.Any<CancellationToken>());
        await projector.DidNotReceive().ProjectAsync(pending.Id, Arg.Any<CancellationToken>());
        await projector.DidNotReceive().ProjectAsync(running.Id, Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task RunAsync_SkipsRunsAlreadyProjected()
    {
        var runs = Substitute.For<ITestRunRepository>();
        var reader = Substitute.For<IStatsReader<TestRunStats, TestRunStats.Filter>>();
        var alreadyProjected = MakeRun(TestRunStatus.Completed);
        var missing = MakeRun(TestRunStatus.Completed);
        Guid alreadyProjectedId = alreadyProjected.Id;
        Guid missingId = missing.Id;
        runs.GetByStatusAsync(Arg.Any<IReadOnlyCollection<TestRunStatus>>(), Arg.Any<CancellationToken>())
            .Returns([alreadyProjected, missing]);

        var existingStats = new TestRunStats(alreadyProjectedId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, 1, null, null, null, DateTimeOffset.UtcNow);
        reader.FindAsync(alreadyProjectedId, Arg.Any<CancellationToken>()).Returns(existingStats);
        reader.FindAsync(missingId, Arg.Any<CancellationToken>()).Returns((TestRunStats?)null);

        var projector = MakeProjector(typeof(ITestRun));
        var svc = new StatisticsBackfillHostedService(runs, reader, [projector], NullLogger<StatisticsBackfillHostedService>.Instance);

        await RunAndWaitAsync(svc);

        await projector.DidNotReceive().ProjectAsync(alreadyProjectedId, Arg.Any<CancellationToken>());
        await projector.Received(1).ProjectAsync(missingId, Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task RunAsync_ProjectorThrows_ContinuesWithRemainingRuns()
    {
        var runs = Substitute.For<ITestRunRepository>();
        var reader = Substitute.For<IStatsReader<TestRunStats, TestRunStats.Filter>>();
        var first = MakeRun(TestRunStatus.Completed);
        var second = MakeRun(TestRunStatus.Completed);
        runs.GetByStatusAsync(Arg.Any<IReadOnlyCollection<TestRunStatus>>(), Arg.Any<CancellationToken>())
            .Returns([first, second]);
        reader.FindAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((TestRunStats?)null);

        var projector = MakeProjector(typeof(ITestRun));
        projector.ProjectAsync(first.Id, Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("projector failure")));

        var svc = new StatisticsBackfillHostedService(runs, reader, [projector], NullLogger<StatisticsBackfillHostedService>.Instance);

        await RunAndWaitAsync(svc);

        await projector.Received(1).ProjectAsync(second.Id, Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task StopAsync_BeforeStart_DoesNotThrow()
    {
        var svc = new StatisticsBackfillHostedService(
            Substitute.For<ITestRunRepository>(),
            Substitute.For<IStatsReader<TestRunStats, TestRunStats.Filter>>(),
            [],
            NullLogger<StatisticsBackfillHostedService>.Instance);

        await svc.Invoking(s => s.StopAsync(CancellationToken)).Should().NotThrowAsync();
    }
}
