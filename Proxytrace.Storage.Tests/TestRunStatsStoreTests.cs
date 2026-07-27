using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Proxytrace.Domain.Statistics;
using Proxytrace.Domain.Statistics.TestRun;
using Proxytrace.Domain;
using Proxytrace.Domain.TestRun;
using Proxytrace.Domain.Usage;
using Proxytrace.Testing;

namespace Proxytrace.Storage.Tests;

[TestClass]
public sealed class TestRunStatsStoreTests : BaseTest<Module>
{
    private static TestRunStats StatsFor(ITestRun run, int cases = 4, int passed = 3, DateTimeOffset? completed = null) =>
        new(TestRunId: run.Id,
            AgentId: run.Group.Suite.Agent.Id,
            EndpointId: run.Endpoint.Id,
            GroupId: run.Group.Id,
            SuiteId: run.Group.Suite.Id,
            TestCases: cases,
            Passed: passed,
            TotalDuration: TimeSpan.FromSeconds(2),
            Usage: new TokenUsage(100, 200),
            Cost: 0.05m,
            RunCompletedAt: completed ?? DateTimeOffset.UtcNow);

    [TestMethod]
    public async Task UpsertAsync_NewRow_PersistsAndIsReadable()
    {
        var services = GetServices();
        var writer = services.GetRequiredService<IStatsWriter<TestRunStats>>();
        var reader = services.GetRequiredService<IStatsReader<TestRunStats, TestRunStats.Filter>>();
        var run = await services.GetRequiredService<IDomainEntityGenerator<ITestRun>>().CreateAsync(CancellationToken);

        var stats = StatsFor(run);
        await writer.UpsertAsync(stats, CancellationToken);

        var found = await reader.FindAsync(run.Id, CancellationToken);
        found.Should().NotBeNull();
        found.TestRunId.Should().Be(run.Id);
        found.TestCases.Should().Be(4);
        found.Passed.Should().Be(3);
        var usage = found.Usage;
        usage.Should().NotBeNull();
        usage.InputTokenCount.Should().Be(100);
        found.Cost.Should().Be(0.05m);
        found.TotalDuration.Should().Be(TimeSpan.FromSeconds(2));
    }

    [TestMethod]
    public async Task QueryAsync_EmptyAgentIds_MatchesNothing_NotEverything()
    {
        var services = GetServices();
        var writer = services.GetRequiredService<IStatsWriter<TestRunStats>>();
        var reader = services.GetRequiredService<IStatsReader<TestRunStats, TestRunStats.Filter>>();
        var run = await services.GetRequiredService<IDomainEntityGenerator<ITestRun>>().CreateAsync(CancellationToken);
        await writer.UpsertAsync(StatsFor(run), CancellationToken);

        // An empty AgentIds list is what the dashboard produces for a project that has no agents.
        // It must narrow to nothing; treating it as "no filter" leaked every other project's runs.
        var scoped = await reader.QueryAsync(new TestRunStats.Filter(AgentIds: []), CancellationToken);
        var unfiltered = await reader.QueryAsync(new TestRunStats.Filter(AgentIds: null), CancellationToken);

        scoped.Should().BeEmpty();
        unfiltered.Should().ContainSingle(s => s.TestRunId == run.Id);
    }

    [TestMethod]
    public async Task QueryAsync_EmptySuiteIds_MatchesNothing_NotEverything()
    {
        var services = GetServices();
        var writer = services.GetRequiredService<IStatsWriter<TestRunStats>>();
        var reader = services.GetRequiredService<IStatsReader<TestRunStats, TestRunStats.Filter>>();
        var run = await services.GetRequiredService<IDomainEntityGenerator<ITestRun>>().CreateAsync(CancellationToken);
        await writer.UpsertAsync(StatsFor(run), CancellationToken);

        var scoped = await reader.QueryAsync(new TestRunStats.Filter(SuiteIds: []), CancellationToken);

        scoped.Should().BeEmpty();
    }

