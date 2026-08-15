using Proxytrace.Domain.TestRun;
using Proxytrace.Domain.TestRunGroup;

namespace Proxytrace.Storage.Internal.Entities.TestRunGroup;

[StoredDomainEntity(typeof(ITestRunGroup))]
internal record TestRunGroupEntity : Entity
{
    /// <summary>
    /// Gets or sets the suite.
    /// </summary>
    public required Guid Suite { get; init; }
    /// <summary>
    /// Gets or sets the status.
    /// </summary>
    public required TestRunStatus Status { get; init; }
    /// <summary>
    /// Gets or sets the completed at.
    /// </summary>
    public DateTimeOffset? CompletedAt { get; init; }
    /// <summary>
    /// Gets or sets the is system run.
    /// </summary>
    public bool IsSystemRun { get; init; }
    /// <summary>
    /// Gets or sets the schedule id.
    /// </summary>
    public Guid? ScheduleId { get; init; }
    /// <summary>
    /// Gets or sets the sample count.
    /// </summary>
    public int SampleCount { get; init; } = 1;

    /// <summary>
    /// <see cref="ITestRunGroup.OptimizationConsideredAt"/>. Null means the optimizer has not looked
    /// at this group yet — the durable marker its otherwise in-memory queue recovers from.
    /// </summary>
    public DateTimeOffset? OptimizationConsideredAt { get; init; }
}
