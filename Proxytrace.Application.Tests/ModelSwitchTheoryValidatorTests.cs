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
using Proxytrace.Domain.TestSuite;
using Proxytrace.Domain.Usage;
using Proxytrace.Testing;

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

    private static Fixture Build(decimal currentCost, decimal proposedCost, bool[] baselinePassed, bool[] candidatePassed)
    {
        var currentEndpoint = MakeEndpoint(currentCost);
        var proposedEndpoint = MakeEndpoint(proposedCost);

        var agent = Substitute.For<IAgent>();
        agent.Endpoint.Returns(currentEndpoint);

        var suite = Substitute.For<ITestSuite>();
        // The validators only score a run that produced a result for every case in the suite.
        var caseCount = Math.Max(baselinePassed.Length, candidatePassed.Length);
        suite.TestCases.Returns(Enumerable.Range(0, caseCount).Select(_ => Substitute.For<ITestCase>()).ToList());

        var baselineId = Guid.NewGuid();
        var candidateId = Guid.NewGuid();
        var baselineRun = MakeRun(baselineId, currentEndpoint, baselinePassed);
        var candidateRun = MakeRun(candidateId, proposedEndpoint, candidatePassed);

        var theory = Substitute.For<IModelSwitchTheory>();
        theory.Agent.Returns(agent);
        theory.Suite.Returns(suite);
        theory.Priority.Returns(Priority.Medium);
        theory.Rationale.Returns("switch");
        theory.ProposedEndpoint.Returns(proposedEndpoint);
        theory.EvidenceTestRunIds.Returns(new[] { baselineId, candidateId });

        var testRuns = Substitute.For<ITestRunRepository>();
        testRuns.FindAsync(baselineId, Arg.Any<CancellationToken>()).Returns(Task.FromResult<ITestRun?>(baselineRun));
        testRuns.FindAsync(candidateId, Arg.Any<CancellationToken>()).Returns(Task.FromResult<ITestRun?>(candidateRun));

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
            new Lazy<ITestRunnerService>(() => Substitute.For<ITestRunnerService>()),
            testRuns,
            new OptimizationOptions { AbSampleCount = 1 });

        return new Fixture { Validator = validator, Theory = theory, Captured = captured };
    }

    private static IModelEndpoint MakeEndpoint(decimal costPerCall)
    {
        var endpoint = Substitute.For<IModelEndpoint>();
        endpoint.Id.Returns(Guid.NewGuid());
        endpoint.CalculateCost(Arg.Any<TokenUsage>()).Returns(costPerCall);
        return endpoint;
    }

    private static ITestRun MakeRun(Guid id, IModelEndpoint endpoint, bool[] passed)
    {
        var results = passed.Select(p =>
        {
            var ev = Substitute.For<Domain.Evaluation.IEvaluation>();
            ev.Passed.Returns(p);
            var result = Substitute.For<ITestResult>();
            result.Evaluations.Returns([ev]);
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
    }

    private sealed class Captured
    {
        public double? CurrentPassRate { get; set; }
        public double? ProposedPassRate { get; set; }
        public decimal? CostDelta { get; set; }
        public TimeSpan? LatencyDelta { get; set; }
    }
}
