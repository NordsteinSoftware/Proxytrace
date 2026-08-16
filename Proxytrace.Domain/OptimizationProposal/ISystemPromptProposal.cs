using Proxytrace.Domain.Agent;
using Proxytrace.Domain.Proposal;
using Proxytrace.Domain.TestRun;

namespace Proxytrace.Domain.OptimizationProposal;

/// <summary>
/// Proposal to change the agent's system prompt.
/// </summary>
public interface ISystemPromptProposal : IOptimizationProposal
{
    /// <summary>The full proposed system prompt text.</summary>
    string ProposedSystemMessage { get; }

    /// <summary>
    /// Factory delegate for creating a new new instance.
    /// </summary>
    public delegate ISystemPromptProposal CreateNew(
        IAgent agent,
        Priority priority,
        string rationale,
        string proposedSystemMessage,
        double? currentPassRate,
        double? proposedPassRate,
        IReadOnlyCollection<Guid> evidenceTestRunIds,
        ITestRun abTestRun);

    /// <summary>
    /// Factory delegate for creating a new existing instance.
    /// </summary>
    public delegate ISystemPromptProposal CreateExisting(
        IAgent agent,
        ProposalStatus status,
        Priority priority,
        string rationale,
        string proposedSystemMessage,
        double? currentPassRate,
        double? proposedPassRate,
        IReadOnlyCollection<Guid> evidenceTestRunIds,
        ITestRun abTestRun,
        string contentHash,
        DateTimeOffset? adoptedAt,
        Guid? adoptedAgentVersionId,
        int? adoptedAgentVersionNumber,
        bool? adoptedManually,
        IDomainEntityData existing);
}
