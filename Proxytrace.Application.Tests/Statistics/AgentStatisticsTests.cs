using Proxytrace.Domain.Statistics;
using AwesomeAssertions;
using NSubstitute;
using Proxytrace.Application.Statistics.Internal;
using Proxytrace.Domain.Statistics.TestRun;
using Proxytrace.Domain;
using Nordstein.Core.Domain.Exceptions;
using Proxytrace.Domain.OptimizationProposal;
using Proxytrace.Domain.TestSuite;
using Nordstein.Core.Testing;

namespace Proxytrace.Application.Tests.Statistics;

[TestClass]
public sealed class AgentStatisticsTests : BaseTest<Module>
{
    private static AgentStatistics Build(
        out IStatsReader<TestRunStats, TestRunStats.Filter> runStats,
        out IAgentCallStatsReader callStats,
        out ITestSuiteRepository testSuites,
        out IOptimizationProposalRepository proposals)
    {
        runStats = Substitute.For<IStatsReader<TestRunStats, TestRunStats.Filter>>();
        callStats = Substitute.For<IAgentCallStatsReader>();
        testSuites = Substitute.For<ITestSuiteRepository>();
        proposals = Substitute.For<IOptimizationProposalRepository>();
        return new AgentStatistics(runStats, callStats, testSuites, proposals);
    }

    private static TestRunStats Stat(Guid suiteId, int cases, int passed, DateTimeOffset completed) =>
        new(TestRunId: Guid.NewGuid(),
            AgentId: Guid.NewGuid(),
            EndpointId: Guid.NewGuid(),
            GroupId: Guid.NewGuid(),
            SuiteId: suiteId,
            TestCases: cases,
            Passed: passed,
            TotalDuration: null,
            Usage: null,
            Cost: null,
            RunCompletedAt: completed);

    [TestMethod]
    public async Task GetAgentOverviewAsync_ComposesAllSubQueries()
    {
        var svc = Build(out var runStats, out var callStats, out var testSuites, out var proposals);
        var agentId = Guid.NewGuid();
        var from = DateTimeOffset.UtcNow.AddDays(-1);
        var to = DateTimeOffset.UtcNow;

        var summary = new AgentTimeSummary(5, 10, 20, 4, 1.5m, 30);
        IReadOnlyList<AgentTimeSeriesPoint> series = [new AgentTimeSeriesPoint(to, 1, 5, 10, 2, 0.5m, 30)];
        callStats.GetAgentWindowAsync(agentId, from, to, StatisticsBucket.Daily, Arg.Any<CancellationToken>())
            .Returns((series, summary));

        runStats.QueryAsync(Arg.Any<TestRunStats.Filter>(), Arg.Any<CancellationToken>())
            .Returns([Stat(Guid.NewGuid(), 3, 2, to)]);

        testSuites.GetByAgentAsync(agentId, Arg.Any<CancellationToken>()).Returns([]);
        testSuites.GetManyAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns([]);
        proposals.GetByAgentAsync(agentId, Arg.Any<CancellationToken>()).Returns([]);

        var result = await svc.GetAgentOverviewAsync(agentId, from, to, StatisticsBucket.Daily, CancellationToken);

        result.Summary.Should().Be(summary);
        result.TimeSeries.Should().BeEquivalentTo(series);
        result.PassRateTrend.Should().HaveCount(1);
        result.Counts.SuiteCount.Should().Be(0);
    }

    [TestMethod]
    public async Task GetAgentPassRateTrendAsync_BucketsByBucketStart()
    {
        var svc = Build(out var runStats, out _, out _, out _);
        var bucketA = new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);
        var bucketB = new DateTimeOffset(2026, 5, 2, 0, 0, 0, TimeSpan.Zero);
        runStats.QueryAsync(Arg.Any<TestRunStats.Filter>(), Arg.Any<CancellationToken>())
            .Returns([
                Stat(Guid.NewGuid(), 5, 4, bucketA.AddHours(3)),
                Stat(Guid.NewGuid(), 3, 1, bucketA.AddHours(6)),
                Stat(Guid.NewGuid(), 2, 2, bucketB.AddHours(2)),
            ]);

        var result = await svc.GetAgentPassRateTrendAsync(Guid.NewGuid(), bucketA, bucketB.AddDays(1), StatisticsBucket.Daily, CancellationToken);

