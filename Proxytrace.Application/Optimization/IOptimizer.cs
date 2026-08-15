using Proxytrace.Domain.OptimizationTheory;
using Proxytrace.Domain.TestRunGroup;

namespace Proxytrace.Application.Optimization;

/// <summary>
/// Represents a optimizer.
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
