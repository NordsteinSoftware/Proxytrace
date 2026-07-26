using Microsoft.Extensions.Logging;
using Proxytrace.Domain.Statistics;
using Proxytrace.Domain.Statistics.TestRun;
using Proxytrace.Application.TestRun;
using Proxytrace.Common.Async;
using Proxytrace.Domain.OptimizationTheory;
using Proxytrace.Domain.TestRun;
using Proxytrace.Domain.TestRunGroup;

namespace Proxytrace.Application.Optimization.Internal;

/// <summary>
/// Aggregates the theory-producing optimizer implementations. Deduplication, persistence
/// and validation of the produced theories are handled downstream by the theory validation
/// pipeline, so this type only fans out to the implementations and collects their hypotheses.
///
/// Builds the per-endpoint <see cref="RunCohort"/>s once and hands them to every implementation, so
/// each optimizer sees one representative run + aggregated stats per endpoint regardless of how many
/// samples the group ran.
/// </summary>
internal sealed class CompositeOptimizer : IOptimizer
{
    private readonly IReadOnlyCollection<IOptimizerImplementation> optimizers;
    private readonly ITestRunRepository testRuns;
    private readonly IStatsReader<TestRunStats, TestRunStats.Filter> runStats;
    private readonly ILogger<CompositeOptimizer> logger;

    public CompositeOptimizer(
        IReadOnlyCollection<IOptimizerImplementation> optimizers,
        ITestRunRepository testRuns,
        IStatsReader<TestRunStats, TestRunStats.Filter> runStats,
        ILogger<CompositeOptimizer> logger)
    {
        this.optimizers = optimizers.DistinctBy(x => x.GetType()).ToArray();
        this.testRuns = testRuns;
        this.runStats = runStats;
        this.logger = logger;
    }

    public async Task<IReadOnlyList<IOptimizationTheory>> DiscoverTheories(
        ITestRunGroup testRunGroup,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ITestRun> runs = await testRuns.GetByGroupAsync(testRunGroup.Id, cancellationToken);
        if (runs.Count == 0)
            return [];

        IReadOnlyList<TestRunStats> groupStats = await runStats.QueryAsync(
            new TestRunStats.Filter(GroupId: testRunGroup.Id), cancellationToken);
        var statsByRunId = groupStats.ToDictionary(s => s.TestRunId);
        IReadOnlyList<RunCohort> cohorts = RunCohort.Build(runs, statsByRunId);

        return (await optimizers
                .Select(optimizer => DiscoverSafely(optimizer, testRunGroup, cohorts, cancellationToken))
                .Await())
            .SelectMany(x => x)
            .ToArray();
    }

    /// <summary>
    /// Runs one implementation, containing its failure. Each optimizer asks a model for structured
    /// output, and a model that omits a required field throws — without this, one such reply
    /// discarded every OTHER optimizer's theories too, so a run that had a perfectly good
    /// system-prompt hypothesis produced nothing at all (observed live: a tool-definition reply
    /// missing `jsonSchema` took the system-prompt theory down with it). A flaky sibling should cost
    /// its own hypothesis and nothing more.
    /// </summary>
    private async Task<IReadOnlyList<IOptimizationTheory>> DiscoverSafely(
        IOptimizerImplementation optimizer,
        ITestRunGroup testRunGroup,
        IReadOnlyList<RunCohort> cohorts,
        CancellationToken cancellationToken)
    {
        try
        {
            return await optimizer.DiscoverTheories(testRunGroup, cohorts, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(
                ex,
                "Optimizer {Optimizer} failed for test run group {GroupId}; its theories are skipped.",
                optimizer.GetType().Name,
                testRunGroup.Id);
            return [];
        }
    }
}
