using System.Globalization;
using AwesomeAssertions;
using NSubstitute;
using Proxytrace.Application.Optimization.Internal;
using Proxytrace.Domain.Statistics.TestRun;
using Proxytrace.Application.TestRun;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.Model;
using Proxytrace.Domain.ModelEndpoint;
using Proxytrace.Domain.OptimizationTheory;
using Proxytrace.Domain.Proposal;
using Proxytrace.Domain.TestRun;
using Proxytrace.Domain.TestRunGroup;
using Proxytrace.Domain.TestSuite;
using Nordstein.Core.Testing;

namespace Proxytrace.Application.Tests;

[TestClass]
public sealed class SwitchModelOptimizerTests : BaseTest<Module>
{
    [TestMethod]
    public async Task DiscoverTheories_NoRunForCurrentEndpoint_ReturnsEmpty()
    {
        // no spec marked current => agent points at an endpoint without a run
        Fixture fixture = Build(
            Spec("a", cost: 5m, latency: Sec(5)),
            Spec("b", cost: 9m, latency: Sec(5)));

        var theories = await fixture.Optimizer.DiscoverTheories(
            fixture.Group, fixture.Cohorts, CancellationToken);

        theories.Should().BeEmpty();
        fixture.Captured.Called.Should().BeFalse();
    }

    [TestMethod]
    public async Task DiscoverTheories_FewerThanTwoRunsWithPassRate_ReturnsEmpty()
    {
        // alternative has no test cases => null pass rate => filtered out, leaving only current
        Fixture fixture = Build(
            Spec("current", cost: 10m, latency: Sec(5), isCurrent: true),
            Spec("alt", cost: 1m, latency: Sec(1), passed: 0, total: 0));

        var theories = await fixture.Optimizer.DiscoverTheories(
            fixture.Group, fixture.Cohorts, CancellationToken);

        theories.Should().BeEmpty();
        fixture.Captured.Called.Should().BeFalse();
    }

    [TestMethod]
    public async Task DiscoverTheories_CurrentAlreadyBest_ReturnsEmpty()
    {
        Fixture fixture = Build(
            Spec("current", cost: 1m, latency: Sec(1), isCurrent: true),
            Spec("alt", cost: 10m, latency: Sec(10)));

        var theories = await fixture.Optimizer.DiscoverTheories(
            fixture.Group, fixture.Cohorts, CancellationToken);

        theories.Should().BeEmpty();
        fixture.Captured.Called.Should().BeFalse();
    }

    [TestMethod]
    public async Task DiscoverTheories_WinningMarginBelowThreshold_ReturnsEmpty()
    {
        // cheapest beats the current model by <10%; latency identical everywhere
        Fixture fixture = Build(
            Spec("current", cost: 10m, latency: Sec(5), isCurrent: true),
            Spec("altA", cost: 9.5m, latency: Sec(5)),
            Spec("altB", cost: 9.8m, latency: Sec(5)));

        var theories = await fixture.Optimizer.DiscoverTheories(
            fixture.Group, fixture.Cohorts, CancellationToken);

        theories.Should().BeEmpty();
        fixture.Captured.Called.Should().BeFalse();
    }

    [TestMethod]
    public async Task DiscoverTheories_OtherStatWorseThanCurrent_ReturnsEmpty()
    {
        // altA wins cost with margin, but its latency regresses the current model's
        Fixture fixture = Build(
            Spec("current", cost: 10m, latency: Sec(5), isCurrent: true),
            Spec("altA", cost: 5m, latency: Sec(20)),
            Spec("altB", cost: 9m, latency: Sec(6)));

        var theories = await fixture.Optimizer.DiscoverTheories(
            fixture.Group, fixture.Cohorts, CancellationToken);

        theories.Should().BeEmpty();
        fixture.Captured.Called.Should().BeFalse();
    }

    [TestMethod]
    public async Task DiscoverTheories_PassRateWorseThanRunnerUp_ReturnsEmpty()
    {
        Fixture fixture = Build(
            Spec("current", cost: 10m, latency: Sec(5), isCurrent: true, passed: 8),
            Spec("altA", cost: 5m, latency: Sec(4), passed: 5),
            Spec("altB", cost: 9m, latency: Sec(6), passed: 9));

        var theories = await fixture.Optimizer.DiscoverTheories(
            fixture.Group, fixture.Cohorts, CancellationToken);

        theories.Should().BeEmpty();
        fixture.Captured.Called.Should().BeFalse();
    }

