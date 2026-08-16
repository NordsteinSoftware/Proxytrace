using Proxytrace.Application.TestRun;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.OptimizationProposal;
using Proxytrace.Domain.OptimizationTheory;
using Proxytrace.Domain.TestRun;

namespace Proxytrace.Application.Optimization.Internal.Validation;

/// <summary>
/// Validates theories whose change is expressed as an <em>ephemeral agent</em> tested on the
/// agent's own endpoint (system-prompt and tool updates). It owns the common A/B flow —
/// resolve a baseline, run the candidate, gate on a pass-rate improvement — and leaves the
/// kind-specific bits (how the candidate agent is built and which proposal is produced) to
/// the subclass.
/// </summary>
internal abstract class AbTestTheoryValidator<TTheory> : TheoryValidatorBase
    where TTheory : class, IOptimizationTheory
{
    protected AbTestTheoryValidator(
        Lazy<ITestRunnerService> testRunnerService,
        ITestRunRepository testRuns,
        OptimizationOptions options)
        : base(testRunnerService, testRuns, options)
    {
    }

    /// <summary>
    /// Determines whether the validate.
    /// </summary>
    public sealed override bool CanValidate(IOptimizationTheory theory) => theory is TTheory;

    /// <summary>
    /// Validates asynchronously.
    /// </summary>
    public sealed override async Task<TheoryValidationOutcome> ValidateAsync(
        IOptimizationTheory theory,
        CancellationToken cancellationToken = default,
        CandidateRunObserver? onCandidateRun = null)
    {
        var typedTheory = (TTheory)theory;
        var agent = theory.Agent;

        // Run the baseline and candidate fresh, back to back, against the same suite and the
        // agent's current state — so the only difference between them is the proposed change.
        // Reusing an older evidence run as the baseline would conflate the change's effect with
        // any drift in the agent since that run, especially when a theory waits in the queue.
        int samples = Options.AbSampleCount;
        var baselineRuns = await RunSamplesAsync(theory.Suite, agent, agent.Endpoint, samples, cancellationToken);
        IAgent candidateAgent = BuildCandidateAgent(agent, typedTheory);
        var candidateRuns = await RunSamplesAsync(
            theory.Suite, candidateAgent, agent.Endpoint, samples, cancellationToken, onCandidateRun);

        // Never score a partial run: a case failure (or cancellation) leaves fewer results than the
        // suite has cases, which would make the A/B comparison unfair and the proposal unfounded.
        if (baselineRuns.Concat(candidateRuns).Any(r => !IsRunComplete(r, theory.Suite)))
        {
            return TheoryValidationOutcome.CouldNotTest;
        }

        // The run the UI links to; its siblings are the extra samples behind the same comparison.
        ITestRun candidateRun = candidateRuns[0];

        // Pooled counts give the pass rates that are REPORTED — "of everything attempted, this
        // fraction passed" — which is what a reader expects to see.
        (int basePasses, int baseTotal) = SumPassCounts(baselineRuns);
        (int candPasses, int candTotal) = SumPassCounts(candidateRuns);

        if (baseTotal == 0 || candTotal == 0)
        {
            return TheoryValidationOutcome.CouldNotTest;
        }

        double basePassRate = basePasses / (double)baseTotal;
        double candidatePassRate = candPasses / (double)candTotal;

        // The significance test, however, must NOT consume those pooled totals. Replays of one test
        // case are not independent trials of each other, so summing them inflated the sample size by
        // the replay count and shrank the standard error by roughly its square root — the gate got
        // easier to pass the more samples an operator configured, which is exactly backwards. Both
        // arms run the same cases, so the test is paired on the case. See ProportionStats.
        (var basePerCase, var candPerCase) = PairedPassRates(baselineRuns, candidateRuns);
        double? pValue = ProportionStats.PairedTwoSidedPValue(basePerCase, candPerCase);

        if (candidatePassRate <= basePassRate)
        {
            return TheoryValidationOutcome.Rejected(basePassRate, candidatePassRate, pValue, candidateRun.Id);
        }

        // An improvement only wins when it is distinguishable from sampling noise — on a small
        // suite a couple of flaky cases can flip the raw pass rate either way. Without this gate
        // every lucky run would spawn a proposal. The kiosk showcase is the one place that trades
        // it away for a demo that finishes on stage; the p-value is still computed and stored so
        // the proposal can be labelled as the weaker evidence it is.
        if (Options.RequireStatisticalSignificance && (pValue is not { } p || p > Options.SignificanceLevel))
        {
            return TheoryValidationOutcome.Rejected(basePassRate, candidatePassRate, pValue, candidateRun.Id);
        }

        var proposal = BuildProposal(typedTheory, basePassRate, candidatePassRate, candidateRun);
        return TheoryValidationOutcome.Won(proposal, basePassRate, candidatePassRate, pValue, candidateRun.Id);
    }

    /// <summary>Builds the ephemeral agent carrying the proposed change.</summary>
    protected abstract IAgent BuildCandidateAgent(IAgent agent, TTheory theory);

    /// <summary>Builds the Draft proposal once the change has been shown to improve the agent.</summary>
    protected abstract IOptimizationProposal BuildProposal(
        TTheory theory,
        double currentPassRate,
        double proposedPassRate,
        ITestRun candidateRun);
}
