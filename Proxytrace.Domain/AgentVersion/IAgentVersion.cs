using Proxytrace.Domain.Agent;
using Nordstein.Core.AI.Prompts;
using Proxytrace.Domain.Prompt;
using Nordstein.Core.AI.Tools;

namespace Proxytrace.Domain.AgentVersion;

/// <summary>
/// A snapshot of an <see cref="IAgent"/>'s system prompt and tools at a point in time.
/// New versions are created automatically by the ingestion pipeline when the prompt or tool-set changes.
/// </summary>
public interface IAgentVersion : IDomainEntity<IAgentVersion>
{
    /// <summary>The id of the parent agent this version belongs to.</summary>
    Guid AgentId { get; }

    /// <summary>The project this version belongs to. Denormalized from the parent agent at creation
    /// (a version never changes project even when re-parented).</summary>
    Guid ProjectId { get; }

    /// <summary>Monotonically increasing version number, unique per <see cref="AgentId"/>.</summary>
    int VersionNumber { get; }

    /// <summary>The system message that defines this version's behaviour.</summary>
    IPromptTemplate SystemPrompt { get; }

    /// <summary>The tools available to this version.</summary>
    IReadOnlyList<ToolSpecification> Tools { get; }

    /// <summary>Resolves the parent <see cref="IAgent"/> from the repository.</summary>
    Task<IAgent> GetAgentAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-parents this version to <paramref name="targetAgent"/>, renumbering it to
    /// max(targetAgent.VersionNumbers)+1. The version keeps its <see cref="IDomainEntityData.Id"/> so
    /// referencing <c>AgentCall</c> rows continue to point at it. Returns the relocated version.
    /// </summary>
    Task<IAgentVersion> MoveToAgentAsync(IAgent targetAgent, CancellationToken cancellationToken = default);

    public delegate IAgentVersion CreateNew(
        Guid projectId,
        Guid agentId,
        int versionNumber,
        IPromptTemplate systemPrompt,
        IReadOnlyList<ToolSpecification> tools);

    public delegate IAgentVersion CreateExisting(
        Guid projectId,
        Guid agentId,
        int versionNumber,
        IPromptTemplate systemPrompt,
        IReadOnlyList<ToolSpecification> tools,
        IDomainEntityData existing);
}