    [TestMethod]
    public async Task DiscoverTheories_CostWin_ProducesTheory()
    {
        Fixture fixture = Build(
            Spec("current", cost: 10m, latency: Sec(5), isCurrent: true),
            Spec("altA", cost: 5m, latency: Sec(5)),
            Spec("altB", cost: 9m, latency: Sec(5)));

        var theories = await fixture.Optimizer.DiscoverTheories(
            fixture.Group, fixture.Cohorts, CancellationToken);

        theories.Should().HaveCount(1);
        Captured c = fixture.Captured;
        c.Endpoint.Should().BeSameAs(fixture.RunsByName["altA"].Endpoint);
        c.Priority.Should().Be(Priority.High); // 50% cost saving vs current
        c.Source.Should().Be(TheorySource.Optimizer);
        c.Suite.Should().BeSameAs(fixture.Group.Suite);
        c.EvidenceIds.Should().BeEquivalentTo(
            [fixture.RunsByName["current"].Id, fixture.RunsByName["altA"].Id]);
        c.Rationale.Should().Contain("cost");
    }

    [TestMethod]
    public async Task DiscoverTheories_LatencyWin_ProducesTheory()
    {
        Fixture fixture = Build(
            Spec("current", cost: 10m, latency: Sec(10), isCurrent: true),
            Spec("altA", cost: 10m, latency: Sec(5)),
            Spec("altB", cost: 10m, latency: Sec(9)));

        var theories = await fixture.Optimizer.DiscoverTheories(
            fixture.Group, fixture.Cohorts, CancellationToken);

        theories.Should().HaveCount(1);
        Captured c = fixture.Captured;
        c.Endpoint.Should().BeSameAs(fixture.RunsByName["altA"].Endpoint);
        c.Priority.Should().Be(Priority.High);
        c.Rationale.Should().Contain("latency");
    }

    [TestMethod]
    public async Task DiscoverTheories_BothMetricsQualify_LargerRelativeSavingWins()
    {
        // cost saving vs current ~16.7%, latency saving vs current 90% => latency wins
        Fixture fixture = Build(
            Spec("current", cost: 12m, latency: Sec(100), isCurrent: true),
            Spec("C", cost: 10m, latency: Sec(50)),
            Spec("L", cost: 11m, latency: Sec(10)),
            Spec("F1", cost: 20m, latency: Sec(60)),
            Spec("F2", cost: 60m, latency: Sec(20)));

        var theories = await fixture.Optimizer.DiscoverTheories(
            fixture.Group, fixture.Cohorts, CancellationToken);

        theories.Should().HaveCount(1);
        Captured c = fixture.Captured;
        c.Endpoint.Should().BeSameAs(fixture.RunsByName["L"].Endpoint);
        c.Rationale.Should().Contain("latency");
        c.Priority.Should().Be(Priority.High);
    }

    [TestMethod]
    public async Task DiscoverTheories_CostWinUnderCommaDecimalCulture_FormatsRationaleInvariantly()
    {
        Fixture fixture = Build(
            Spec("current", cost: 12.5m, latency: Sec(5), isCurrent: true),
            Spec("altA", cost: 10m, latency: Sec(5)));

        await DiscoverUnderCulture(fixture);

        // Persisted proposal text must read the same on every host: "20.0%", not "20,0%".
        fixture.Captured.Rationale.Should().Contain("20.0%").And.Contain("12.5");
        fixture.Captured.Rationale.Should().NotContain(",");
    }

    [TestMethod]
    public async Task DiscoverTheories_LatencyWinUnderCommaDecimalCulture_FormatsRationaleInvariantly()
    {
        Fixture fixture = Build(
            Spec("current", cost: 10m, latency: Sec(12.5), isCurrent: true),
            Spec("altA", cost: 10m, latency: Sec(5)));

        await DiscoverUnderCulture(fixture);

        fixture.Captured.Rationale.Should().Contain("60.0%").And.Contain("5.00s").And.Contain("12.50s");
        fixture.Captured.Rationale.Should().NotContain(",");
    }

