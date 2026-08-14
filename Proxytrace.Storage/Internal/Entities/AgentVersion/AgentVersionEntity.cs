using Proxytrace.Domain.AgentVersion;
using Nordstein.Core.AI.Tools;
using Proxytrace.Storage.Internal.Entities.Agent;

namespace Proxytrace.Storage.Internal.Entities.AgentVersion;

[StoredDomainEntity(typeof(IAgentVersion))]
[Cacheable]
internal record AgentVersionEntity : Entity
{
    public required Guid AgentId { get; init; }

    /// <summary>
    /// Denormalized from <see cref="AgentEntity.Project"/> so similarity queries stay single-table.
    /// </summary>
    public required Guid Project { get; init; }

    public required int VersionNumber { get; init; }

    public required SystemPromptData SystemPrompt { get; init; }

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
