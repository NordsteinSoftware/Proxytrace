using Proxytrace.Domain.Statistics;
using AwesomeAssertions;
using NSubstitute;
using Proxytrace.Application.Statistics;
using Proxytrace.Application.Statistics.Internal;
using Proxytrace.Domain.Statistics.TestRun;
using Nordstein.Core.Common.Time;
using Proxytrace.Domain;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.AgentCall;
using Proxytrace.Messaging;
using Nordstein.Core.Testing;

namespace Proxytrace.Application.Tests.Statistics;

[TestClass]
public sealed class DashboardStatisticsTests : BaseTest<Module>
{
    private static DashboardStatistics Build(
        out ITestRunStatsReader runStats,
        out IAgentCallStatsReader callStats,
        out IAgentRepository agents,
        DashboardCacheOptions? cacheOptions = null,
        IClock? clock = null)
    {
        runStats = Substitute.For<ITestRunStatsReader>();
        callStats = Substitute.For<IAgentCallStatsReader>();
        agents = Substitute.For<IAgentRepository>();
        var agentCalls = Substitute.For<IAgentCallRepository>();
        var ingestionStream = Substitute.For<IIngestionStream>();
        // Default substitute reports IsActive == false (not inside a transaction), which is the
        // expected state for the dashboard fan-out guard.
        var transaction = Substitute.For<ITransaction>();
        if (clock is null)
        {
            clock = Substitute.For<IClock>();
            clock.UtcNow.Returns(_ => DateTimeOffset.UtcNow);
        }
        // Caching is opt-in per test (Ttl 0 disables it) so the behavioral tests below observe every
        // underlying call; the cache-specific tests pass an explicit TTL.
        return new DashboardStatistics(
            runStats, callStats, agents, agentCalls, ingestionStream, transaction,
            clock, cacheOptions ?? new DashboardCacheOptions { TtlSeconds = 0d });
    }