    [TestMethod]
    public async Task UpsertAsync_ExistingRow_UpdatesValuesInPlace()
    {
        var services = GetServices();
        var writer = services.GetRequiredService<IStatsWriter<TestRunStats>>();
        var reader = services.GetRequiredService<IStatsReader<TestRunStats, TestRunStats.Filter>>();
        var run = await services.GetRequiredService<IDomainEntityGenerator<ITestRun>>().CreateAsync(CancellationToken);

        await writer.UpsertAsync(StatsFor(run, cases: 4, passed: 1), CancellationToken);
        await writer.UpsertAsync(StatsFor(run, cases: 6, passed: 5), CancellationToken);

        var found = await reader.FindAsync(run.Id, CancellationToken);
        found.Should().NotBeNull();
        found.TestCases.Should().Be(6);
        found.Passed.Should().Be(5);

        var all = await reader.QueryAsync(new TestRunStats.Filter(), CancellationToken);
        all.Should().ContainSingle(s => s.TestRunId == run.Id);
    }

    [TestMethod]
    public async Task FindAsync_Unknown_ReturnsNull()
    {
        var services = GetServices();
        var reader = services.GetRequiredService<IStatsReader<TestRunStats, TestRunStats.Filter>>();

        var found = await reader.FindAsync(Guid.NewGuid(), CancellationToken);

        found.Should().BeNull();
    }

    [TestMethod]
    public async Task RemoveAsync_Existing_DeletesRow()
    {
        var services = GetServices();
        var writer = services.GetRequiredService<IStatsWriter<TestRunStats>>();
        var reader = services.GetRequiredService<IStatsReader<TestRunStats, TestRunStats.Filter>>();
        var run = await services.GetRequiredService<IDomainEntityGenerator<ITestRun>>().CreateAsync(CancellationToken);

        await writer.UpsertAsync(StatsFor(run), CancellationToken);
        await writer.RemoveAsync(run.Id, CancellationToken);

        var found = await reader.FindAsync(run.Id, CancellationToken);
        found.Should().BeNull();
    }

    [TestMethod]
    public async Task RemoveAsync_Unknown_DoesNotThrow()
    {
        var services = GetServices();
        var writer = services.GetRequiredService<IStatsWriter<TestRunStats>>();

        await writer.Invoking(w => w.RemoveAsync(Guid.NewGuid(), CancellationToken))
            .Should().NotThrowAsync();
    }

    [TestMethod]
    public async Task QueryAsync_FilterByAgent_ReturnsOnlyMatching()
    {
        var services = GetServices();
        var writer = services.GetRequiredService<IStatsWriter<TestRunStats>>();
        var reader = services.GetRequiredService<IStatsReader<TestRunStats, TestRunStats.Filter>>();
        var gen = services.GetRequiredService<IDomainEntityGenerator<ITestRun>>();
        var runA = await gen.CreateAsync(CancellationToken);
        var runB = await gen.CreateAsync(CancellationToken);

        await writer.UpsertAsync(StatsFor(runA), CancellationToken);
        await writer.UpsertAsync(StatsFor(runB), CancellationToken);

        var agentA = runA.Group.Suite.Agent.Id;
        var agentB = Guid.NewGuid();
        if (agentB == agentA) agentB = Guid.NewGuid();
        await writer.UpsertAsync(StatsFor(runB) with { AgentId = agentB }, CancellationToken);

        var byAgentA = await reader.QueryAsync(
            new TestRunStats.Filter(AgentId: agentA), CancellationToken);

        byAgentA.Should().ContainSingle();
        byAgentA[0].TestRunId.Should().Be(runA.Id);
    }

    [TestMethod]
    public async Task QueryAsync_FilterByAgentIds_ReturnsAllInSet()
    {
        var services = GetServices();
        var writer = services.GetRequiredService<IStatsWriter<TestRunStats>>();
        var reader = services.GetRequiredService<IStatsReader<TestRunStats, TestRunStats.Filter>>();
        var gen = services.GetRequiredService<IDomainEntityGenerator<ITestRun>>();
        var runA = await gen.CreateAsync(CancellationToken);
        var runB = await gen.CreateAsync(CancellationToken);

        await writer.UpsertAsync(StatsFor(runA), CancellationToken);
        await writer.UpsertAsync(StatsFor(runB), CancellationToken);

        var filtered = await reader.QueryAsync(
            new TestRunStats.Filter(AgentIds: [runA.Group.Suite.Agent.Id, runB.Group.Suite.Agent.Id]),
            CancellationToken);

        filtered.Should().HaveCount(2);
    }

