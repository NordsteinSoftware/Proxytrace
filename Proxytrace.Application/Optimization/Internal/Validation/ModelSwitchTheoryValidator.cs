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

        // Both arms run fresh, back to back, honouring the configured sample count — the same
        // contract the other A/B validators keep, and the one docs/optimization-loop.md states.
        // This used to compare a freshly executed candidate against a PREVIOUSLY STORED baseline
        // run, so anything that had drifted since that run — the model's own behaviour, a provider
        // update, an edit to the suite — was attributed to the model switch. A theory sitting in
        // the validation queue made the gap, and the misattribution, arbitrarily large.
        int samples = Options.AbSampleCount;
        var baselineRuns = await RunSamplesAsync(theory.Suite, agent, agent.Endpoint, samples, cancellationToken);
        var candidateRuns = await RunSamplesAsync(
            theory.Suite, agent, modelSwitchTheory.ProposedEndpoint, samples, cancellationToken, onCandidateRun);

        // Never score a partial run (a failed/cancelled case leaves fewer results than the suite).
        if (baselineRuns.Concat(candidateRuns).Any(r => !IsRunComplete(r, theory.Suite)))
        {
            return TheoryValidationOutcome.CouldNotTest;
        }

        // The run the UI links to; its siblings are the extra samples behind the same comparison.
        ITestRun candidateRun = candidateRuns[0];

        RunMetrics baseline = AggregateMetrics(baselineRuns);
        RunMetrics candidate = AggregateMetrics(candidateRuns);

        if (baseline.PassRate is not { } basePassRate || candidate.PassRate is not { } candidatePassRate)
        {
            return TheoryValidationOutcome.CouldNotTest;
        }

        // Paired on the test case, like the other validators — pooling replays would treat each
        // replay as an independent trial and overstate confidence. See ProportionStats.
        (var basePerCase, var candPerCase) = PairedPassRates(baselineRuns, candidateRuns);
        double? pValue = ProportionStats.PairedTwoSidedPValue(basePerCase, candPerCase);

        decimal? costDelta = candidate.Cost.HasValue && baseline.Cost.HasValue
            ? candidate.Cost.Value - baseline.Cost.Value
            : null;
        TimeSpan latencyDelta = candidate.Latency - baseline.Latency;

        // Pass-rate must not regress, and the switch must actually save money or time —
        // equal-quality-but-pricier is not a win. The raw-rate comparison is deliberately
        // conservative: it never recommends a model that scored worse on the evidence, even by a
        // noisy margin.
        bool cheaper = costDelta is < 0m;
        bool faster = latencyDelta < TimeSpan.Zero;
        if (candidatePassRate < basePassRate || (!cheaper && !faster))
        {
            return TheoryValidationOutcome.Rejected(basePassRate, candidatePassRate, pValue, candidateRun.Id);
        }

        // The evidence gate here is DIRECTIONAL, and deliberately not the one the improvement
        // validators use. A model switch claims *parity* on quality plus a cost or latency win, so
        // demanding a statistically significant DIFFERENCE would be exactly backwards: it would
        // reject the ideal result — identical answers, materially cheaper — and accept only switches
        // that measurably changed the output. Note in particular that a null p-value here usually
        // means "the two arms agreed on every case", which is the best possible outcome, not a
        // missing one.
        //
        // What must be ruled out is concluding parity from evidence that could not have shown a
        // difference in the first place. Fewer than two paired cases cannot: a one-case suite agrees
        // with itself trivially. So the gate is on the size of the paired comparison, not on its
        // result. The kiosk showcase drops it to keep the demo moving, as it does for the others.
        const int minimumPairedCases = 2;
        if (Options.RequireStatisticalSignificance && basePerCase.Count < minimumPairedCases)
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
