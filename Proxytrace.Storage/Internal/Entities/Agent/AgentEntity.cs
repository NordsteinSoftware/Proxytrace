using Proxytrace.Domain.Agent;
using Proxytrace.Storage.Internal.Entities.Inference;

namespace Proxytrace.Storage.Internal.Entities.Agent;

internal record SystemPromptData(string Name, string Template);

[StoredDomainEntity(typeof(IAgent))]
[Cacheable]
internal record AgentEntity : Entity, IArchivableEntity
{
    /// <summary>
    /// Gets or sets the name.
    /// </summary>
    public required string Name { get; init; }
    /// <summary>
    /// Gets or sets the project.
    /// </summary>
    public required Guid Project { get; init; }
    /// <summary>
    /// Gets or sets the endpoint.
    /// </summary>
    public required Guid Endpoint { get; init; }
    /// <summary>
    /// Gets or sets the is system agent.
    /// </summary>
    public required bool IsSystemAgent { get; init; }
    /// <summary>
    /// Gets or sets the model parameters.
    /// </summary>
    public required ModelParametersData ModelParameters { get; init; }

    /// <inheritdoc />
    public bool IsArchived { get; init; }

    /// <summary>The id of the version currently in effect for this agent. Agents are persisted
    /// together with their initial version in a single transaction
    /// (<c>AgentRepository.PersistWithInitialVersionAsync</c>), so this is always populated.</summary>
    public required Guid CurrentVersionId { get; init; }
}