    [TestMethod]
    public async Task QueryAsync_FilterByDateRange_ExcludesOutsideWindow()
    {
        var services = GetServices();
        var writer = services.GetRequiredService<IStatsWriter<TestRunStats>>();
        var reader = services.GetRequiredService<IStatsReader<TestRunStats, TestRunStats.Filter>>();
        var gen = services.GetRequiredService<IDomainEntityGenerator<ITestRun>>();
        var oldRun = await gen.CreateAsync(CancellationToken);
        var recentRun = await gen.CreateAsync(CancellationToken);

        var oldDate = DateTimeOffset.UtcNow.AddDays(-10);
        var recentDate = DateTimeOffset.UtcNow;
        await writer.UpsertAsync(StatsFor(oldRun, completed: oldDate), CancellationToken);
        await writer.UpsertAsync(StatsFor(recentRun, completed: recentDate), CancellationToken);

        var inWindow = await reader.QueryAsync(
            new TestRunStats.Filter(From: DateTimeOffset.UtcNow.AddDays(-1)),
            CancellationToken);

        inWindow.Should().ContainSingle(s => s.TestRunId == recentRun.Id);
    }

    [TestMethod]
    public async Task GetPassTotalsAsync_EmptyTable_ReturnsZeros()
    {
        var services = GetServices();
        var reader = services.GetRequiredService<ITestRunStatsReader>();

        var totals = await reader.GetPassTotalsAsync(new TestRunStats.Filter(), CancellationToken);

        totals.TotalCases.Should().Be(0);
        totals.TotalPassed.Should().Be(0);
    }

    [TestMethod]
    public async Task GetPassTotalsAsync_SumsAcrossMatchingRuns_AndHonorsFilter()
    {
        var services = GetServices();
        var writer = services.GetRequiredService<IStatsWriter<TestRunStats>>();
        var reader = services.GetRequiredService<ITestRunStatsReader>();
        var gen = services.GetRequiredService<IDomainEntityGenerator<ITestRun>>();
        var runA = await gen.CreateAsync(CancellationToken);
        var runB = await gen.CreateAsync(CancellationToken);

        // The run generator reuses the same agent, so give run B a synthetic AgentId (a plain
        // indexed column, no FK) to make the agent-scoped read distinguishable.
        await writer.UpsertAsync(StatsFor(runA, cases: 4, passed: 3), CancellationToken);
        await writer.UpsertAsync(StatsFor(runB, cases: 6, passed: 3) with { AgentId = Guid.NewGuid() }, CancellationToken);

        var all = await reader.GetPassTotalsAsync(new TestRunStats.Filter(), CancellationToken);
        var scoped = await reader.GetPassTotalsAsync(
            new TestRunStats.Filter(AgentId: runA.Group.Suite.Agent.Id), CancellationToken);

        all.TotalCases.Should().Be(10);
        all.TotalPassed.Should().Be(6);
        scoped.TotalCases.Should().Be(4);
        scoped.TotalPassed.Should().Be(3);
    }

