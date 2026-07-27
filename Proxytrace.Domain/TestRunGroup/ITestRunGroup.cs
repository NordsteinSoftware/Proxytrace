using Proxytrace.Domain.TestRun;
using Proxytrace.Domain.TestSuite;

namespace Proxytrace.Domain.TestRunGroup;

/// <summary>
/// Groups test runs executing the same suite against multiple endpoints for model comparison.
/// </summary>
public interface ITestRunGroup : IDomainEntity<ITestRunGroup>
{
    /// <summary>
    /// Hard cap on the number of model endpoints a single suite run (or schedule) may target.
    /// Enforced in the domain (<c>TestRunSchedule</c>), the runner service, and the API.
    /// </summary>
    public const int MaxModelEndpoints = 3;

    /// <summary>
    /// Hard cap on the number of samples (repeated runs) a single endpoint may be run for in one
    /// group. Enforced in the runner service and the API. A group holds up to
    /// <see cref="MaxModelEndpoints"/> × <see cref="MaxSampleCount"/> runs.
    /// </summary>
    public const int MaxSampleCount = 5;

    /// <summary>The test suite executed by all runs in this group.</summary>
    ITestSuite Suite { get; }

    /// <summary>
    /// How many times each endpoint is run (1..<see cref="MaxSampleCount"/>). The per-endpoint runs
    /// (a "cohort") are averaged in the UI and aggregated to one representative run for the
    /// optimization loop. 1 for legacy/manual single-sample runs.
    /// </summary>
    int SampleCount { get; }

    /// <summary>The aggregate execution status across all child runs.</summary>
    TestRunStatus Status { get; }

    /// <summary>When the last child run finished, or null if still running.</summary>
    DateTimeOffset? CompletedAt { get; }

    /// <summary>
    /// True for ephemeral A/B runs spawned internally to validate optimization theories.
    /// These are hidden from the user-facing test-run list by default.
    /// </summary>
    bool IsSystemRun { get; }

    /// <summary>The schedule that triggered this group, or null for a manual/system run.</summary>
    Guid? ScheduleId { get; }

    /// <summary>
    /// When the optimizer finished looking at this group, or <see langword="null"/> if it has not
    /// yet been considered.
    /// </summary>
    /// <remarks>
    /// A durable marker for a queue that is otherwise in memory. Completed groups are handed to the
    /// optimizer through an unbounded in-process channel; a restart discarded whatever was still
    /// queued, so a deploy during a scheduled-run window silently dropped that night's optimization
    /// with nothing recorded anywhere. The theory queue can recover because a theory's status *is*
    /// its marker — a group had no equivalent, which is what this is. It is set once the group has
    /// been considered, whether or not that produced any theories: "looked at and found nothing" and
    /// "never looked at" must not be confused.
    /// </remarks>
    DateTimeOffset? OptimizationConsideredAt { get; }

    /// <summary>Records that the optimizer has considered this group, and persists.</summary>
    Task<ITestRunGroup> MarkOptimizationConsidered(CancellationToken cancellationToken = default);

    /// <summary>Factory delegate for creating a new test run group.</summary>
    public delegate ITestRunGroup CreateNew(ITestSuite suite, bool isSystemRun, Guid? scheduleId, int sampleCount);

    /// <summary>Factory delegate for reconstituting an existing test run group from persistence.</summary>
    public delegate ITestRunGroup CreateExisting(
        ITestSuite suite,
        TestRunStatus status,
        DateTimeOffset? completedAt,
        bool isSystemRun,
        Guid? scheduleId,
        int sampleCount,
        DateTimeOffset? optimizationConsideredAt,
        IDomainEntityData existing);

    Task<ITestRunGroup> SetRunning(CancellationToken cancellationToken = default);
    Task<ITestRunGroup> SetCompleted(CancellationToken cancellationToken = default);
    Task<ITestRunGroup> SetFailed(CancellationToken cancellationToken = default);
    Task<ITestRunGroup> SetCancelled(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ITestRun>> GetTestRuns(CancellationToken cancellationToken = default);
}
