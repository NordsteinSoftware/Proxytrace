using AwesomeAssertions;
using NSubstitute;
using Proxytrace.Application.Optimization;
using Proxytrace.Application.Optimization.Internal.Validation;
using Proxytrace.Application.TestRun;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.ModelEndpoint;
using Proxytrace.Domain.OptimizationProposal;
using Proxytrace.Domain.OptimizationTheory;
using Proxytrace.Domain.Proposal;
using Proxytrace.Domain.TestCase;
using Proxytrace.Domain.TestResult;
using Proxytrace.Domain.TestRun;
using Proxytrace.Domain.TestRunGroup;
using Proxytrace.Domain.TestSuite;
using Nordstein.Core.AI.Completions;
using Nordstein.Core.Testing;

namespace Proxytrace.Application.Tests;

[TestClass]
public sealed class ModelSwitchTheoryValidatorTests : BaseTest<Module>
{
    [TestMethod]
    public async Task Validate_CheaperSamePassRate_ProducesProposal()
    {
        var f = Build(currentCost: 10m, proposedCost: 4m, baselinePassed: [true, true], candidatePassed: [true, true]);

        var outcome = await f.Validator.ValidateAsync(f.Theory, CancellationToken);

        outcome.Proposal.Should().NotBeNull();
        outcome.BaselinePassRate.Should().Be(1.0);
        outcome.ProjectedPassRate.Should().Be(1.0);
        f.Captured.CostDelta.Should().Be(-12m); // (4*2) - (10*2)
        f.Captured.CurrentPassRate.Should().Be(1.0);
        f.Captured.ProposedPassRate.Should().Be(1.0);
    }

    [TestMethod]
    public async Task Validate_SameCostSameLatency_NoWin_ReturnsNoProposalButRecordsMetrics()
    {
        var f = Build(currentCost: 10m, proposedCost: 10m, baselinePassed: [true, true], candidatePassed: [true, true]);

        var outcome = await f.Validator.ValidateAsync(f.Theory, CancellationToken);

        outcome.Proposal.Should().BeNull();
        outcome.BaselinePassRate.Should().Be(1.0);
        outcome.ProjectedPassRate.Should().Be(1.0);
    }

    [TestMethod]
    public async Task Validate_CheaperButPassRateRegresses_ReturnsNoProposal()
    {
        var f = Build(currentCost: 10m, proposedCost: 4m, baselinePassed: [true, true], candidatePassed: [true, false]);

        var outcome = await f.Validator.ValidateAsync(f.Theory, CancellationToken);

        outcome.Proposal.Should().BeNull();
    }

