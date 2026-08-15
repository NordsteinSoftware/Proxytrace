using Proxytrace.Domain.TestRunGroup;

namespace Proxytrace.Application.Optimization;

/// <summary>
/// Service that provides optimizer functionality.
/// </summary>
public interface IOptimizerService
{
    /// <summary>
    /// Enqueues a completed test run group for optimization analysis.
    /// Returns immediately; proposals are discovered in the background and broadcast via <see cref="Streaming.IProposalBroadcaster"/>.
    /// </summary>
    Task EnqueueAsync(ITestRunGroup testRunGroup, CancellationToken cancellationToken = default);
}
