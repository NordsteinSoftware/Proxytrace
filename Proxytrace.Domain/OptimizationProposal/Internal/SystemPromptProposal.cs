using System.ComponentModel.DataAnnotations;
using JetBrains.Annotations;
using Nordstein.Core.Common.Serialization;
using Nordstein.Core.Common.Validation;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.Proposal;
using Proxytrace.Domain.TestRun;

namespace Proxytrace.Domain.OptimizationProposal.Internal;

[UsedImplicitly]
internal record SystemPromptProposal : OptimizationProposal, ISystemPromptProposal
{
    /// <summary>
    /// Gets the kind.
    /// </summary>
    public override ProposalKind Kind => ProposalKind.SystemPrompt;
    /// <summary>
    /// Gets or sets the proposed system message.
    /// </summary>
    public string ProposedSystemMessage { get; private init; }

    /// <summary>
    /// Initializes a new instance of the <see cref="SystemPromptProposal"/> class.
    /// </summary>
    public SystemPromptProposal(
        IAgent agent,
        Priority priority,
        string rationale,
        string proposedSystemMessage,
        double? currentPassRate,
        double? proposedPassRate,
        IReadOnlyCollection<Guid> evidenceTestRunIds,
        ITestRun abTestRun,
        ISerializer serializer,
        IRepository<IOptimizationProposal> repository)
        : base(agent, priority, rationale, currentPassRate, proposedPassRate, evidenceTestRunIds, abTestRun,
            OptimizationContentHash.ForSystemPrompt(serializer, agent.Id, proposedSystemMessage), repository)
    {
        ProposedSystemMessage = proposedSystemMessage;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SystemPromptProposal"/> class.
    /// </summary>
    public SystemPromptProposal(
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
        IDomainEntityData existing,
        IRepository<IOptimizationProposal> repository)
        : base(agent, status, priority, rationale, currentPassRate, proposedPassRate, evidenceTestRunIds, abTestRun,
            contentHash, adoptedAt, adoptedAgentVersionId, adoptedAgentVersionNumber, adoptedManually, existing, repository)
    {
        ProposedSystemMessage = proposedSystemMessage;
    }

    /// <summary>
    /// Validates.
    /// </summary>
    public override IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var result in base.Validate(validationContext))
            yield return result;

        yield return Validation.NotNullOrWhiteSpace(ProposedSystemMessage);
    }
}
