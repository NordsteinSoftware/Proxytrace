using JetBrains.Annotations;
using Proxytrace.Application.TestRun;
using Proxytrace.Domain.OptimizationProposal;
using Proxytrace.Domain.OptimizationTheory;
using Proxytrace.Domain.TestRun;

namespace Proxytrace.Application.Optimization.Internal.Validation;

/// <summary>
/// Validates a model-switch theory by comparing the agent on its current endpoint against
/// the proposed endpoint. Reuses the evidence runs from the originating group when present,
/// otherwise executes the runs. The switch is accepted only when the proposed endpoint does
/// not regress pass rate AND delivers a real cost or latency win.
/// </summary>
[UsedImplicitly]
internal sealed class ModelSwitchTheoryValidator : TheoryValidatorBase
{
    private readonly IModelSwitchProposal.CreateNew proposalFactory;

    public ModelSwitchTheoryValidator(
        IModelSwitchProposal.CreateNew proposalFactory,
        Lazy<ITestRunnerService> testRunnerService,
        ITestRunRepository testRuns,
        OptimizationOptions options)
        : base(testRunnerService, testRuns, options)
    {
        this.proposalFactory = proposalFactory;
    }

    public override bool CanValidate(IOptimizationTheory theory) => theory is IModelSwitchTheory;

    public override async Task<TheoryValidationOutcome> ValidateAsync(
        IOptimizationTheory theory,
        CancellationToken cancellationToken = default,
        CandidateRunObserver? onCandidateRun = null)
    {
        var modelSwitchTheory = (IModelSwitchTheory)theory;
        var agent = theory.Agent;

        ITestRun baselineRun = await ResolveRunAsync(theory, agent, agent.Endpoint, cancellationToken);
        ITestRun candidateRun = await ResolveRunAsync(theory, agent, modelSwitchTheory.ProposedEndpoint, cancellationToken, onCandidateRun);

        // Never score a partial run (a failed/cancelled case leaves fewer results than the suite).
        if (!IsRunComplete(baselineRun, theory.Suite) || !IsRunComplete(candidateRun, theory.Suite))
        {
            return TheoryValidationOutcome.CouldNotTest;
        }

        RunMetrics baseline = Metrics(baselineRun);
        RunMetrics candidate = Metrics(candidateRun);

        if (baseline.PassRate is not { } basePassRate || candidate.PassRate is not { } candidatePassRate)
        {
            return TheoryValidationOutcome.CouldNotTest;
        }

        (int basePasses, int baseTotal) = PassCounts(baselineRun);
        (int candPasses, int candTotal) = PassCounts(candidateRun);
        double? pValue = ProportionStats.TwoSidedPValue(basePasses, baseTotal, candPasses, candTotal);

        decimal? costDelta = candidate.Cost.HasValue && baseline.Cost.HasValue
            ? candidate.Cost.Value - baseline.Cost.Value
            : null;
        TimeSpan latencyDelta = candidate.Latency - baseline.Latency;

        // Pass-rate must not regress, and the switch must actually save money or time —
        // equal-quality-but-pricier is not a win. The conservative raw-rate comparison never
        // recommends a model that scored worse on the evidence, even by a noisy margin; the p-value
        // is still recorded on the outcome for transparency.
        bool cheaper = costDelta is < 0m;
        bool faster = latencyDelta < TimeSpan.Zero;
        if (candidatePassRate < basePassRate || (!cheaper && !faster))
        {
            return TheoryValidationOutcome.Rejected(basePassRate, candidatePassRate, pValue, candidateRun.Id);
        }

        var proposal = proposalFactory(
            agent: agent,
            priority: theory.Priority,
            rationale: theory.Rationale,
            proposedEndpoint: modelSwitchTheory.ProposedEndpoint,
            currentPassRate: basePassRate,
            proposedPassRate: candidatePassRate,
            expectedCostDelta: costDelta,
            expectedLatencyDelta: latencyDelta,
            evidenceTestRunIds: theory.EvidenceTestRunIds,
            abTestRun: candidateRun);

        return TheoryValidationOutcome.Won(proposal, basePassRate, candidatePassRate, pValue, candidateRun.Id);
    }
}
