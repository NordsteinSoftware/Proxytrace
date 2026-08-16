using Proxytrace.Domain.OptimizationTheory;
using Proxytrace.Domain.TestRunGroup;

namespace Proxytrace.Application.Optimization;

/// <summary>
/// Analyses a completed test-run group and proposes optimization theories for underperforming agents.
/// </summary>
public interface IOptimizer
{
    /// <summary>
    /// Produces unproven optimization theories from a completed test-run group.
    /// The returned theories are hypotheses that have not yet been persisted or validated.
    /// </summary>
    Task<IReadOnlyList<IOptimizationTheory>> DiscoverTheories(
        ITestRunGroup testRunGroup,
        CancellationToken cancellationToken = default);
}
