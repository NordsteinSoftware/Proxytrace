using AwesomeAssertions;
using NSubstitute;
using Proxytrace.Application.Optimization;
using Proxytrace.Application.Optimization.Internal.Validation;
using Proxytrace.Application.TestRun;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.Evaluation;
using Proxytrace.Domain.Inference;
using Proxytrace.Domain.ModelEndpoint;
using Proxytrace.Domain.OptimizationProposal;
using Proxytrace.Domain.OptimizationTheory;
using Proxytrace.Domain.Prompt;
using Proxytrace.Domain.Proposal;
using Proxytrace.Domain.TestCase;
using Proxytrace.Domain.TestResult;
using Proxytrace.Domain.TestRun;
using Proxytrace.Domain.TestRunGroup;
using Proxytrace.Domain.TestSuite;
using Proxytrace.Domain.Tools;
using Proxytrace.Testing;

namespace Proxytrace.Application.Tests;

[TestClass]
public sealed class SystemPromptTheoryValidatorTests : BaseTest<Module>
{
    /// <summary>
    /// One sample per arm — the fixture stubs exactly two foreground runs (baseline, candidate).
    /// <see cref="Validate_PoolsSamples_SoASmallSuiteCanReachSignificance"/> covers the multi-sample path.
    /// </summary>
    private static readonly OptimizationOptions TestOptions = new() { AbSampleCount = 1 };

    [TestMethod]
    public async Task Validate_CandidateImproves_ProducesProposal()
    {
        // A large improvement over a decent sample (10/50 → 45/50) — far beyond sampling noise.
        var f = Build(baselinePassed: Passes(10, 50), candidatePassed: Passes(45, 50));

        var outcome = await f.Validator.ValidateAsync(f.Theory, CancellationToken);

        outcome.Proposal.Should().NotBeNull();
        outcome.BaselinePassRate.Should().Be(0.2);
        outcome.ProjectedPassRate.Should().Be(0.9);
        outcome.PValue.Should().BeLessThan(TestOptions.SignificanceLevel);
        f.Captured.CurrentPassRate.Should().Be(0.2);
        f.Captured.ProposedPassRate.Should().Be(0.9);
    }

    [TestMethod]
    public async Task Validate_ImprovementWithinNoise_ReturnsNoProposalButRecordsMetrics()
    {
        // 1/2 → 2/2 looks like an improvement but is statistically indistinguishable from a
        // single flaky case — it must not spawn a proposal.
        var f = Build(baselinePassed: [true, false], candidatePassed: [true, true]);

        var outcome = await f.Validator.ValidateAsync(f.Theory, CancellationToken);

        outcome.Proposal.Should().BeNull();
        outcome.BaselinePassRate.Should().Be(0.5);
        outcome.ProjectedPassRate.Should().Be(1.0);
        outcome.PValue.Should().BeGreaterThan(TestOptions.SignificanceLevel);
    }

    [TestMethod]
    public async Task Validate_CandidateNoImprovement_ReturnsNoProposalButRecordsMetrics()
    {
        var f = Build(baselinePassed: [true, true], candidatePassed: [true, true]);

        var outcome = await f.Validator.ValidateAsync(f.Theory, CancellationToken);

        outcome.Proposal.Should().BeNull();
        outcome.BaselinePassRate.Should().Be(1.0);
        outcome.ProjectedPassRate.Should().Be(1.0);
    }

    [TestMethod]
    public async Task Validate_CandidateRegresses_ReturnsNoProposal()
    {
        var f = Build(baselinePassed: [true, true], candidatePassed: [true, false]);

        var outcome = await f.Validator.ValidateAsync(f.Theory, CancellationToken);

        outcome.Proposal.Should().BeNull();
        outcome.BaselinePassRate.Should().Be(1.0);
        outcome.ProjectedPassRate.Should().Be(0.5);
    }

    [TestMethod]
    public async Task Validate_UnevaluatedResults_DoNotCountAsPass()
    {
        // A run whose results carry no evaluations must score 0, not 1 (All() over empty is vacuously true).
        var f = Build(baselinePassed: [true], candidatePassed: []); // candidate result has zero evaluations
        f.OverrideCandidate(MakeRunWithEmptyEvaluations(1));

        var outcome = await f.Validator.ValidateAsync(f.Theory, CancellationToken);

        outcome.Proposal.Should().BeNull();
    }

