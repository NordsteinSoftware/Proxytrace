using Proxytrace.Domain.Agent;
using Proxytrace.Domain.Project;
using Nordstein.Core.AI.Prompts;
using Proxytrace.Domain.Prompt;
using Nordstein.Core.AI.Tools;

namespace Proxytrace.Domain.AgentVersion;

/// <summary>
/// Repository for <see cref="IAgentVersion"/> with version-specific queries.
/// </summary>
public interface IAgentVersionRepository : IRepository<IAgentVersion>
{
    /// <summary>
    /// Returns the version in the given project whose strict fingerprint matches the given
    /// system prompt + tools (exact match including tool descriptions). Null if none.
    /// </summary>
    Task<IAgentVersion?> FindByStrictFingerprintAsync(
        IProject project,
        IPromptTemplate systemPrompt,
        IReadOnlyList<ToolSpecification> tools,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all versions in the given project sharing a loose fingerprint with the supplied
    /// system prompt + tools (tool descriptions stripped). Used as a shortlist for the
    /// similarity match.
    /// </summary>
    Task<IReadOnlyList<IAgentVersion>> GetByLooseFingerprintAsync(
        IProject project,
        IPromptTemplate systemPrompt,
        IReadOnlyList<ToolSpecification> tools,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all versions belonging to <paramref name="agent"/> ordered by VersionNumber asc.
    /// </summary>
    Task<IReadOnlyList<IAgentVersion>> GetByAgentAsync(
        IAgent agent,
        CancellationToken cancellationToken = default);

    /// <summary>Strict fingerprint = SHA-256(system prompt + sorted tools incl. descriptions).</summary>
    string GetStrictFingerprint(IPromptTemplate systemPrompt, IReadOnlyCollection<ToolSpecification> tools);
}
