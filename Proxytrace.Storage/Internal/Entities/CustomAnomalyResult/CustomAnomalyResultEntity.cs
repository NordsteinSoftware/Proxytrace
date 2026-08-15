using Proxytrace.Domain.CustomAnomaly;

namespace Proxytrace.Storage.Internal.Entities.CustomAnomalyResult;

[StoredDomainEntity(typeof(ICustomAnomalyResult))]
internal record CustomAnomalyResultEntity : Entity
{
    /// <summary>
    /// Gets or sets the detector id.
    /// </summary>
    public required Guid DetectorId { get; init; }
    /// <summary>
    /// Gets or sets the agent call id.
    /// </summary>
    public required Guid AgentCallId { get; init; }
    /// <summary>
    /// Gets or sets the project id.
    /// </summary>
    public required Guid ProjectId { get; init; }
    /// <summary>
    /// Gets or sets the matched trigger.
    /// </summary>
    public required string MatchedTrigger { get; init; }
    /// <summary>
    /// Gets or sets the reasoning.
    /// </summary>
    public string? Reasoning { get; init; }
}