    [TestMethod]
    public async Task Validate_PoolsSamples_SoASmallSuiteCanReachSignificance()
    {
        // The showcase's real numbers: an 11-case suite going 5/11 → 8/11. That is a large, genuine
        // improvement, but on a single sample per arm it lands at p≈0.19 and is discarded as noise —
        // which is exactly why the kiosk demo's optimizer never produced a proposal. Replaying the
        // same per-sample outcome three times pools it into 15/33 → 24/33, which clears 0.05.
        var f = BuildSampled(
            samples: 3,
            baselinePassed: Passes(5, 11),
            candidatePassed: Passes(8, 11));

        var outcome = await f.Validator.ValidateAsync(f.Theory, CancellationToken);

        outcome.Proposal.Should().NotBeNull("pooling three samples makes the 5/11 → 8/11 effect provable");
        outcome.BaselinePassRate.Should().BeApproximately(15d / 33d, 1e-9);
        outcome.ProjectedPassRate.Should().BeApproximately(24d / 33d, 1e-9);
        outcome.PValue.Should().BeLessThan(0.05);
    }

    [TestMethod]
    public async Task Validate_SingleSample_RejectsTheSameEffectAsNoise()
    {
        // The control for the test above: identical per-sample rates, one sample per arm, no proposal.
        var f = BuildSampled(samples: 1, baselinePassed: Passes(5, 11), candidatePassed: Passes(8, 11));

        var outcome = await f.Validator.ValidateAsync(f.Theory, CancellationToken);

        outcome.Proposal.Should().BeNull("one sample of an 11-case suite cannot resolve this effect");
        outcome.PValue.Should().BeGreaterThan(0.05);
    }

    [TestMethod]
    public async Task Validate_WithoutSignificanceRequirement_AcceptsAnImprovementAndStillRecordsThePValue()
    {
        // The kiosk showcase's gate: the same under-powered 5/11 → 8/11 wins on the improvement
        // alone. The p-value must survive into the outcome — the UI labels such a proposal as an
        // improvement rather than a significant result, and it cannot do that without the number.
        var f = BuildSampled(
            samples: 1,
            baselinePassed: Passes(5, 11),
            candidatePassed: Passes(8, 11),
            options: OptimizationOptions.KioskShowcase);

        var outcome = await f.Validator.ValidateAsync(f.Theory, CancellationToken);

        outcome.Proposal.Should().NotBeNull();
        outcome.PValue.Should().BeGreaterThan(0.05, "the win is not significance-backed, and the UI must be able to say so");
    }

    [TestMethod]
    public async Task Validate_WithoutSignificanceRequirement_StillRejectsARegression()
    {
        // Dropping the significance gate must not drop the improvement gate: a candidate that is no
        // better than the baseline loses whatever the configuration says.
        var f = BuildSampled(
            samples: 1,
            baselinePassed: Passes(8, 11),
            candidatePassed: Passes(5, 11),
            options: OptimizationOptions.KioskShowcase);

        var outcome = await f.Validator.ValidateAsync(f.Theory, CancellationToken);

        outcome.Proposal.Should().BeNull();
    }

    private static bool[] Passes(int passed, int total)
        => Enumerable.Range(0, total).Select(i => i < passed).ToArray();

