using Proxytrace.Domain.Agent;
using Proxytrace.Domain.ModelEndpoint;
using Proxytrace.Domain.TestRunGroup;
using Proxytrace.Domain.TestSuite;

namespace Proxytrace.Application.TestRun;

public interface ITestRunnerService
{
    /// <summary>
    /// Executes a single-endpoint test run synchronously and returns the completed group.
    /// Used for direct invocations, A/B validation and tests. <paramref name="sampleCount"/> repeats
    /// the suite that many times against the endpoint, which A/B validation pools into one
    /// comparison to get past the small-suite significance floor.
    /// </summary>
    internal Task<ITestRunGroup> RunInForegroundAsync(
        ITestSuite suite,
        IReadOnlyList<IModelEndpoint> endpoints,
        IAgent? customAgent = null,
        bool isSystemTestRun = false,
        Func<ITestRunGroup, CancellationToken, Task>? onGroupCreated = null,
        int sampleCount = 1,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a group of runs — <paramref name="sampleCount"/> per endpoint — executing the same
    /// suite for model comparison. Queues all runs for background execution and returns immediately
    /// with the pending group. The per-endpoint samples are averaged in the UI.
    /// </summary>
    Task<ITestRunGroup> RunInBackgroundAsync(
        ITestSuite suite,
        IReadOnlyList<IModelEndpoint> endpoints,
        Guid? scheduleId = null,
        int sampleCount = 1,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels all in-flight runs in the group and transitions the group to Cancelled.
    /// </summary>
    Task<ITestRunGroup> CancelAsync(
        ITestRunGroup group,
        CancellationToken cancellationToken = default);
}
