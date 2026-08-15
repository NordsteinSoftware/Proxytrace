using System.Globalization;
using Proxytrace.Domain.Statistics.TestRun;
using Proxytrace.Application.TestRun;
using Proxytrace.Domain.OptimizationTheory;
using Proxytrace.Domain.Proposal;
using Proxytrace.Domain.TestRun;
using Proxytrace.Domain.TestRunGroup;

namespace Proxytrace.Application.Optimization.Internal;

/// <summary>
/// Optimizer implementation that hypothesises switching the agent's model endpoint
/// when an alternative wins on cost or latency without regressing pass rate.
/// </summary>
internal sealed class SwitchModelOptimizer : IOptimizerImplementation
{
    /// <summary>
    /// Minimum relative win (vs the runner-up) the best model must have on its winning metric.
    /// </summary>
    private const double MinMargin = 0.10;

    private readonly IModelSwitchTheory.CreateNew factory;

    /// <summary>
    /// Initializes a new instance of the <see cref="SwitchModelOptimizer"/> class.
    /// </summary>
    public SwitchModelOptimizer(IModelSwitchTheory.CreateNew factory)
    {
        this.factory = factory;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<IOptimizationTheory>> DiscoverTheories(
        ITestRunGroup testRunGroup,
        IReadOnlyList<RunCohort> cohorts,
        CancellationToken cancellationToken = default)
    {
        var currentEndpointId = testRunGroup.Suite.Agent.Endpoint.Id;
        var currentCohort = cohorts.FirstOrDefault(c => c.EndpointId == currentEndpointId);

        // we need a cohort for the currently used endpoint, plus at least one alternative
        if (currentCohort is null || cohorts.All(c => c.EndpointId == currentEndpointId))
        {
            return Empty;
        }

        // one aggregated stats row per endpoint cohort; pass rate is the regression gate, so drop
        // cohorts that lack it (no projected/judged samples yet)
        IReadOnlyList<TestRunStats> groupStats = cohorts
            .Select(c => c.Stats)
            .OfType<TestRunStats>()
            .Where(s => s.PassRate.HasValue)
            .ToList();

        // need the current cohort's stats plus at least one alternative to compare against
        if (groupStats.Count < 2)
        {
            return Empty;
        }

        ITestRun currentRun = currentCohort.Representative;
        var currentStats = groupStats.FirstOrDefault(s => s.TestRunId == currentRun.Id);
        if (currentStats is null)
        {
            return Empty;
        }

        // evaluate both metric paths; pick the qualifying winner that saves most vs the current model
        Candidate? chosen = new[] { Metric.Cost, Metric.Latency }
            .Select(metric => Evaluate(metric, groupStats, currentRun.Id, currentStats))
            .Where(c => c is not null)
            .Cast<Candidate>()
            .OrderByDescending(c => c.RelativeSaving)
            .FirstOrDefault();

        if (chosen is null)
        {
            return Empty;
        }

        TestRunStats winner = chosen.Winner;
        ITestRun bestRun = cohorts.First(c => c.Representative.Id == winner.TestRunId).Representative;

        Priority priority = chosen.RelativeSaving switch
        {
            >= 0.50 => Priority.High,
            >= 0.25 => Priority.Medium,
            _ => Priority.Low,
        };

        var metricLabel = chosen.Metric == Metric.Cost ? "cost" : "latency";
        // Invariant culture: the rationale is persisted proposal text, so it must not read
        // "12,3%" merely because the host happens to run under de-DE.
        var savingPct = (chosen.RelativeSaving * 100).ToString("F1", CultureInfo.InvariantCulture);
        var currentName = currentRun.Endpoint.Model.Name;
        var bestName = bestRun.Endpoint.Model.Name;
        var rationale =
            $"Switching from {currentName} to {bestName} cuts {metricLabel} by {savingPct}% vs current "
            + $"with no pass-rate regression. {metricLabel}: {FormatMetric(chosen.Metric, winner)} vs "
            + $"{FormatMetric(chosen.Metric, currentStats)}.";

        var theory = factory(
            agent: testRunGroup.Suite.Agent,
            suite: testRunGroup.Suite,
            source: TheorySource.Optimizer,
            priority: priority,
            rationale: rationale,
            proposedEndpoint: bestRun.Endpoint,
            evidenceTestRunIds: [currentRun.Id, bestRun.Id]);

        return Task.FromResult<IReadOnlyList<IOptimizationTheory>>([theory]);
    }

    private static Task<IReadOnlyList<IOptimizationTheory>> Empty
        => Task.FromResult<IReadOnlyList<IOptimizationTheory>>([]);

    /// <summary>
    /// Ranks the alternative runs by <paramref name="metric"/> (lower is better) and returns a
    /// qualifying candidate when the best alternative beats the <em>current</em> model by
    /// <see cref="MinMargin"/>, does not regress the other metric, and does not regress pass
    /// rate — all measured against the current model, which is what the proposal's rationale
    /// claims and what the agent would actually switch away from.
    /// </summary>
    private static Candidate? Evaluate(
        Metric metric,
        IReadOnlyList<TestRunStats> stats,
        Guid currentRunId,
        TestRunStats currentStats)
    {
        double? currentValue = GetMetric(currentStats, metric);
        if (currentValue is not > 0)
        {
            return null;
        }

        TestRunStats? winner = stats
            .Where(s => s.TestRunId != currentRunId && GetMetric(s, metric).HasValue)
            .OrderBy(s => GetMetric(s, metric) ?? 0d)
            .FirstOrDefault();

        if (winner is null)
        {
            return null;
        }

        double winnerValue = GetMetric(winner, metric) ?? 0d;

        // winning metric: at least MinMargin better than the current model
        double relativeSaving = (currentValue.Value - winnerValue) / currentValue.Value;
        if (relativeSaving < MinMargin)
        {
            return null;
        }

        // other metric: not worse than the current model (both values required)
        Metric other = metric == Metric.Cost ? Metric.Latency : Metric.Cost;
        double? winnerOther = GetMetric(winner, other);
        double? currentOther = GetMetric(currentStats, other);
        if (!winnerOther.HasValue || !currentOther.HasValue || winnerOther.Value > currentOther.Value)
        {
            return null;
        }

        // pass rate: not worse than the current model
        if (!winner.PassRate.HasValue || !currentStats.PassRate.HasValue || winner.PassRate.Value < currentStats.PassRate.Value)
        {
            return null;
        }

        return new Candidate(winner, metric, relativeSaving);
    }

    private static double? GetMetric(TestRunStats stats, Metric metric) => metric switch
    {
        Metric.Cost => stats.Cost.HasValue ? (double)stats.Cost.Value : null,
        Metric.Latency => stats.TotalDuration?.TotalMilliseconds,
        _ => null,
    };

    private static string FormatMetric(Metric metric, TestRunStats stats) 
        => metric switch
        {
            Metric.Cost => stats.Cost?.ToString("0.####", CultureInfo.InvariantCulture) ?? "?",
            Metric.Latency => stats.TotalDuration.HasValue
                ? $"{stats.TotalDuration.Value.TotalSeconds.ToString("F2", CultureInfo.InvariantCulture)}s"
                : "?",
            _ => "?",
        };

    private enum Metric
    {
        Cost,
        Latency,
    }

    private sealed record Candidate(TestRunStats Winner, Metric Metric, double RelativeSaving);
}
