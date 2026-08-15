using System.ComponentModel.DataAnnotations;
using JetBrains.Annotations;
using Nordstein.Core.Common.Serialization;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.Proposal;
using Proxytrace.Domain.TestRun;
using Nordstein.Core.AI.Tools;

namespace Proxytrace.Domain.OptimizationProposal.Internal;

[UsedImplicitly]
internal record ToolUpdateProposal : OptimizationProposal, IToolUpdateProposal
{
    /// <summary>
    /// Gets the kind.
    /// </summary>
    public override ProposalKind Kind => ProposalKind.Tool;
    /// <summary>
    /// Gets or sets the proposed tools.
    /// </summary>
    public IReadOnlyList<ToolSpecification> ProposedTools { get; private init; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ToolUpdateProposal"/> class.
    /// </summary>
    public ToolUpdateProposal(
        IAgent agent,
        Priority priority,
        string rationale,
        IReadOnlyList<ToolSpecification> proposedTools,
        double? currentPassRate,
        double? proposedPassRate,
        IReadOnlyCollection<Guid> evidenceTestRunIds,
        ITestRun abTestRun,
        ISerializer serializer,
        IRepository<IOptimizationProposal> repository)
        : base(agent, priority, rationale, currentPassRate, proposedPassRate, evidenceTestRunIds, abTestRun,
            OptimizationContentHash.ForTools(serializer, agent.Id, proposedTools), repository)
    {
        ProposedTools = proposedTools.ToArray();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ToolUpdateProposal"/> class.
    /// </summary>
    public ToolUpdateProposal(
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
        IDomainEntityData existing,
        IRepository<IOptimizationProposal> repository)
        : base(agent, status, priority, rationale, currentPassRate, proposedPassRate, evidenceTestRunIds, abTestRun,
            contentHash, adoptedAt, adoptedAgentVersionId, adoptedAgentVersionNumber, adoptedManually, existing, repository)
    {
        ProposedTools = proposedTools.ToArray();
    }

    /// <summary>
    /// Validates.
    /// </summary>
    public override IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var result in base.Validate(validationContext))
            yield return result;

        foreach (var result in ProposedTools.SelectMany(tool => tool.Validate(validationContext)))
            yield return result;
    }
}
