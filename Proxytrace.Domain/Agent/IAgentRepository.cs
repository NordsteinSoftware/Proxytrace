using Proxytrace.Domain.AgentVersion;
using Nordstein.Core.AI.Completions;
using Proxytrace.Domain.ModelEndpoint;
using Proxytrace.Domain.Project;
using Nordstein.Core.AI.Prompts;
using Proxytrace.Domain.Prompt;
using Nordstein.Core.AI.Tools;

namespace Proxytrace.Domain.Agent;

/// <summary>
/// Repository for <see cref="IAgent"/> entities with agent-specific lookup operations.
/// </summary>
public interface IAgentRepository : IArchivableRepository<IAgent>
{
    /// <summary>
    /// Returns the agent whose current version exactly matches the given system message + tools
    /// (strict fingerprint), creating a new agent + v1 if none exists. Callers that have already
    /// performed the strict-fingerprint lookup can pass <paramref name="skipStrictPreCheck"/> to
    /// avoid a redundant query — the lock + post-write race check still protect against concurrent
    /// inserts.
    /// </summary>
    Task<IAgent> GetOrCreateAsync(
        IPromptTemplate systemPrompt,
        IReadOnlyList<ToolSpecification> tools,
        IProject project,
        IModelEndpoint endpoint,
        string? name = null,
        bool isSystemAgent = false,
        IModelParameters? modelParameters = null,
        bool skipStrictPreCheck = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new agent together with its initial <see cref="IAgentVersion"/> in a single
    /// transactional unit. Returns the persisted agent with <see cref="IAgent.CurrentVersion"/> set.
    /// </summary>
    Task<IAgent> CreateWithInitialVersionAsync(
        string name,
        IPromptTemplate systemPrompt,
        IReadOnlyList<ToolSpecification> tools,
        IProject project,
        IModelEndpoint endpoint,
        IModelParameters modelParameters,
        bool isSystemAgent,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Strict fingerprint of an agent based on its current version's prompt + tools.
    /// </summary>
    string GetAgentFingerprint(IPromptTemplate systemPrompt, IReadOnlyCollection<ToolSpecification> tools);

    string GetAgentFingerprint(IAgent agent);

    /// <summary>Atomically point <paramref name="agentId"/> at a different
    /// <see cref="IAgentVersion"/>. Callers must ensure the version belongs to the agent.</summary>
    Task SetCurrentVersionAsync(Guid agentId, Guid versionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Counts user-facing agents only, excluding system agents (e.g. optimizers, agentic
    /// evaluators). Used to enforce the licensed agent limit.
    /// </summary>
    Task<int> CountNonSystemAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the agent with the given <paramref name="name"/> in <paramref name="project"/>
    /// (system or not), or null if none exists. Used for explicit name-based call attribution.
    /// </summary>
    Task<IAgent?> FindByNameAsync(IProject project, string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all agents belonging to the project identified by <paramref name="projectId"/>.
    /// </summary>
    Task<IReadOnlyList<IAgent>> GetByProjectAsync(Guid projectId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the id of the project the agent belongs to, or <see langword="null"/> if the agent does
    /// not exist. A cheap FK projection (no full agent load) used to attribute audit events to a project.
    /// </summary>
    Task<Guid?> GetProjectIdAsync(Guid agentId, CancellationToken cancellationToken = default);
}
