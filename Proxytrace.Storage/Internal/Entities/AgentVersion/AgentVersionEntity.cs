using Proxytrace.Domain.AgentVersion;
using Nordstein.Core.AI.Tools;
using Proxytrace.Storage.Internal.Entities.Agent;

namespace Proxytrace.Storage.Internal.Entities.AgentVersion;

[StoredDomainEntity(typeof(IAgentVersion))]
[Cacheable]
internal record AgentVersionEntity : Entity
{
    /// <summary>
    /// Gets or sets the agent id.
    /// </summary>
    public required Guid AgentId { get; init; }

    /// <summary>
    /// Denormalized from <see cref="AgentEntity.Project"/> so similarity queries stay single-table.
    /// </summary>
    public required Guid Project { get; init; }

    /// <summary>
    /// Gets or sets the version number.
    /// </summary>
    public required int VersionNumber { get; init; }

    /// <summary>
    /// Gets or sets the system prompt.
    /// </summary>
    public required SystemPromptData SystemPrompt { get; init; }

    /// <summary>
    /// Gets or sets the tools.
    /// </summary>
    public required IReadOnlyList<ToolSpecification> Tools { get; init; }

    /// <summary>
    /// SHA-256 of system prompt + sorted tools including descriptions.
    /// </summary>
    public required string Fingerprint { get; init; }

    /// <summary>
    /// SHA-256 of system prompt + sorted tools with descriptions stripped.
    /// </summary>
    public required string LooseFingerprint { get; init; }
}