        result.Should().HaveCount(2);
        result[0].BucketStart.Should().Be(bucketA);
        result[0].Passed.Should().Be(5);
        result[0].TestCases.Should().Be(8);
        result[1].BucketStart.Should().Be(bucketB);
        result[1].Passed.Should().Be(2);
        result[1].TestCases.Should().Be(2);
    }

    [TestMethod]
    public async Task GetAgentLatestSuitePassRatesAsync_NoRuns_ReturnsEmpty()
    {
        var svc = Build(out var runStats, out _, out _, out _);
        runStats.QueryAsync(Arg.Any<TestRunStats.Filter>(), Arg.Any<CancellationToken>())
            .Returns([]);

        var result = await svc.GetAgentLatestSuitePassRatesAsync(Guid.NewGuid(), CancellationToken);

        result.Should().BeEmpty();
    }

    [TestMethod]
    public async Task GetAgentLatestSuitePassRatesAsync_PicksLatestPerSuite()
    {
        var svc = Build(out var runStats, out _, out var testSuites, out _);
        var suiteId = Guid.NewGuid();
        var older = DateTimeOffset.UtcNow.AddDays(-2);
        var newer = DateTimeOffset.UtcNow;
        runStats.QueryAsync(Arg.Any<TestRunStats.Filter>(), Arg.Any<CancellationToken>())
            .Returns([Stat(suiteId, 4, 1, older), Stat(suiteId, 4, 4, newer)]);

        var suite = Substitute.For<ITestSuite>();
        suite.Id.Returns(suiteId);
        suite.Name.Returns("My Suite");
        testSuites.GetManyAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns([suite]);

        var result = await svc.GetAgentLatestSuitePassRatesAsync(Guid.NewGuid(), CancellationToken);

        result.Should().ContainSingle();
        result[0].SuiteId.Should().Be(suiteId);
        result[0].SuiteName.Should().Be("My Suite");
        result[0].LatestRunAt.Should().Be(newer);
        result[0].Passed.Should().Be(4);
        result[0].TestCases.Should().Be(4);
    }

    [TestMethod]
    public async Task GetAgentLatestSuitePassRatesAsync_DeletedSuite_FallsBackToFindAsync()
    {
        var svc = Build(out var runStats, out _, out var testSuites, out _);
        var liveSuiteId = Guid.NewGuid();
        var deletedSuiteId = Guid.NewGuid();
        var when = DateTimeOffset.UtcNow;
        runStats.QueryAsync(Arg.Any<TestRunStats.Filter>(), Arg.Any<CancellationToken>())
            .Returns([Stat(liveSuiteId, 3, 3, when), Stat(deletedSuiteId, 2, 1, when)]);

        var liveSuite = Substitute.For<ITestSuite>();
        liveSuite.Id.Returns(liveSuiteId);
        liveSuite.Name.Returns("Live");

        testSuites.GetManyAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<IReadOnlyList<ITestSuite>>(new EntitiesNotFoundException([deletedSuiteId], typeof(ITestSuite))));
        testSuites.FindAsync(liveSuiteId, Arg.Any<CancellationToken>()).Returns(liveSuite);
        testSuites.FindAsync(deletedSuiteId, Arg.Any<CancellationToken>()).Returns((ITestSuite?)null);

        var result = await svc.GetAgentLatestSuitePassRatesAsync(Guid.NewGuid(), CancellationToken);

        result.Should().ContainSingle();
        result[0].SuiteId.Should().Be(liveSuiteId);
        result[0].SuiteName.Should().Be("Live");
    }

    [TestMethod]
    public async Task GetAgentEntityCountsAsync_CountsAgentScopedSuitesProposalsAndOpenStatus()
    {
        var svc = Build(out _, out _, out var testSuites, out var proposals);
        var agentId = Guid.NewGuid();

        // The repository filters by agent server-side, so it only returns this agent's rows.
        var suiteA = Substitute.For<ITestSuite>();
        suiteA.Agent.Id.Returns(agentId);
        suiteA.TestCases.Returns([Substitute.For<Domain.TestCase.ITestCase>(), Substitute.For<Domain.TestCase.ITestCase>()]);
        var suiteB = Substitute.For<ITestSuite>();
        suiteB.Agent.Id.Returns(agentId);
        suiteB.TestCases.Returns([Substitute.For<Domain.TestCase.ITestCase>()]);
        testSuites.GetByAgentAsync(agentId, Arg.Any<CancellationToken>())
            .Returns([suiteA, suiteB]);

        var draftProposal = Substitute.For<IOptimizationProposal>();
        draftProposal.Agent.Id.Returns(agentId);
        draftProposal.Status.Returns(ProposalStatus.Draft);
        var approvedProposal = Substitute.For<IOptimizationProposal>();
        approvedProposal.Agent.Id.Returns(agentId);
        approvedProposal.Status.Returns(ProposalStatus.Accepted);
        proposals.GetByAgentAsync(agentId, Arg.Any<CancellationToken>())
            .Returns([draftProposal, approvedProposal]);

        var result = await svc.GetAgentEntityCountsAsync(agentId, CancellationToken);

        result.SuiteCount.Should().Be(2);
        result.TestCaseCount.Should().Be(3);
        result.OpenProposalCount.Should().Be(1);
        result.TotalProposalCount.Should().Be(2);
    }
}
