using Proxytrace.Domain.TestRun;
using Proxytrace.Domain.TestRunGroup;

namespace Proxytrace.Storage.Internal.Entities.TestRunGroup;

[StoredDomainEntity(typeof(ITestRunGroup))]
internal record TestRunGroupEntity : Entity
{
    public required Guid Suite { get; init; }
    public required TestRunStatus Status { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    public bool IsSystemRun { get; init; }
    public Guid? ScheduleId { get; init; }
    public int SampleCount { get; init; } = 1;

    /// <summary>
    /// <see cref="ITestRunGroup.OptimizationConsideredAt"/>. Null means the optimizer has not looked
    /// at this group yet — the durable marker its otherwise in-memory queue recovers from.
    /// </summary>
    public DateTimeOffset? OptimizationConsideredAt { get; init; }
}
