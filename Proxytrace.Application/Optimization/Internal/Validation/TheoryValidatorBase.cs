using Proxytrace.Application.TestRun;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.ModelEndpoint;
using Proxytrace.Domain.OptimizationProposal;
using Proxytrace.Domain.OptimizationTheory;
using Proxytrace.Domain.TestResult;
using Proxytrace.Domain.TestRun;
using Proxytrace.Domain.TestSuite;

namespace Proxytrace.Application.Optimization.Internal.Validation;

/// <summary>
/// Metrics derived directly from a completed run's test results — computed synchronously,
/// so validation never races the asynchronous statistics projection.
/// </summary>
internal readonly record struct RunMetrics(double? PassRate, decimal? Cost, TimeSpan Latency);

/// <summary>
/// Shared infrastructure for the per-kind theory validators: resolving a baseline run
/// (reusing an evidence run when available, otherwise running the current agent) and
/// executing an ephemeral A/B run for a candidate agent/endpoint.
/// </summary>
internal abstract class TheoryValidatorBase : ITheoryValidator
{
    private readonly Lazy<ITestRunnerService> testRunnerService;
    private readonly ITestRunRepository testRuns;

    protected TheoryValidatorBase(
        Lazy<ITestRunnerService> testRunnerService,
        ITestRunRepository testRuns,
        OptimizationOptions options)
    {
        this.testRunnerService = testRunnerService;
        this.testRuns = testRuns;
        Options = options;
    }

    /// <summary>How hard a theory has to work to be believed — significance level and A/B samples.</summary>
    protected OptimizationOptions Options { get; }

    public abstract bool CanValidate(IOptimizationTheory theory);

    public abstract Task<TheoryValidationOutcome> ValidateAsync(
        IOptimizationTheory theory,
        CancellationToken cancellationToken = default,
        CandidateRunObserver? onCandidateRun = null);

    /// <summary>
    /// Returns the evidence run executed against <paramref name="endpoint"/> if one exists,
    /// otherwise executes a fresh run of <paramref name="agent"/> against that endpoint.
    /// When <paramref name="onRunResolved"/> is supplied it is invoked with the run id as soon as
    /// the run is resolved (a reused evidence run) or created (a fresh run, before it executes).
    /// </summary>
    protected async Task<ITestRun> ResolveRunAsync(
        IOptimizationTheory theory,
        IAgent agent,
        IModelEndpoint endpoint,
        CancellationToken cancellationToken,
        CandidateRunObserver? onRunResolved = null)
    {
        foreach (var evidenceId in theory.EvidenceTestRunIds)
        {
            var run = await testRuns.FindAsync(evidenceId, cancellationToken);
            if (run is not null && run.Endpoint.Id == endpoint.Id)
            {
                if (onRunResolved is not null)
                    await onRunResolved(run.Id, cancellationToken);
                return run;
            }
        }

        return await RunAsync(theory.Suite, agent, endpoint, cancellationToken, onRunResolved);
    }

    /// <summary>
    /// Executes an ephemeral A/B run of <paramref name="agent"/> against <paramref name="endpoint"/>
    /// over the supplied suite. The run is flagged as a system run so it does not re-trigger optimization.
    /// When <paramref name="onRunCreated"/> is supplied it is invoked with the run id the moment the run
    /// is created — before it executes — so an in-flight run can be linked while validation is still running.
    /// </summary>
    protected async Task<ITestRun> RunAsync(
        ITestSuite suite,
        IAgent agent,
        IModelEndpoint endpoint,
        CancellationToken cancellationToken,
        CandidateRunObserver? onRunCreated = null)
        => (await RunSamplesAsync(suite, agent, endpoint, sampleCount: 1, cancellationToken, onRunCreated))[0];

    /// <summary>
    /// Executes <paramref name="sampleCount"/> ephemeral runs of <paramref name="agent"/> over the
    /// suite and returns all of them, so the caller can pool their results into one comparison.
    /// <paramref name="onRunCreated"/> receives the FIRST run's id — that is the run the UI links to
    /// while validation is in flight.
    /// </summary>
    protected async Task<IReadOnlyList<ITestRun>> RunSamplesAsync(
        ITestSuite suite,
        IAgent agent,
        IModelEndpoint endpoint,
        int sampleCount,
        CancellationToken cancellationToken,
        CandidateRunObserver? onRunCreated = null)
    {
        var group = await testRunnerService.Value.RunInForegroundAsync(
            suite: suite,
            endpoints: [endpoint],
            customAgent: agent,
            isSystemTestRun: true,
            onGroupCreated: onRunCreated is null
                ? null
                : async (createdGroup, ct) =>
                {
                    var createdRuns = await createdGroup.GetTestRuns(ct);
                    await onRunCreated(createdRuns.First().Id, ct);
                },
            sampleCount: sampleCount,
            cancellationToken: cancellationToken);

        return await group.GetTestRuns(cancellationToken);
    }

    /// <summary>
    /// Computes pass rate, cost and latency from a run's test results. These are populated
    /// when the run completes, unlike the statistics store which is projected asynchronously.
    /// </summary>
    protected static RunMetrics Metrics(ITestRun run)
    {
        var results = run.TestResults;
        if (results.Count == 0)
            return new RunMetrics(null, null, TimeSpan.Zero);

        double passRate = results.Count(r => r.IsPass()) / (double)results.Count;

        TimeSpan latency = TimeSpan.Zero;
        decimal? cost = null;
        foreach (var result in results)
        {
            latency += result.Latency;
            if (result.Usage is null)
                continue;
            var resultCost = run.Endpoint.CalculateCost(result.Usage);
            if (resultCost.HasValue)
                cost = (cost ?? 0m) + resultCost.Value;
        }

        return new RunMetrics(passRate, cost, latency);
    }

    /// <summary>
    /// Returns the number of passing results and the total result count for a run — the raw
    /// counts a two-proportion test needs, as opposed to the rounded <see cref="RunMetrics.PassRate"/>.
    /// </summary>
    protected static (int Passes, int Total) PassCounts(ITestRun run)
    {
        var results = run.TestResults;
        return (results.Count(r => r.IsPass()), results.Count);
    }

    /// <summary>
    /// Pools <see cref="PassCounts"/> over every sample in one A/B arm. Each sample is an
    /// independent replay of the same suite, so the sums are the arm's total evidence.
    /// </summary>
    protected static (int Passes, int Total) SumPassCounts(IReadOnlyList<ITestRun> runs)
    {
        var counts = runs.Select(PassCounts).ToList();
        return (counts.Sum(c => c.Passes), counts.Sum(c => c.Total));
    }

    /// <summary>
    /// Whether a run is trustworthy as A/B evidence: it must have produced a result for every case in
    /// the suite. A run that failed or cancelled part-way (or skipped a case after an inference error)
    /// leaves fewer results than the suite has cases, so scoring it would compare baseline and
    /// candidate over different numbers of cases and invalidate the two-proportion test — the
    /// validators return Inconclusive instead. (Per-run status is not used: only the group is marked
    /// terminal, so the result count is the reliable completeness signal.)
    /// </summary>
    protected static bool IsRunComplete(ITestRun run, ITestSuite suite)
        => suite.TestCases.Count > 0
           && run.TestResults.Count == suite.TestCases.Count;
}
