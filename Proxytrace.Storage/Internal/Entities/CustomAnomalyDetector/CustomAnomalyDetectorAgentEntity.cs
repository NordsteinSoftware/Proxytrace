namespace Proxytrace.Storage.Internal.Entities.CustomAnomalyDetector;

/// <summary>
/// Join table for the N:M between detectors and the agents they are scoped to (when not applying
/// to all agents). Storage-only, no domain counterpart.
/// </summary>
internal record CustomAnomalyDetectorAgentEntity
{
    /// <summary>
    /// Gets or sets the detector id.
    /// </summary>
    public required Guid DetectorId { get; init; }
    /// <summary>
    /// Gets or sets the agent id.
    /// </summary>
    public required Guid AgentId { get; init; }
}
