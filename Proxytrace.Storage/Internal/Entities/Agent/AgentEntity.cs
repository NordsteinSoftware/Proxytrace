using Proxytrace.Domain.Agent;
using Proxytrace.Storage.Internal.Entities.Inference;

namespace Proxytrace.Storage.Internal.Entities.Agent;

internal record SystemPromptData(string Name, string Template);

[StoredDomainEntity(typeof(IAgent))]
[Cacheable]
internal record AgentEntity : Entity, IArchivableEntity
{
    /// <summary>
    /// Human-readable display name of the agent, unique within the owning project. Maps to the agents.name column.
    /// </summary>
    public required string Name { get; init; }
    /// <summary>
    /// FK to the project that owns this agent. Maps to the agents.project_id column.
    /// </summary>
    public required Guid Project { get; init; }
    /// <summary>
    /// FK to the model endpoint this agent routes calls through by default. Maps to the agents.endpoint_id column.
    /// </summary>
    public required Guid Endpoint { get; init; }
    /// <summary>
    /// True for agents created automatically by the proxy to represent a recognized configuration, not by a user.
    /// System agents are excluded from the licensed agent count and are filtered out of most user-facing lists.
    /// </summary>
    public required bool IsSystemAgent { get; init; }
    /// <summary>
    /// JSON-serialized inference parameters (temperature, top-p, etc.) applied to calls through this agent.
    /// Maps to the agents.model_parameters column.
    /// </summary>
    public required ModelParametersData ModelParameters { get; init; }

    /// <inheritdoc />
    public bool IsArchived { get; init; }

    /// <summary>The id of the version currently in effect for this agent. Agents are persisted
    /// together with their initial version in a single transaction
    /// (<c>AgentRepository.PersistWithInitialVersionAsync</c>), so this is always populated.</summary>
    public required Guid CurrentVersionId { get; init; }
}
