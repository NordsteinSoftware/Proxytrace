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

namespace Proxytrace.Application.Tests.Optimization;

/// <summary>
/// Additional concrete scenario tests for <see cref="SwitchModelOptimizer"/>.
/// These represent realistic multi-model comparison scenarios.
/// </summary>
[TestClass]
public sealed class SwitchModelOptimizerScenarioTests : BaseTest<Module>
{
    /// <summary>
    /// Scenario: GPT-4o is the current model at $10 cost. GPT-4o-mini achieves same pass rate
    /// at $3 cost (70% saving), with lower latency too. Should recommend switching.
    /// </summary>
    [TestMethod]
    public async Task Gpt4oToGpt4oMini_MajorCostSaving_RecommendsSwitchWithHighPriority()
    {
        Fixture fixture = Build(
            Spec("gpt-4o", cost: 10.0m, latency: Sec(8), isCurrent: true, passed: 9),
            Spec("gpt-4o-mini", cost: 3.0m, latency: Sec(4), passed: 9),
            Spec("claude-3-haiku", cost: 4.0m, latency: Sec(5), passed: 9));

        var theories = await fixture.Optimizer.DiscoverTheories(
            fixture.Group, fixture.Cohorts, CancellationToken);

        theories.Should().ContainSingle();
        Captured c = fixture.Captured;
        c.Endpoint.Should().BeSameAs(fixture.RunsByName["gpt-4o-mini"].Endpoint);
        c.Priority.Should().Be(Priority.High); // 70% saving
        c.Rationale.Should().Contain("cost");
    }

    /// <summary>
    /// Scenario: Claude Sonnet is current at 12s latency. GPT-4o-mini does it in 3s
    /// (75% latency improvement) at lower cost too. Both metrics win by same margin,
    /// so the algorithm picks whichever saves more vs current (both 75% here — cost evaluated first).
    /// </summary>
    [TestMethod]
    public async Task ClaudeSonnetToFasterModel_BothMetricsWin_RecommendsSwitchWithHighPriority()
    {
        Fixture fixture = Build(
            Spec("claude-sonnet", cost: 8.0m, latency: Sec(12), isCurrent: true, passed: 8),
            Spec("gpt-4o-mini", cost: 2.0m, latency: Sec(3), passed: 8),
            Spec("gpt-4o", cost: 6.0m, latency: Sec(6), passed: 8));

        var theories = await fixture.Optimizer.DiscoverTheories(
            fixture.Group, fixture.Cohorts, CancellationToken);

        theories.Should().ContainSingle();
        Captured c = fixture.Captured;
        c.Endpoint.Should().BeSameAs(fixture.RunsByName["gpt-4o-mini"].Endpoint);
        c.Priority.Should().Be(Priority.High); // 75% saving
    }

    /// <summary>
    /// Scenario: Current model is cheapest but an alternative is much faster.
    /// However, the faster model has worse pass rate => no recommendation.
    /// </summary>
    [TestMethod]
    public async Task FasterModelWithWorsePassRate_NoRecommendation()
    {
        Fixture fixture = Build(
            Spec("gpt-4o-mini", cost: 2.0m, latency: Sec(10), isCurrent: true, passed: 9),
            Spec("gpt-3.5-turbo", cost: 1.0m, latency: Sec(2), passed: 5),
            Spec("gpt-4o", cost: 8.0m, latency: Sec(5), passed: 10));

        var theories = await fixture.Optimizer.DiscoverTheories(
            fixture.Group, fixture.Cohorts, CancellationToken);

        theories.Should().BeEmpty();
    }

    /// <summary>
    /// Scenario: Three alternatives all beat the current on cost, but the cost winner
    /// must also not regress latency vs the current model.
    /// budget-model: cheapest but far slower than current => disqualified on the cost path.
    /// balanced-model: fastest, cheaper than current => qualifies on the latency path.
    /// </summary>
    [TestMethod]
    public async Task CheapestModelTooSlow_SecondCheapestQualifies()
    {
        Fixture fixture = Build(
            Spec("current", cost: 20.0m, latency: Sec(10), isCurrent: true, passed: 8),
            Spec("budget-model", cost: 2.0m, latency: Sec(50), passed: 8),
            Spec("balanced-model", cost: 5.0m, latency: Sec(6), passed: 8),
            Spec("premium-model", cost: 15.0m, latency: Sec(8), passed: 8));

        var theories = await fixture.Optimizer.DiscoverTheories(
            fixture.Group, fixture.Cohorts, CancellationToken);

        // Cost path: budget-model (2) is cheapest, but its latency (50s) regresses current (10s) => disqualified.
        // Latency path: balanced-model (6s) beats current (10s) by 40% ✓, its cost (5) doesn't
        // regress current (20) ✓, pass rate equal ✓ => balanced-model is proposed.
        theories.Should().ContainSingle();
        Captured c = fixture.Captured;
        c.Endpoint.Should().BeSameAs(fixture.RunsByName["balanced-model"].Endpoint);
    }

