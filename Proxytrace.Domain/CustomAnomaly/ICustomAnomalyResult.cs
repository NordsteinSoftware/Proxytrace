namespace Proxytrace.Domain.CustomAnomaly;

/// <summary>
/// Records an anomalous verdict of an <see cref="ICustomAnomalyDetector"/> for one agent call:
/// which detector fired, on which trigger, and the judge's reasoning. Referenced ids are held as
/// plain <see cref="Guid"/>s — the storage layer cascades results away with their detector and
/// call, so a result never outlives either.
/// </summary>
public interface ICustomAnomalyResult : IDomainEntity<ICustomAnomalyResult>
{
    Guid DetectorId { get; }
    Guid AgentCallId { get; }
    Guid ProjectId { get; }

    /// <summary>The trigger pattern whose match gated the review.</summary>
    string MatchedTrigger { get; }

    /// <summary>The judge's reasoning for the anomalous verdict, if it provided one.</summary>
    string? Reasoning { get; }

    /// <summary>
    /// Factory delegate for creating a new new instance.
    /// </summary>
    public delegate ICustomAnomalyResult CreateNew(
        Guid detectorId,
        Guid agentCallId,
        Guid projectId,
        string matchedTrigger,
        string? reasoning);

    /// <summary>
    /// Factory delegate for creating a new existing instance.
    /// </summary>
    public delegate ICustomAnomalyResult CreateExisting(
        Guid detectorId,
        Guid agentCallId,
        Guid projectId,
        string matchedTrigger,
        string? reasoning,
        IDomainEntityData existing);
}
