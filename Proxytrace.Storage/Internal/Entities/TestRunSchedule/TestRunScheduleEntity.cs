using Proxytrace.Domain.TestRunSchedule;

namespace Proxytrace.Storage.Internal.Entities.TestRunSchedule;

[StoredDomainEntity(typeof(ITestRunSchedule))]
internal record TestRunScheduleEntity : Entity
{
    /// <summary>
    /// Gets or sets the name.
    /// </summary>
    public required string Name { get; init; }
    /// <summary>
    /// Gets or sets the suite.
    /// </summary>
    public required Guid Suite { get; init; }
    /// <summary>
    /// Gets or sets the interval minutes.
    /// </summary>
    public required int IntervalMinutes { get; init; }
    /// <summary>
    /// Gets or sets the is enabled.
    /// </summary>
    public required bool IsEnabled { get; init; }
    /// <summary>
    /// Gets or sets the anchor at.
    /// </summary>
    public required DateTimeOffset AnchorAt { get; init; }
    /// <summary>
    /// Gets or sets the next run at.
    /// </summary>
    public required DateTimeOffset NextRunAt { get; init; }
    /// <summary>
    /// Gets or sets the last run at.
    /// </summary>
    public DateTimeOffset? LastRunAt { get; init; }
    /// <summary>
    /// Gets or sets the schedule endpoints.
    /// </summary>
    public required ICollection<TestRunScheduleEndpointEntity> ScheduleEndpoints { get; init; }
}
