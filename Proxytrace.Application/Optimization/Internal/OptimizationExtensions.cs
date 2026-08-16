using Proxytrace.Domain.Statistics;
using Proxytrace.Domain.Statistics.TestRun;
using Proxytrace.Domain.Proposal;

namespace Proxytrace.Application.Optimization.Internal;

internal static class OptimizationExtensions
{
    /// <summary>
    /// Gets the optimization priority.
    /// </summary>
    public static Priority GetOptimizationPriority(this TestRunStats stats)
        => GetPriorityFromFailRate(stats.TestCases, stats.Failed);

    /// <summary>
    /// Gets the optimization priority.
    /// </summary>
    public static Priority GetOptimizationPriority(this TestRunStatsAggregate stats)
        => GetPriorityFromFailRate(stats.TestCases, stats.Failed);

    /// <summary>
    /// To aggregate.
    /// </summary>
    public static TestRunStatsAggregate ToAggregate(this TestRunStats stats)
        => new(stats.TestCases, stats.Passed, stats.TotalDuration, stats.Usage, stats.Cost);

    private static Priority GetPriorityFromFailRate(int testCases, int failed)
    {
        if (testCases <= 0)
        {
            return Priority.Low;
        }

        double failRate = failed / (double)testCases;
        return failRate switch
        {
            >= 0.50 => Priority.Critical,
            >= 0.25 => Priority.High,
            >= 0.10 => Priority.Medium,
            _ => Priority.Low,
        };
    }
}