    /// <summary>
    /// Runs the optimizer with a comma-decimal culture in scope, restoring the ambient culture on
    /// every path. The culture is set on the flowing <see cref="CultureInfo.CurrentCulture"/> only —
    /// never on the process-wide <c>DefaultThreadCurrentCulture</c> — and the optimizer completes
    /// synchronously, so nothing escapes this method.
    /// </summary>
    private async Task DiscoverUnderCulture(Fixture fixture)
    {
        var german = new CultureInfo("de-DE");

        // Guard against a globalization-invariant host silently making these tests vacuous.
        12.5m.ToString("0.####", german).Should().Be("12,5");

        CultureInfo previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = german;
        try
        {
            var theories = await fixture.Optimizer.DiscoverTheories(
                fixture.Group, fixture.Cohorts, CancellationToken);

            theories.Should().HaveCount(1);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    private static TimeSpan Sec(double seconds) => TimeSpan.FromSeconds(seconds);

    private static RunSpec Spec(
        string name,
        decimal? cost,
        TimeSpan? latency,
        bool isCurrent = false,
        int passed = 8,
        int total = 10)
        => new(name, cost, latency, isCurrent, passed, total);

    private static Fixture Build(params RunSpec[] specs)
    {
        var groupId = Guid.NewGuid();
        var captured = new Captured();
        var runsByName = new Dictionary<string, ITestRun>();
        var stats = new List<TestRunStats>();
        IModelEndpoint? currentEndpoint = null;

        foreach (RunSpec spec in specs)
        {
            var endpointId = Guid.NewGuid();
            var model = Substitute.For<IModel>();
            model.Name.Returns(spec.Name);
            var endpoint = Substitute.For<IModelEndpoint>();
            endpoint.Id.Returns(endpointId);
            endpoint.Model.Returns(model);

            var runId = Guid.NewGuid();
            var run = Substitute.For<ITestRun>();
            run.Id.Returns(runId);
            run.Endpoint.Returns(endpoint);
            runsByName[spec.Name] = run;

            stats.Add(new TestRunStats(
                TestRunId: runId,
                AgentId: Guid.Empty,
                EndpointId: endpointId,
                GroupId: groupId,
                SuiteId: Guid.Empty,
                TestCases: spec.Total,
                Passed: spec.Passed,
                TotalDuration: spec.Latency,
                Usage: null,
                Cost: spec.Cost,
                RunCompletedAt: DateTimeOffset.UtcNow));

            if (spec.IsCurrent)
            {
                currentEndpoint = endpoint;
            }
        }

        if (currentEndpoint is null)
        {
            currentEndpoint = Substitute.For<IModelEndpoint>();
            currentEndpoint.Id.Returns(Guid.NewGuid());
        }

        var agent = Substitute.For<IAgent>();
        agent.Endpoint.Returns(currentEndpoint);

        var suite = Substitute.For<ITestSuite>();
        suite.Agent.Returns(agent);

        var group = Substitute.For<ITestRunGroup>();
        group.Id.Returns(groupId);
        group.Suite.Returns(suite);

        // The optimizer now consumes cohorts (built once by the CompositeOptimizer); here each spec
        // is a distinct endpoint, so every cohort holds a single sample whose aggregated stats equal
        // the spec's stats.
        var cohorts = RunCohort.Build(
            runsByName.Values.ToList(),
            stats.ToDictionary(s => s.TestRunId));

        IModelSwitchTheory.CreateNew factory = (
            _, theorySuite, source, priority, rationale, proposedEndpoint, evidenceIds) =>
        {
            captured.Called = true;
            captured.Priority = priority;
            captured.Rationale = rationale;
            captured.Endpoint = proposedEndpoint;
            captured.Source = source;
            captured.Suite = theorySuite;
            captured.EvidenceIds = evidenceIds;
            return Substitute.For<IModelSwitchTheory>();
        };

        var optimizer = new SwitchModelOptimizer(factory);

        return new Fixture
        {
            Optimizer = optimizer,
            Group = group,
            Cohorts = cohorts,
            Captured = captured,
            RunsByName = runsByName,
        };
    }

    private sealed record RunSpec(
        string Name,
        decimal? Cost,
        TimeSpan? Latency,
        bool IsCurrent,
        int Passed,
        int Total);

    private sealed class Fixture
    {
        public required SwitchModelOptimizer Optimizer { get; init; }
        public required ITestRunGroup Group { get; init; }
        public required IReadOnlyList<RunCohort> Cohorts { get; init; }
        public required Captured Captured { get; init; }
        public required IReadOnlyDictionary<string, ITestRun> RunsByName { get; init; }
    }

    private sealed class Captured
    {
        public bool Called { get; set; }
        public Priority Priority { get; set; }
        public string? Rationale { get; set; }
        public IModelEndpoint? Endpoint { get; set; }
        public TheorySource Source { get; set; }
        public ITestSuite? Suite { get; set; }
        public IReadOnlyCollection<Guid>? EvidenceIds { get; set; }
    }
}
