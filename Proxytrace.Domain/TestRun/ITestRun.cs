using Proxytrace.Domain.ModelEndpoint;
using Proxytrace.Domain.TestResult;
using Proxytrace.Domain.TestRunGroup;

namespace Proxytrace.Domain.TestRun;

/// <summary>
/// Represents an execution of a test suite against a single endpoint, belonging to a test run group.
/// </summary>
public interface ITestRun : IDomainEntity<ITestRun>
{
    /// <summary>The group this run belongs to, which owns the suite and name.</summary>
    ITestRunGroup Group { get; }

    /// <summary>The endpoint to test against.</summary>
    IModelEndpoint Endpoint { get; }

    /// <summary>
    /// Zero-based index of this run within its endpoint's sample cohort (sample <c>i</c> of
    /// <see cref="ITestRunGroup.SampleCount"/>). Always 0 for single-sample runs.
    /// </summary>
    int SampleIndex { get; }

    /// <summary>The current execution status of this run.</summary>
    TestRunStatus Status { get; }

    /// <summary>The time at which this run finished, or null if still running.</summary>
    DateTimeOffset? CompletedAt { get; }

    /// <summary>The ordered list of results for each test case executed during this run.</summary>
    IReadOnlyList<ITestResult> TestResults { get; }

    /// <summary>Factory delegate for creating a new test run.</summary>
    public delegate ITestRun CreateNew(
        ITestRunGroup group,
        IModelEndpoint endpoint,
        int sampleIndex);

    /// <summary>Factory delegate for reconstituting an existing test run from persistence.</summary>
    public delegate ITestRun CreateExisting(
        ITestRunGroup group,
        IModelEndpoint endpoint,
        int sampleIndex,
        TestRunStatus status,
        DateTimeOffset? completedAt,
        IReadOnlyList<ITestResult> testResults,
        IDomainEntityData existing);

    /// <summary>
    /// Adds the <paramref name="testResult"/> to the <see cref="TestResults"/>.
    /// If all testresults are set, the test run is completed.
    /// </summary>
    Task<ITestRun> SetTestResult(
        ITestResult testResult,
        CancellationToken cancellationToken = default);

    Task<ITestRun> SetRunning(CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks the run finished. <see cref="SetTestResult"/> already completes a run that produced a
    /// result for every case; this is the explicit transition the runner applies once every case has
    /// been *attempted*, which is the only signal available when a case was skipped and therefore
    /// never produced a result. Throws if the run is already terminal.
    /// </summary>
    Task<ITestRun> SetCompleted(CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks the run failed — it finished without producing a result for every case in the suite, so
    /// it is not usable as evidence. Throws if the run is already terminal.
    /// </summary>
    Task<ITestRun> SetFailed(CancellationToken cancellationToken = default);

    Task<ITestRun> SetCancelled(CancellationToken cancellationToken = default);
}
