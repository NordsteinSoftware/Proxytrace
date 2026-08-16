using Proxytrace.Domain.TestRunGroup;

namespace Proxytrace.Application.Optimization;

/// <summary>
/// Background queue that feeds completed test-run groups through the optimizer pipeline and broadcasts any discovered theories.
/// </summary>
public interface IOptimizerService
{
    /// <summary>
    /// Enqueues a completed test run group for optimization analysis.
    /// Returns immediately; proposals are discovered in the background and broadcast via <see cref="Streaming.IProposalBroadcaster"/>.
    /// </summary>
    Task EnqueueAsync(ITestRunGroup testRunGroup, CancellationToken cancellationToken = default);
}
