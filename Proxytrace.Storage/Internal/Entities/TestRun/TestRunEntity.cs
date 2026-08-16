using Proxytrace.Domain.TestRun;

namespace Proxytrace.Storage.Internal.Entities.TestRun;

[StoredDomainEntity(typeof(ITestRun))]
internal record TestRunEntity : Entity
{
    /// <summary>
    /// Gets or sets the group.
    /// </summary>
    public required Guid Group { get; init; }
    /// <summary>
    /// Gets or sets the endpoint.
    /// </summary>
    public required Guid Endpoint { get; init; }
    /// <summary>
    /// Gets or sets the sample index.
    /// </summary>
    public int SampleIndex { get; init; }
    /// <summary>
    /// Gets or sets the status.
    /// </summary>
    public required TestRunStatus Status { get; init; }
    /// <summary>
    /// Gets or sets the completed at.
    /// </summary>
    public DateTimeOffset? CompletedAt { get; init; }
    /// <summary>
    /// Gets or sets the test results.
    /// </summary>
    public required IReadOnlyCollection<Guid> TestResults { get; init; }
}