    /// <summary>
    /// Scenario: The cheapest alternative beats every other alternative, but its pass rate
    /// regresses the current model's. All gates compare against the current model — the model
    /// the agent would actually switch away from — so no switch may be proposed.
    /// </summary>
    [TestMethod]
    public async Task CheaperModelRegressesCurrentPassRate_NoRecommendation()
    {
        Fixture fixture = Build(
            Spec("current", cost: 5.0m, latency: Sec(5), isCurrent: true, passed: 9),
            Spec("cheap-a", cost: 2.0m, latency: Sec(5), passed: 8),
            Spec("cheap-b", cost: 2.4m, latency: Sec(5), passed: 8));

        var theories = await fixture.Optimizer.DiscoverTheories(
            fixture.Group, fixture.Cohorts, CancellationToken);

        theories.Should().BeEmpty();
    }

    /// <summary>
    /// Scenario: All models perform identically on cost and latency.
    /// No model has a 10%+ margin over the runner-up => no switch.
    /// </summary>
    [TestMethod]
    public async Task AllModelsEqual_NoRecommendation()
    {
        Fixture fixture = Build(
            Spec("model-a", cost: 5.0m, latency: Sec(5), isCurrent: true, passed: 8),
            Spec("model-b", cost: 5.0m, latency: Sec(5), passed: 8),
            Spec("model-c", cost: 5.0m, latency: Sec(5), passed: 8));

        var theories = await fixture.Optimizer.DiscoverTheories(
            fixture.Group, fixture.Cohorts, CancellationToken);

        theories.Should().BeEmpty();
    }

    /// <summary>
    /// Scenario: Medium saving (30%) maps to Medium priority.
    /// </summary>
    [TestMethod]
    public async Task MediumCostSaving_MediumPriority()
    {
        Fixture fixture = Build(
            Spec("expensive", cost: 10.0m, latency: Sec(5), isCurrent: true, passed: 8),
            Spec("cheaper", cost: 7.0m, latency: Sec(5), passed: 8),
            Spec("cheapest", cost: 8.0m, latency: Sec(5), passed: 8));

        var theories = await fixture.Optimizer.DiscoverTheories(
            fixture.Group, fixture.Cohorts, CancellationToken);

        theories.Should().ContainSingle();
        fixture.Captured.Priority.Should().Be(Priority.Medium); // 30% saving
    }

    /// <summary>
    /// Scenario: Small saving (15%) maps to Low priority.
    /// </summary>
    [TestMethod]
    public async Task SmallCostSaving_LowPriority()
    {
        Fixture fixture = Build(
            Spec("current", cost: 10.0m, latency: Sec(5), isCurrent: true, passed: 8),
            Spec("slightly-cheaper", cost: 8.5m, latency: Sec(5), passed: 8),
            Spec("runner-up", cost: 9.8m, latency: Sec(5), passed: 8));

        var theories = await fixture.Optimizer.DiscoverTheories(
            fixture.Group, fixture.Cohorts, CancellationToken);

        theories.Should().ContainSingle();
        fixture.Captured.Priority.Should().Be(Priority.Low); // 15% saving
    }

    /// <summary>
    /// Scenario: Only one alternative model available (plus current).
    /// If it qualifies on both metrics, it should be recommended.
    /// </summary>
    [TestMethod]
    public async Task SingleAlternative_Qualifies_RecommendsSingleModel()
    {
        Fixture fixture = Build(
            Spec("current", cost: 10.0m, latency: Sec(10), isCurrent: true, passed: 8),
            Spec("alternative", cost: 4.0m, latency: Sec(4), passed: 8));

        var theories = await fixture.Optimizer.DiscoverTheories(
            fixture.Group, fixture.Cohorts, CancellationToken);

        theories.Should().ContainSingle();
        Captured c = fixture.Captured;
        c.Endpoint.Should().BeSameAs(fixture.RunsByName["alternative"].Endpoint);
        c.Priority.Should().Be(Priority.High); // 60% cost saving
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
