using Proxytrace.Domain.Agent;
using Proxytrace.Domain.Proposal;
using Proxytrace.Domain.TestRun;
using Nordstein.Core.AI.Tools;

namespace Proxytrace.Domain.OptimizationProposal;

/// <summary>
/// Proposal to update the agent's tool definitions.
/// </summary>
public interface IToolUpdateProposal : IOptimizationProposal
{
    /// <summary>The proposed tool specifications, replacing the agent's current ones.</summary>
    IReadOnlyList<ToolSpecification> ProposedTools { get; }

    public delegate IToolUpdateProposal CreateNew(
        IAgent agent,
        Priority priority,
        string rationale,
        IReadOnlyList<ToolSpecification> proposedTools,
        double? currentPassRate,
        double? proposedPassRate,
        IReadOnlyCollection<Guid> evidenceTestRunIds,
        ITestRun abTestRun);

    public delegate IToolUpdateProposal CreateExisting(
        IAgent agent,
        ProposalStatus status,
        Priority priority,
        string rationale,
        IReadOnlyList<ToolSpecification> proposedTools,
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
