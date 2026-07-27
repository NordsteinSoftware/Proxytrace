using Autofac;
using Proxytrace.Domain;
using Proxytrace.Domain.Evaluator;
using Proxytrace.Domain.Statistics;
using Proxytrace.Domain.TestResult;
using Proxytrace.Domain.Statistics.TestRun;
using Proxytrace.PerfHarness.Bootstrap;
using Proxytrace.PerfHarness.Reporting;

namespace Proxytrace.PerfHarness.Scenarios;

/// <summary>
/// Times the suite-scoped <c>TestRunStats</c> projection query (issue #253) against the seeded table,
/// plus the dashboard's server-side aggregates over it (issue #288). The test-suites controller loads
/// finalized run statistics for the suites on the current page (and for a single suite GET); before
/// #253 it materialized the whole <c>TestRunStatsEntity</c> table and filtered in memory, an
/// O(all-rows) read on every suites list. The fix pushes the suite set into SQL
/// (<c>WHERE SuiteId IN (...)</c>), so this scenario measures that scoped read — both a single suite
/// (the single-suite GET) and a page of ~50 suites (the suites list) — against a large seeded table.
/// The dashboard's pass-rate summary and sparkline previously materialized the whole table the same
/// way (#288); they now run as a server-side totals aggregate (<c>GetPassTotalsAsync</c>) and a
/// cohort <c>GROUP BY</c> capped to the most recent 50 cohorts (<c>GetRecentCohortsAsync</c>),
/// measured here against the same seeded table.
/// </summary>
internal static class TestRunStatsQueryScenario
{
    public static async Task<IReadOnlyList<MetricResult>> RunAsync(
        PerfContainer container,
        PerfBudgets budgets,
        int warmup,
        int iterations,
        CancellationToken cancellationToken)
    {
        using var scope = container.BeginScope();
        var reader = scope.Resolve<IStatsReader<TestRunStats, TestRunStats.Filter>>();
        var aggregateReader = scope.Resolve<ITestRunStatsReader>();

        // Discover the seeded suites once, outside the timed loop. An empty filter materializes the
        // whole table — exactly the O(all-rows) read #253 removed from the request path — so it is
        // intentionally kept out of the measured loop and used only to pick representative target
        // suite ids (the seeder's suite ids are synthetic and not known to this process otherwise).
        IReadOnlyList<TestRunStats> all = await reader.QueryAsync(new TestRunStats.Filter(), cancellationToken);
        if (all.Count == 0)
        {
            throw new InvalidOperationException("No TestRunStats rows found — run `seed` against this database first.");
        }

        Guid[] suiteIds = all.Select(r => r.SuiteId).Distinct().ToArray();
        // Busiest suite ≈ a realistic single-suite GET (most rows to project).
        Guid singleSuite = all.GroupBy(r => r.SuiteId).OrderByDescending(g => g.Count()).First().Key;
        // A page of suites ≈ the suites-list path (controller pages 50 suites by default).
        Guid[] suitePage = suiteIds.Take(50).ToArray();

        Console.WriteLine($"[db-layer] TestRunStats rows={all.Count:N0}, suites={suiteIds.Length:N0}, page={suitePage.Length}");

        var results = new List<MetricResult>();

        async Task Measure(string name, Func<Task> action)
        {
            var (p50, p95) = await PerfReport.MeasureLatencyAsync(warmup, iterations, action);
            Console.WriteLine($"[db-layer] {name,-26} p50={p50,8:N1}ms  p95={p95,8:N1}ms");
            results.Add(new MetricResult("db-layer", name, p95, budgets.DbQueryBudget(name), "ms", BudgetDirection.LowerIsBetter));
        }

        await Measure("testRunStatsBySuite",
            () => reader.QueryAsync(new TestRunStats.Filter(SuiteIds: [singleSuite]), cancellationToken));
        await Measure("testRunStatsBySuitePage",
            () => reader.QueryAsync(new TestRunStats.Filter(SuiteIds: suitePage), cancellationToken));

        // The dashboard aggregates run unfiltered (the worst case: every row is in scope) — their
        // cost must stay bounded by the GROUP BY, not by the row count materialized.
        await Measure("testRunStatsPassTotals",
            () => aggregateReader.GetPassTotalsAsync(new TestRunStats.Filter(), cancellationToken));
        await Measure("testRunStatsRecentCohorts",
            () => aggregateReader.GetRecentCohortsAsync(new TestRunStats.Filter(), limit: 50, cancellationToken));

        // Evaluator history. These used to take the most recent N test results across the whole
        // install and filter by evaluator in memory, so their cost — and their correctness — was
        // governed by total table size rather than by the evaluator asked about. They now filter in
        // SQL over the EvaluationStat (EvaluatorId, CreatedAt) index, so the budgets below are the
        // regression signal: a drift toward the full-scan aggregates means the filter stopped being
        // pushed down.
        var evaluators = scope.Resolve<IRepository<IEvaluator>>();
        var testResults = scope.Resolve<ITestResultRepository>();
        IReadOnlyList<IEvaluator> seededEvaluators = await evaluators.GetAllAsync(cancellationToken);
        if (seededEvaluators.Count == 0)
        {
            Console.WriteLine("[db-layer] no evaluators seeded — skipping evaluator-history metrics");
            return results;
        }

        Guid evaluatorId = seededEvaluators[0].Id;
        Console.WriteLine($"[db-layer] evaluator history target={evaluatorId}");

        await Measure("evaluatorLatestResult",
            () => testResults.GetLatestByEvaluatorAsync(evaluatorId, cancellationToken));
        await Measure("evaluatorRecentResults",
            () => testResults.GetRecentByEvaluatorAsync(evaluatorId, count: 20, cancellationToken: cancellationToken));
        await Measure("evaluatorSearchResults",
            () => testResults.SearchByEvaluatorAsync(evaluatorId, "the", count: 20, cancellationToken));

        return results;
    }
}