    [TestMethod]
    public async Task Validate_ExecutesBothArmsFreshRatherThanReusingStoredEvidence()
    {
        // The baseline used to come from a PREVIOUSLY STORED evidence run while the candidate ran
        // fresh, so anything that drifted between the two — the model's behaviour, a provider
        // update, an edit to the suite — was attributed to the model switch. A theory waiting in the
        // validation queue widened that gap arbitrarily. docs/optimization-loop.md always said the
        // arms run "fresh, back to back"; now they do.
        var f = Build(currentCost: 10m, proposedCost: 4m, baselinePassed: [true, true], candidatePassed: [true, true]);

        await f.Validator.ValidateAsync(f.Theory, CancellationToken);

        await f.Runner.Received(2).RunInForegroundAsync(
            Arg.Any<ITestSuite>(), Arg.Any<IReadOnlyList<IModelEndpoint>>(),
            Arg.Any<IAgent?>(), Arg.Any<bool>(),
            Arg.Any<Func<ITestRunGroup, CancellationToken, Task>?>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Validate_HonoursTheConfiguredSampleCount()
    {
        // AbSampleCount was ignored entirely here, so the setting silently applied to some
        // validators and not others.
        var f = Build(
            currentCost: 10m, proposedCost: 4m,
            baselinePassed: [true, true], candidatePassed: [true, true],
            sampleCount: 3);

        await f.Validator.ValidateAsync(f.Theory, CancellationToken);

        await f.Runner.Received(2).RunInForegroundAsync(
            Arg.Any<ITestSuite>(), Arg.Any<IReadOnlyList<IModelEndpoint>>(),
            Arg.Any<IAgent?>(), Arg.Any<bool>(),
            Arg.Any<Func<ITestRunGroup, CancellationToken, Task>?>(), sampleCount: 3, Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Validate_WithASingleTestCase_IsRefusedAsUnprovable()
    {
        // One case agrees with itself trivially, so it cannot evidence parity. The gate is on the
        // size of the paired comparison — NOT on finding a significant difference, which for a
        // model switch would be backwards: identical answers plus a lower price is the ideal result.
        var f = Build(currentCost: 10m, proposedCost: 4m, baselinePassed: [true], candidatePassed: [true]);

        var outcome = await f.Validator.ValidateAsync(f.Theory, CancellationToken);

        outcome.Proposal.Should().BeNull();
    }

    private static Fixture Build(
        decimal currentCost,
        decimal proposedCost,
        bool[] baselinePassed,
        bool[] candidatePassed,
        int sampleCount = 1)
    {
        var currentEndpoint = MakeEndpoint(currentCost);
        var proposedEndpoint = MakeEndpoint(proposedCost);

        var agent = Substitute.For<IAgent>();
        agent.Endpoint.Returns(currentEndpoint);

        var suite = Substitute.For<ITestSuite>();
        // The validators only score a run that produced a result for every case in the suite.
        var caseCount = Math.Max(baselinePassed.Length, candidatePassed.Length);
        suite.TestCases.Returns(Enumerable.Range(0, caseCount).Select(_ => Substitute.For<ITestCase>()).ToList());

        // Case ids shared across both arms: the significance test is PAIRED on the test case.
        var caseIds = Enumerable.Range(0, caseCount).Select(_ => Guid.NewGuid()).ToArray();
        var baselineRun = MakeRun(Guid.NewGuid(), currentEndpoint, baselinePassed, caseIds);
        var candidateRun = MakeRun(Guid.NewGuid(), proposedEndpoint, candidatePassed, caseIds);

        var theory = Substitute.For<IModelSwitchTheory>();
        theory.Agent.Returns(agent);
        theory.Suite.Returns(suite);
        theory.Priority.Returns(Priority.Medium);
        theory.Rationale.Returns("switch");
        theory.ProposedEndpoint.Returns(proposedEndpoint);
        theory.EvidenceTestRunIds.Returns(Array.Empty<Guid>());

        // Both arms are executed FRESH, back to back — the validator no longer reuses a previously
        // stored evidence run as its baseline, so the runner is what has to be stubbed, not the run
        // repository. Reusing an old run meant any drift since it was recorded (the model's own
        // behaviour, a provider update, an edited suite) was attributed to the model switch.
        // Built before the Returns() call: GroupReturning itself stubs a substitute, and doing that
        // inside the argument list leaves NSubstitute unable to tell which call it is returning from.
        var baselineGroup = GroupReturning(baselineRun);
        var candidateGroup = GroupReturning(candidateRun);

        var runner = Substitute.For<ITestRunnerService>();
        runner.RunInForegroundAsync(
                Arg.Any<ITestSuite>(), Arg.Any<IReadOnlyList<IModelEndpoint>>(),
                Arg.Any<IAgent?>(), Arg.Any<bool>(),
                Arg.Any<Func<ITestRunGroup, CancellationToken, Task>?>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(baselineGroup, candidateGroup);

        var captured = new Captured();
        IModelSwitchProposal.CreateNew factory = (
            _, _, _, _, currentPassRate, proposedPassRate, costDelta, latencyDelta, _, _) =>
        {
            captured.CurrentPassRate = currentPassRate;
            captured.ProposedPassRate = proposedPassRate;
            captured.CostDelta = costDelta;
            captured.LatencyDelta = latencyDelta;
            return Substitute.For<IModelSwitchProposal>();
        };

        var validator = new ModelSwitchTheoryValidator(
            factory,
            new Lazy<ITestRunnerService>(() => runner),
            Substitute.For<ITestRunRepository>(),
            new OptimizationOptions { AbSampleCount = sampleCount });

        return new Fixture { Validator = validator, Theory = theory, Captured = captured, Runner = runner };
    }

    private static IModelEndpoint MakeEndpoint(decimal costPerCall)
    {
        var endpoint = Substitute.For<IModelEndpoint>();
        endpoint.Id.Returns(Guid.NewGuid());
        endpoint.CalculateCost(Arg.Any<TokenUsage>()).Returns(costPerCall);
        return endpoint;
    }

    private static ITestRunGroup GroupReturning(ITestRun run)
    {
        var group = Substitute.For<ITestRunGroup>();
        group.GetTestRuns(Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<ITestRun>>([run]));
        return group;
    }

    private static ITestRun MakeRun(Guid id, IModelEndpoint endpoint, bool[] passed, IReadOnlyList<Guid> caseIds)
    {
        var results = passed.Select((p, i) =>
        {
            var ev = Substitute.For<Domain.Evaluation.IEvaluation>();
            ev.Passed.Returns(p);
            var testCase = Substitute.For<ITestCase>();
            testCase.Id.Returns(caseIds[i]);
            var result = Substitute.For<ITestResult>();
            result.Evaluations.Returns([ev]);
            result.TestCase.Returns(testCase);
            result.Latency.Returns(TimeSpan.FromMilliseconds(100));
            result.Usage.Returns(new TokenUsage(10, 5));
            return result;
        }).ToList();

        var run = Substitute.For<ITestRun>();
        run.Id.Returns(id);
        run.Endpoint.Returns(endpoint);
        run.TestResults.Returns(results);
        return run;
    }

    private sealed class Fixture
    {
        public required ModelSwitchTheoryValidator Validator { get; init; }
        public required IModelSwitchTheory Theory { get; init; }
        public required Captured Captured { get; init; }
        public required ITestRunnerService Runner { get; init; }
    }

    private sealed class Captured
    {
        public double? CurrentPassRate { get; set; }
        public double? ProposedPassRate { get; set; }
        public decimal? CostDelta { get; set; }
        public TimeSpan? LatencyDelta { get; set; }
    }
}