    [TestMethod]
    public async Task GetRecentCohortsAsync_CollapsesSamples_MatchingAggregateSamples()
    {
        var services = GetServices();
        var writer = services.GetRequiredService<IStatsWriter<TestRunStats>>();
        var reader = services.GetRequiredService<ITestRunStatsReader>();
        var gen = services.GetRequiredService<IDomainEntityGenerator<ITestRun>>();

        // Two cohorts: cohort X has three samples (passed 1, 2, 2), cohort Y is single-sample. The
        // GroupId/EndpointId columns are plain indexed columns (no FK), so the cohort spread is
        // synthetic; only TestRunId needs a real run per row.
        var now = DateTimeOffset.UtcNow;
        Guid groupX = Guid.NewGuid(), endpointX = Guid.NewGuid();
        Guid groupY = Guid.NewGuid(), endpointY = Guid.NewGuid();
        var rows = new List<TestRunStats>();
        foreach (var (group, endpoint, passed, completedAt) in new[]
        {
            (groupX, endpointX, 1, now.AddMinutes(-30)),
            (groupX, endpointX, 2, now.AddMinutes(-20)),
            (groupX, endpointX, 2, now.AddMinutes(-10)),
            (groupY, endpointY, 4, now.AddMinutes(-5)),
        })
        {
            var run = await gen.CreateAsync(CancellationToken);
            var stats = StatsFor(run, cases: 4, passed: passed, completed: completedAt)
                with { GroupId = group, EndpointId = endpoint };
            await writer.UpsertAsync(stats, CancellationToken);
            rows.Add(stats);
        }

        var cohorts = await reader.GetRecentCohortsAsync(new TestRunStats.Filter(), limit: 50, CancellationToken);

        // Regression guard: the SQL cohort aggregation must keep AggregateSamples' semantics —
        // rounded-mean Passed, shared TestCases, latest RunCompletedAt — on the same rows.
        var expected = rows.AggregateSamples()
            .OrderBy(r => r.RunCompletedAt)
            .Select(r => (r.GroupId, r.EndpointId, r.TestCases, r.Passed, r.RunCompletedAt))
            .ToArray();
        cohorts
            .Select(c => (c.GroupId, c.EndpointId, c.TestCases, c.Passed, c.LastRunCompletedAt))
            .Should().Equal(expected);
    }

    [TestMethod]
    public async Task GetRecentCohortsAsync_CapsToMostRecentCohorts_InChronologicalOrder()
    {
        var services = GetServices();
        var writer = services.GetRequiredService<IStatsWriter<TestRunStats>>();
        var reader = services.GetRequiredService<ITestRunStatsReader>();
        var gen = services.GetRequiredService<IDomainEntityGenerator<ITestRun>>();

        var now = DateTimeOffset.UtcNow;
        var cohortTimes = new[] { now.AddHours(-3), now.AddHours(-2), now.AddHours(-1) };
        foreach (var completedAt in cohortTimes)
        {
            var run = await gen.CreateAsync(CancellationToken);
            await writer.UpsertAsync(
                StatsFor(run, completed: completedAt) with { GroupId = Guid.NewGuid() },
                CancellationToken);
        }

        var cohorts = await reader.GetRecentCohortsAsync(new TestRunStats.Filter(), limit: 2, CancellationToken);

        // The oldest cohort falls off; the two survivors come back oldest-first for the sparkline.
        cohorts.Should().HaveCount(2);
        cohorts.Select(c => c.LastRunCompletedAt).Should().Equal(cohortTimes[1], cohortTimes[2]);
    }

    [TestMethod]
    public async Task QueryAsync_FilterBySuiteAndEndpoint_ScopesResults()
    {
        var services = GetServices();
        var writer = services.GetRequiredService<IStatsWriter<TestRunStats>>();
        var reader = services.GetRequiredService<IStatsReader<TestRunStats, TestRunStats.Filter>>();
        var run = await services.GetRequiredService<IDomainEntityGenerator<ITestRun>>().CreateAsync(CancellationToken);
        await writer.UpsertAsync(StatsFor(run), CancellationToken);

        var bySuite = await reader.QueryAsync(new TestRunStats.Filter(SuiteId: run.Group.Suite.Id), CancellationToken);
        var byEndpoint = await reader.QueryAsync(new TestRunStats.Filter(EndpointId: run.Endpoint.Id), CancellationToken);
        var byGroup = await reader.QueryAsync(new TestRunStats.Filter(GroupId: run.Group.Id), CancellationToken);
        var byUnknown = await reader.QueryAsync(new TestRunStats.Filter(SuiteId: Guid.NewGuid()), CancellationToken);

        bySuite.Should().ContainSingle();
        byEndpoint.Should().ContainSingle();
        byGroup.Should().ContainSingle();
        byUnknown.Should().BeEmpty();
    }
}