    private Fixture Build(bool[] baselinePassed, bool[] candidatePassed)
    {
        var endpoint = Substitute.For<IModelEndpoint>();
        endpoint.Id.Returns(Guid.NewGuid());

        var agent = Substitute.For<IAgent>();
        agent.Name.Returns("agent");
        agent.Endpoint.Returns(endpoint);
        agent.Tools.Returns(new List<ToolSpecification>());
        agent.Project.Returns(Substitute.For<Domain.Project.IProject>());
        agent.SystemPrompt.Returns(Substitute.For<IPromptTemplate>());
        agent.ModelParameters.Returns(Substitute.For<IModelParameters>());

        var suite = Substitute.For<ITestSuite>();
        // The validators only score a run that produced a result for every case in the suite, so the
        // suite must have as many cases as the (equal-length) baseline/candidate runs.
        var caseCount = Math.Max(baselinePassed.Length, candidatePassed.Length);
        suite.TestCases.Returns(Enumerable.Range(0, caseCount).Select(_ => Substitute.For<ITestCase>()).ToList());

        var theory = Substitute.For<ISystemPromptTheory>();
        theory.Agent.Returns(agent);
        theory.Suite.Returns(suite);
        theory.Priority.Returns(Priority.Medium);
        theory.Rationale.Returns("better prompt");
        theory.ProposedSystemMessage.Returns("You are better.");
        theory.EvidenceTestRunIds.Returns(Array.Empty<Guid>());

        var baselineRun = MakeRun(endpoint, baselinePassed);
        var candidateRun = MakeRun(endpoint, candidatePassed);
        var baselineGroup = GroupReturning(baselineRun);
        var candidateGroup = GroupReturning(candidateRun);

        var runner = Substitute.For<ITestRunnerService>();
        runner.RunInForegroundAsync(
                Arg.Any<ITestSuite>(), Arg.Any<IReadOnlyList<IModelEndpoint>>(),
                Arg.Any<IAgent?>(), Arg.Any<bool>(),
                Arg.Any<Func<ITestRunGroup, CancellationToken, Task>?>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(baselineGroup, candidateGroup);

        var captured = new Captured();
        ISystemPromptProposal.CreateNew proposalFactory = (
            _, _, _, _, currentPassRate, proposedPassRate, _, _) =>
        {
            captured.CurrentPassRate = currentPassRate;
            captured.ProposedPassRate = proposedPassRate;
            return Substitute.For<ISystemPromptProposal>();
        };

        IPromptTemplate.Create promptFactory = (_, _) => Substitute.For<IPromptTemplate>();
        IAgent.CreateNew agentFactory = (_, _, _, _, _, _, _) =>
        {
            var candidate = Substitute.For<IAgent>();
            candidate.Endpoint.Returns(endpoint);
            return candidate;
        };

        var validator = new SystemPromptTheoryValidator(
            proposalFactory, promptFactory, agentFactory,
            new Lazy<ITestRunnerService>(() => runner),
            Substitute.For<ITestRunRepository>(),
            TestOptions);

        return new Fixture { Validator = validator, Theory = theory, Captured = captured, Runner = runner, Endpoint = endpoint, Baseline = baselineRun };
    }

    private static ITestRunGroup GroupReturning(ITestRun run)
    {
        var group = Substitute.For<ITestRunGroup>();
        group.GetTestRuns(Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<ITestRun>>([run]));
        return group;
    }

    /// <summary>
    /// Like <see cref="Build"/>, but each arm's group returns <paramref name="samples"/> identical
    /// runs — the shape the runner produces for a multi-sample foreground run.
    /// </summary>
    private Fixture BuildSampled(
        int samples,
        bool[] baselinePassed,
        bool[] candidatePassed,
        OptimizationOptions? options = null)
    {
        var endpoint = Substitute.For<IModelEndpoint>();
        endpoint.Id.Returns(Guid.NewGuid());

        var agent = Substitute.For<IAgent>();
        agent.Name.Returns("agent");
        agent.Endpoint.Returns(endpoint);
        agent.Tools.Returns(new List<ToolSpecification>());
        agent.Project.Returns(Substitute.For<Domain.Project.IProject>());
        agent.SystemPrompt.Returns(Substitute.For<IPromptTemplate>());
        agent.ModelParameters.Returns(Substitute.For<IModelParameters>());

        var suite = Substitute.For<ITestSuite>();
        var caseCount = Math.Max(baselinePassed.Length, candidatePassed.Length);
        suite.TestCases.Returns(Enumerable.Range(0, caseCount).Select(_ => Substitute.For<ITestCase>()).ToList());

        var theory = Substitute.For<ISystemPromptTheory>();
        theory.Agent.Returns(agent);
        theory.Suite.Returns(suite);
        theory.Priority.Returns(Priority.Medium);
        theory.Rationale.Returns("better prompt");
        theory.ProposedSystemMessage.Returns("You are better.");
        theory.EvidenceTestRunIds.Returns(Array.Empty<Guid>());

        // Build every substitute BEFORE the Returns() call — NSubstitute cannot configure a
        // substitute while it is resolving the arguments of another one.
        var baselineRuns = Enumerable.Range(0, samples).Select(_ => MakeRun(endpoint, baselinePassed)).ToList();
        var candidateRuns = Enumerable.Range(0, samples).Select(_ => MakeRun(endpoint, candidatePassed)).ToList();
        var baselineGroup = GroupReturningMany(baselineRuns);
        var candidateGroup = GroupReturningMany(candidateRuns);

        var runner = Substitute.For<ITestRunnerService>();
        runner.RunInForegroundAsync(
                Arg.Any<ITestSuite>(), Arg.Any<IReadOnlyList<IModelEndpoint>>(),
                Arg.Any<IAgent?>(), Arg.Any<bool>(),
                Arg.Any<Func<ITestRunGroup, CancellationToken, Task>?>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(baselineGroup, candidateGroup);

        var captured = new Captured();
        ISystemPromptProposal.CreateNew proposalFactory = (
            _, _, _, _, currentPassRate, proposedPassRate, _, _) =>
        {
            captured.CurrentPassRate = currentPassRate;
            captured.ProposedPassRate = proposedPassRate;
            return Substitute.For<ISystemPromptProposal>();
        };

        IPromptTemplate.Create promptFactory = (_, _) => Substitute.For<IPromptTemplate>();
        IAgent.CreateNew agentFactory = (_, _, _, _, _, _, _) =>
        {
            var candidate = Substitute.For<IAgent>();
            candidate.Endpoint.Returns(endpoint);
            return candidate;
        };

        var validator = new SystemPromptTheoryValidator(
            proposalFactory, promptFactory, agentFactory,
            new Lazy<ITestRunnerService>(() => runner),
            Substitute.For<ITestRunRepository>(),
            (options ?? new OptimizationOptions()) with { AbSampleCount = samples });

        return new Fixture { Validator = validator, Theory = theory, Captured = captured, Runner = runner, Endpoint = endpoint, Baseline = baselineRuns[0] };
    }

    private static ITestRunGroup GroupReturningMany(IReadOnlyList<ITestRun> runs)
    {
        var group = Substitute.For<ITestRunGroup>();
        group.GetTestRuns(Arg.Any<CancellationToken>()).Returns(Task.FromResult(runs));
        return group;
    }

    private static ITestRun MakeRun(IModelEndpoint endpoint, bool[] passed)
    {
        var results = passed.Select(p =>
        {
            var ev = Substitute.For<IEvaluation>();
            ev.Passed.Returns(p);
            var r = Substitute.For<ITestResult>();
            r.Evaluations.Returns([ev]);
            return r;
        }).ToList();

        var run = Substitute.For<ITestRun>();
        run.Id.Returns(Guid.NewGuid());
        run.Endpoint.Returns(endpoint);
        run.TestResults.Returns(results);
        return run;
    }

    private static ITestRun MakeRunWithEmptyEvaluations(int resultCount)
    {
        var results = Enumerable.Range(0, resultCount).Select(_ =>
        {
            var r = Substitute.For<ITestResult>();
            r.Evaluations.Returns(Array.Empty<IEvaluation>());
            return r;
        }).ToList();

        var run = Substitute.For<ITestRun>();
        run.Id.Returns(Guid.NewGuid());
        run.TestResults.Returns(results);
        return run;
    }

    private sealed class Fixture
    {
        public required SystemPromptTheoryValidator Validator { get; init; }
        public required ISystemPromptTheory Theory { get; init; }
        public required Captured Captured { get; init; }
        public required ITestRunnerService Runner { get; init; }
        public required IModelEndpoint Endpoint { get; init; }
        public required ITestRun Baseline { get; init; }

        public void OverrideCandidate(ITestRun candidate)
        {
            var baselineGroup = GroupReturning(Baseline);
            var candidateGroup = GroupReturning(candidate);
            Runner.RunInForegroundAsync(
                    Arg.Any<ITestSuite>(), Arg.Any<IReadOnlyList<IModelEndpoint>>(),
                    Arg.Any<IAgent?>(), Arg.Any<bool>(),
                    Arg.Any<Func<ITestRunGroup, CancellationToken, Task>?>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
                .Returns(baselineGroup, candidateGroup);
        }
    }

    private sealed class Captured
    {
        public double? CurrentPassRate { get; set; }
        public double? ProposedPassRate { get; set; }
    }
}
