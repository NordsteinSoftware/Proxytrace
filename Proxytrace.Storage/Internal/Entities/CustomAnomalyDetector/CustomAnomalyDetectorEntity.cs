using Proxytrace.Domain.CustomAnomaly;

namespace Proxytrace.Storage.Internal.Entities.CustomAnomalyDetector;

[StoredDomainEntity(typeof(ICustomAnomalyDetector))]
internal record CustomAnomalyDetectorEntity : Entity
{
    /// <summary>
    /// Gets or sets the name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>The hidden system agent holding the review instructions as its system prompt.</summary>
    public required Guid Agent { get; init; }

    /// <summary>
    /// Gets or sets the project.
    /// </summary>
    public required Guid Project { get; init; }

    /// <summary>
    /// The trigger list serialized as JSON (mirrors <c>EvaluatorEntity.Data</c>) — triggers are a
    /// value collection, not queryable rows.
    /// </summary>
    public required string Triggers { get; init; }

    /// <summary>
    /// Gets or sets the all agents.
    /// </summary>
    public required bool AllAgents { get; init; }

    /// <summary>
    /// Gets or sets the is enabled.
    /// </summary>
    public required bool IsEnabled { get; init; }

    /// <summary>Whether the proxy rejects trigger-matching requests before they reach the provider.</summary>
    public required bool BlockUpstream { get; init; }

    /// <summary>
    /// Gets or sets the scoped agents.
    /// </summary>
    public required ICollection<CustomAnomalyDetectorAgentEntity> ScopedAgents { get; init; }
}
