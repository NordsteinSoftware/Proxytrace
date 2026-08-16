using Proxytrace.Domain.ModelEndpoint;
using Proxytrace.Domain.TestSuite;

namespace Proxytrace.Domain.TestRunSchedule;

/// <summary>
/// A recurring schedule that runs a <see cref="ITestSuite"/> against a fixed set of endpoints
/// every <see cref="Interval"/>, phased to <see cref="AnchorAt"/>. Polled by the scheduler
/// background service.
/// </summary>
public interface ITestRunSchedule : IDomainEntity<ITestRunSchedule>
{
    string Name { get; }
    ITestSuite Suite { get; }
    IReadOnlyCollection<IModelEndpoint> Endpoints { get; }
    TimeSpan Interval { get; }
    bool IsEnabled { get; }

    /// <summary>
    /// The recurrence phase: the schedule fires at <c>AnchorAt + k·Interval</c> for integer k ≥ 0.
    /// This is what gives time-of-day control — e.g. a daily schedule anchored to today 02:00 fires
    /// every day at 02:00. <see cref="NextRunAt"/> is always the first such instant after "now".
    /// </summary>
    DateTimeOffset AnchorAt { get; }

    DateTimeOffset NextRunAt { get; }
    DateTimeOffset? LastRunAt { get; }

    /// <summary>
    /// Factory delegate for creating a new new instance.
    /// </summary>
    public delegate ITestRunSchedule CreateNew(
        string name, ITestSuite suite, IReadOnlyCollection<IModelEndpoint> endpoints,
        TimeSpan interval, bool isEnabled, DateTimeOffset anchorAt);

    /// <summary>
    /// Factory delegate for creating a new existing instance.
    /// </summary>
    public delegate ITestRunSchedule CreateExisting(
        string name, ITestSuite suite, IReadOnlyCollection<IModelEndpoint> endpoints,
        TimeSpan interval, bool isEnabled, DateTimeOffset anchorAt, DateTimeOffset nextRunAt,
        DateTimeOffset? lastRunAt, IDomainEntityData existing);

    Task<ITestRunSchedule> Disable(CancellationToken cancellationToken = default);
    Task<ITestRunSchedule> Enable(CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a fire at <paramref name="now"/>: sets LastRunAt and advances NextRunAt to the first
    /// anchor-aligned instant strictly after <paramref name="now"/> (missed ticks collapse — no
    /// catch-up burst).
    /// </summary>
    Task<ITestRunSchedule> RecordFired(DateTimeOffset now, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the schedule's editable fields and re-derives <see cref="NextRunAt"/> from the new
    /// <paramref name="anchorAt"/> + <paramref name="interval"/> relative to <paramref name="now"/>,
    /// so changing the cadence or time-of-day reschedules the next run immediately.
    /// </summary>
    Task<ITestRunSchedule> Update(
        string name, IReadOnlyCollection<IModelEndpoint> endpoints,
        TimeSpan interval, bool isEnabled, DateTimeOffset anchorAt, DateTimeOffset now,
        CancellationToken cancellationToken = default);
}