    /// <summary>
    /// Stubs every reader the dashboard fan-out touches with empty results so
    /// <c>GetDashboardViewAsync</c> completes; individual tests override what they assert on.
    /// </summary>
    private static void StubEmptyView(ITestRunStatsReader runStats, IAgentCallStatsReader callStats)
    {
        runStats.GetPassTotalsAsync(Arg.Any<TestRunStats.Filter>(), Arg.Any<CancellationToken>())
            .Returns(new TestRunPassTotals(0, 0));
        runStats.GetRecentCohortsAsync(Arg.Any<TestRunStats.Filter>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([]);
        callStats.GetSummaryAsync(Arg.Any<StatisticsFilter>(), Arg.Any<CancellationToken>())
            .Returns(new StatisticsSummary(0, 0, 0, 0, 0, null));
        callStats.GetLiveTelemetryAsync(Arg.Any<StatisticsFilter>(), Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new LiveTelemetry(0, 0, 0, 0, 0));
        callStats.GetCallTrendsAsync(Arg.Any<StatisticsFilter>(), Arg.Any<int>(), Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new CallTrends([], [], []));
        callStats.GetAgentBreakdownAsync(Arg.Any<StatisticsFilter>(), Arg.Any<CancellationToken>())
            .Returns([]);
        callStats.GetLatencyAsync(Arg.Any<StatisticsFilter>(), Arg.Any<CancellationToken>())
            .Returns([]);
        callStats.GetModelBreakdownAsync(Arg.Any<StatisticsFilter>(), Arg.Any<CancellationToken>())
            .Returns([]);
        callStats.GetEarliestCallAsync(Arg.Any<StatisticsFilter>(), Arg.Any<CancellationToken>())
            .Returns((DateTimeOffset?)null);
        callStats.GetTokenUsageAsync(Arg.Any<StatisticsFilter>(), Arg.Any<StatisticsBucket>(), Arg.Any<CancellationToken>())
            .Returns([]);
        callStats.GetTokenUsageByAgentAsync(Arg.Any<StatisticsFilter>(), Arg.Any<StatisticsBucket>(), Arg.Any<CancellationToken>())
            .Returns([]);
        callStats.GetPulseAsync(Arg.Any<StatisticsFilter>(), Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new int[60]);
    }

    [TestMethod]
    public async Task GetSummaryAsync_NoRuns_PassRateIsNull()
    {
        var svc = Build(out var runStats, out var callStats, out _);
        callStats.GetSummaryAsync(Arg.Any<StatisticsFilter>(), Arg.Any<CancellationToken>())
            .Returns(new StatisticsSummary(TotalCalls: 5, TotalInputTokens: 10, TotalOutputTokens: 20, TotalCachedInputTokens: 4, AvgLatencyMs: 100, OverallPassRate: 0.5));
        runStats.GetPassTotalsAsync(Arg.Any<TestRunStats.Filter>(), Arg.Any<CancellationToken>())
            .Returns(new TestRunPassTotals(0, 0));

        var result = await svc.GetSummaryAsync(new StatisticsFilter(), CancellationToken);

        result.TotalCalls.Should().Be(5);
        result.OverallPassRate.Should().BeNull();
    }

    [TestMethod]
    public async Task GetSummaryAsync_AggregatesPassRate_FromServerSideTotals()
    {
        var svc = Build(out var runStats, out var callStats, out _);
        callStats.GetSummaryAsync(Arg.Any<StatisticsFilter>(), Arg.Any<CancellationToken>())
            .Returns(new StatisticsSummary(0, 0, 0, 0, 0, 0));
        runStats.GetPassTotalsAsync(Arg.Any<TestRunStats.Filter>(), Arg.Any<CancellationToken>())
            .Returns(new TestRunPassTotals(TotalCases: 10, TotalPassed: 6));

        var result = await svc.GetSummaryAsync(new StatisticsFilter(), CancellationToken);

        // 6 passed / 10 cases = 0.6
        result.OverallPassRate.Should().Be(0.6);
    }

    [TestMethod]
    public async Task GetSummaryAsync_WithProjectFilter_ResolvesAgentsAndPassesAgentIds()
    {
        var svc = Build(out var runStats, out var callStats, out var agents);
        callStats.GetSummaryAsync(Arg.Any<StatisticsFilter>(), Arg.Any<CancellationToken>())
            .Returns(new StatisticsSummary(0, 0, 0, 0, 0, 0));
        runStats.GetPassTotalsAsync(Arg.Any<TestRunStats.Filter>(), Arg.Any<CancellationToken>())
            .Returns(new TestRunPassTotals(0, 0));

        var projectId = Guid.NewGuid();
        var matchingAgent = Substitute.For<IAgent>();
        matchingAgent.Id.Returns(Guid.NewGuid());
        matchingAgent.Project.Id.Returns(projectId);
        // The repository scopes to the project server-side, returning only that project's agents.
        agents.GetByProjectAsync(projectId, Arg.Any<CancellationToken>())
            .Returns([matchingAgent]);

        await svc.GetSummaryAsync(new StatisticsFilter(ProjectId: projectId), CancellationToken);

        await runStats.Received(1).GetPassTotalsAsync(
            Arg.Is<TestRunStats.Filter>(f =>
                f != null && f.AgentIds != null && f.AgentIds.Count == 1 && f.AgentIds.Single() == matchingAgent.Id),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task GetSummaryAsync_WithSeveralProjects_ResolvesAgentsOfEveryScopedProject()
    {
        // #483: a caller who may read several projects and named none. The agent set behind the
        // pass-rate totals must be the union of those projects' agents — not empty (the old
        // single-project-only branch) and not every agent in the install.
        var svc = Build(out var runStats, out var callStats, out var agents);
        callStats.GetSummaryAsync(Arg.Any<StatisticsFilter>(), Arg.Any<CancellationToken>())
            .Returns(new StatisticsSummary(0, 0, 0, 0, 0, 0));
        runStats.GetPassTotalsAsync(Arg.Any<TestRunStats.Filter>(), Arg.Any<CancellationToken>())
            .Returns(new TestRunPassTotals(0, 0));

        var firstProject = Guid.NewGuid();
        var secondProject = Guid.NewGuid();
        IAgent first = AgentIn(firstProject);
        IAgent second = AgentIn(secondProject);
        IAgent outsider = AgentIn(Guid.NewGuid());
        agents.GetAllAsync(Arg.Any<CancellationToken>()).Returns([first, second, outsider]);

        await svc.GetSummaryAsync(
            new StatisticsFilter(ProjectIds: [firstProject, secondProject]), CancellationToken);

        await runStats.Received(1).GetPassTotalsAsync(
            Arg.Is<TestRunStats.Filter>(f =>
                f != null && f.AgentIds != null && f.AgentIds.Count == 2
                && f.AgentIds.Contains(first.Id) && f.AgentIds.Contains(second.Id)),
            Arg.Any<CancellationToken>());
    }

    private static IAgent AgentIn(Guid projectId)
    {
        var agent = Substitute.For<IAgent>();
        agent.Id.Returns(Guid.NewGuid());
        agent.Project.Id.Returns(projectId);
        return agent;
    }

    [TestMethod]
    public async Task GetDashboardTrendsAsync_CapsSparklineToRecentCohorts()
    {
        var svc = Build(out var runStats, out var callStats, out _);
        callStats.GetCallTrendsAsync(Arg.Any<StatisticsFilter>(), Arg.Any<int>(), Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new CallTrends([], [], []));
        var now = DateTimeOffset.UtcNow;
        runStats.GetRecentCohortsAsync(Arg.Any<TestRunStats.Filter>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([
                new TestRunCohort(Guid.NewGuid(), Guid.NewGuid(), TestCases: 4, Passed: 2, now.AddHours(-2)),
                new TestRunCohort(Guid.NewGuid(), Guid.NewGuid(), TestCases: 0, Passed: 0, now.AddHours(-1)),
                new TestRunCohort(Guid.NewGuid(), Guid.NewGuid(), TestCases: 4, Passed: 4, now),
            ]);

        var trends = await svc.GetDashboardTrendsAsync(new StatisticsFilter(), CancellationToken);

        // Case-less cohorts are dropped; the rest map to percentages in chronological order, and the
        // reader is asked for a bounded number of cohorts (never the whole history).
        trends.PassRate.Should().Equal(50d, 100d);
        await runStats.Received(1).GetRecentCohortsAsync(
            Arg.Any<TestRunStats.Filter>(), Arg.Is<int>(limit => limit > 0), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task GetDashboardViewAsync_RunsIndependentQueriesConcurrently()
    {
        // The kiosk in-memory EF provider executes queries synchronously (no thread yield). This
        // test reproduces that by making every reader block the calling thread synchronously, then
        // asserts the dashboard fan-out still overlaps them. Before the Task.Run fan-out the queries
        // ran strictly sequentially (max concurrency == 1); this guards that regression.
        ThreadPool.GetMinThreads(out int minW, out int minIo);
        ThreadPool.SetMinThreads(Math.Max(minW, 24), minIo);

        int current = 0;
        int max = 0;
        object gate = new();
        T Track<T>(T value)
        {
            lock (gate)
            {
                current++;
                if (current > max) max = current;
            }
            Thread.Sleep(40);
            lock (gate) { current--; }
            return value;
        }

        var svc = Build(out var runStats, out var callStats, out var agents);

        runStats.GetPassTotalsAsync(Arg.Any<TestRunStats.Filter>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(Track(new TestRunPassTotals(0, 0))));
        runStats.GetRecentCohortsAsync(Arg.Any<TestRunStats.Filter>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(Track<IReadOnlyList<TestRunCohort>>([])));
        callStats.GetSummaryAsync(Arg.Any<StatisticsFilter>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(Track(new StatisticsSummary(0, 0, 0, 0, 0, null))));
        callStats.GetLiveTelemetryAsync(Arg.Any<StatisticsFilter>(), Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(Track(new LiveTelemetry(0, 0, 0, 0, 0))));
        callStats.GetCallTrendsAsync(Arg.Any<StatisticsFilter>(), Arg.Any<int>(), Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(Track(new CallTrends([], [], []))));
        callStats.GetAgentBreakdownAsync(Arg.Any<StatisticsFilter>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(Track<IReadOnlyList<AgentBreakdownStat>>([])));
        callStats.GetLatencyAsync(Arg.Any<StatisticsFilter>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(Track<IReadOnlyList<LatencyStat>>([])));
        callStats.GetModelBreakdownAsync(Arg.Any<StatisticsFilter>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(Track<IReadOnlyList<ModelBreakdownStat>>([])));
        callStats.GetEarliestCallAsync(Arg.Any<StatisticsFilter>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(Track<DateTimeOffset?>(null)));
        callStats.GetTokenUsageAsync(Arg.Any<StatisticsFilter>(), Arg.Any<StatisticsBucket>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(Track<IReadOnlyList<TokenUsageStat>>([])));
        callStats.GetTokenUsageByAgentAsync(Arg.Any<StatisticsFilter>(), Arg.Any<StatisticsBucket>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(Track<IReadOnlyList<AgentTokenUsageStat>>([])));
        agents.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(Track<IReadOnlyList<IAgent>>([])));

        var view = await svc.GetDashboardViewAsync(new StatisticsFilter(), recentTraceCount: 5, agentLimit: 5, CancellationToken);

        view.Should().NotBeNull();
        max.Should().BeGreaterThan(1, "independent dashboard queries must run concurrently, not sequentially");
    }

    [TestMethod]
    public async Task GetDashboardViewAsync_RequestsPulse_TrailingHourInMinuteBuckets()
    {
        var svc = Build(out var runStats, out var callStats, out var agents);
        StubEmptyView(runStats, callStats);
        agents.GetAllAsync(Arg.Any<CancellationToken>()).Returns([]);
        int[] pulse = new int[60];
        pulse[59] = 7;
        DateTimeOffset capturedFrom = default;
        DateTimeOffset capturedTo = default;
        callStats.GetPulseAsync(
                Arg.Any<StatisticsFilter>(),
                Arg.Do<DateTimeOffset>(from => capturedFrom = from),
                Arg.Do<DateTimeOffset>(to => capturedTo = to),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(pulse);

        DashboardView view = await svc.GetDashboardViewAsync(new StatisticsFilter(), recentTraceCount: 6, agentLimit: 8, CancellationToken);

        view.Pulse.Should().HaveCount(60);
        view.Pulse[59].Should().Be(7);
        // Trailing hour with one bucket per minute, regardless of the range filter.
        await callStats.Received(1).GetPulseAsync(
            Arg.Any<StatisticsFilter>(),
            Arg.Any<DateTimeOffset>(),
            Arg.Any<DateTimeOffset>(),
            60,
            Arg.Any<CancellationToken>());
        (capturedTo - capturedFrom).Should().Be(TimeSpan.FromMinutes(60));
        capturedTo.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));
    }

    [TestMethod]
    public async Task GetTokenUsage_DelegatesToCallStats()
    {
        var svc = Build(out _, out var callStats, out _);
        var bucketStart = DateTimeOffset.UtcNow;
        callStats.GetTokenUsageAsync(Arg.Any<StatisticsFilter>(), Arg.Any<StatisticsBucket>(), Arg.Any<CancellationToken>())
            .Returns([new TokenUsageStat(bucketStart, Guid.NewGuid(), 1, 2, 0)]);

        var result = await svc.GetTokenUsageAsync(new StatisticsFilter(), StatisticsBucket.Daily, CancellationToken);

        result.Should().ContainSingle();
        await callStats.Received(1).GetTokenUsageAsync(Arg.Any<StatisticsFilter>(), Arg.Any<StatisticsBucket>(), Arg.Any<CancellationToken>());
    }

    // ── Dashboard composite cache ────────────────────────────────────────────────

    [TestMethod]
    public async Task GetDashboardViewAsync_WithinTtl_ServesCachedViewWithoutRequerying()
    {
        var svc = Build(out var runStats, out var callStats, out _,
            new DashboardCacheOptions { TtlSeconds = 10d });
        StubEmptyView(runStats, callStats);

        var first = await svc.GetDashboardViewAsync(new StatisticsFilter(), recentTraceCount: 5, agentLimit: 5, CancellationToken);
        var second = await svc.GetDashboardViewAsync(new StatisticsFilter(), recentTraceCount: 5, agentLimit: 5, CancellationToken);

        second.Should().BeSameAs(first);
        await callStats.Received(1).GetSummaryAsync(Arg.Any<StatisticsFilter>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task GetDashboardViewAsync_DifferentFilters_DoNotShareCacheEntries()
    {
        var svc = Build(out var runStats, out var callStats, out var agents,
            new DashboardCacheOptions { TtlSeconds = 10d });
        StubEmptyView(runStats, callStats);
        agents.GetByProjectAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns([]);

        // Tenant isolation hinges on this: the ProjectId is part of the cache key, so a
        // project-scoped view and the admin-only global (ProjectId == null) view never share an
        // entry — the controller's access check decides who may request which key.
        var global = await svc.GetDashboardViewAsync(new StatisticsFilter(), recentTraceCount: 5, agentLimit: 5, CancellationToken);
        var scoped = await svc.GetDashboardViewAsync(new StatisticsFilter(ProjectId: Guid.NewGuid()), recentTraceCount: 5, agentLimit: 5, CancellationToken);

        scoped.Should().NotBeSameAs(global);
        await callStats.Received(2).GetSummaryAsync(Arg.Any<StatisticsFilter>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task GetDashboardViewAsync_ConcurrentCallersSameKey_TriggerOneComputation()
    {
        var svc = Build(out var runStats, out var callStats, out _,
            new DashboardCacheOptions { TtlSeconds = 10d });
        StubEmptyView(runStats, callStats);

        // Hold the underlying computation open until every caller has piled onto the same key.
        var release = new TaskCompletionSource<StatisticsSummary>(TaskCreationOptions.RunContinuationsAsynchronously);
        callStats.GetSummaryAsync(Arg.Any<StatisticsFilter>(), Arg.Any<CancellationToken>())
            .Returns(_ => release.Task);

        Task<DashboardView>[] callers = Enumerable.Range(0, 5)
            .Select(_ => svc.GetDashboardViewAsync(new StatisticsFilter(), recentTraceCount: 5, agentLimit: 5, CancellationToken))
            .ToArray();
        release.SetResult(new StatisticsSummary(0, 0, 0, 0, 0, null));
        DashboardView[] views = await Task.WhenAll(callers);

        views.Should().OnlyContain(v => ReferenceEquals(v, views[0]));
        await callStats.Received(1).GetSummaryAsync(Arg.Any<StatisticsFilter>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task GetDashboardViewAsync_FaultedComputation_IsNotCached()
    {
        var svc = Build(out var runStats, out var callStats, out _,
            new DashboardCacheOptions { TtlSeconds = 10d });
        StubEmptyView(runStats, callStats);

        int attempts = 0;
        callStats.GetSummaryAsync(Arg.Any<StatisticsFilter>(), Arg.Any<CancellationToken>())
            .Returns(_ => Interlocked.Increment(ref attempts) == 1
                ? Task.FromException<StatisticsSummary>(new InvalidOperationException("db down"))
                : Task.FromResult(new StatisticsSummary(0, 0, 0, 0, 0, null)));

        await FluentActions
            .Invoking(() => svc.GetDashboardViewAsync(new StatisticsFilter(), recentTraceCount: 5, agentLimit: 5, CancellationToken))
            .Should().ThrowAsync<InvalidOperationException>();
        var view = await svc.GetDashboardViewAsync(new StatisticsFilter(), recentTraceCount: 5, agentLimit: 5, CancellationToken);

        // The failure was evicted, so the second request recomputed instead of replaying the error.
        view.Should().NotBeNull();
        attempts.Should().Be(2);
    }

    [TestMethod]
    public async Task GetDashboardViewAsync_ExpiredTtl_Recomputes()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(_ => now);
        var svc = Build(out var runStats, out var callStats, out _,
            new DashboardCacheOptions { TtlSeconds = 10d }, clock);
        StubEmptyView(runStats, callStats);

        var first = await svc.GetDashboardViewAsync(new StatisticsFilter(), recentTraceCount: 5, agentLimit: 5, CancellationToken);
        now = now.AddSeconds(11);
        var second = await svc.GetDashboardViewAsync(new StatisticsFilter(), recentTraceCount: 5, agentLimit: 5, CancellationToken);

        second.Should().NotBeSameAs(first);
        await callStats.Received(2).GetSummaryAsync(Arg.Any<StatisticsFilter>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task GetDashboardViewAsync_CacheDisabled_RecomputesEveryCall()
    {
        var svc = Build(out var runStats, out var callStats, out _,
            new DashboardCacheOptions { TtlSeconds = 0d });
        StubEmptyView(runStats, callStats);

        await svc.GetDashboardViewAsync(new StatisticsFilter(), recentTraceCount: 5, agentLimit: 5, CancellationToken);
        await svc.GetDashboardViewAsync(new StatisticsFilter(), recentTraceCount: 5, agentLimit: 5, CancellationToken);

        await callStats.Received(2).GetSummaryAsync(Arg.Any<StatisticsFilter>(), Arg.Any<CancellationToken>());
    }
}
