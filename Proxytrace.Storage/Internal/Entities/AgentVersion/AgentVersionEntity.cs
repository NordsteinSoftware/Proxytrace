using Proxytrace.Domain.AgentVersion;
using Nordstein.Core.AI.Tools;
using Proxytrace.Storage.Internal.Entities.Agent;

namespace Proxytrace.Storage.Internal.Entities.AgentVersion;

[StoredDomainEntity(typeof(IAgentVersion))]
[Cacheable]
internal record AgentVersionEntity : Entity
{
    /// <summary>
    /// FK to the parent agent that owns this version. Maps to the agent_versions.agent_id column.
    /// </summary>
    public required Guid AgentId { get; init; }

    /// <summary>
    /// Denormalized from <see cref="AgentEntity.Project"/> so similarity queries stay single-table.
    /// </summary>
    public required Guid Project { get; init; }

    /// <summary>
    /// Monotonically increasing integer within the owning agent, starting at 1 for the initial version.
    /// Maps to the agent_versions.version_number column.
    /// </summary>
    public required int VersionNumber { get; init; }

    /// <summary>
    /// JSON-serialized system prompt (name and template text) active in this version.
    /// Maps to the agent_versions.system_prompt column.
    /// </summary>
    public required SystemPromptData SystemPrompt { get; init; }

    /// <summary>
    /// JSON-serialized list of tool specifications available to the agent in this version.
    /// Maps to the agent_versions.tools column.
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
