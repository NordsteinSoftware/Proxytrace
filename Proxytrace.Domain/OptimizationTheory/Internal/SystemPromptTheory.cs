using System.ComponentModel.DataAnnotations;
using JetBrains.Annotations;
using Nordstein.Core.Common.Serialization;
using Nordstein.Core.Common.Validation;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.OptimizationProposal;
using Proxytrace.Domain.Proposal;
using Proxytrace.Domain.TestSuite;

namespace Proxytrace.Domain.OptimizationTheory.Internal;

[UsedImplicitly]
internal record SystemPromptTheory : OptimizationTheory, ISystemPromptTheory
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
    /// Initializes a new instance of the <see cref="SystemPromptTheory"/> class.
    /// </summary>
    public SystemPromptTheory(
        IAgent agent,
        ITestSuite suite,
        TheorySource source,
        Priority priority,
        string rationale,
        string proposedSystemMessage,
        IReadOnlyCollection<Guid> evidenceTestRunIds,
        ISerializer serializer,
        IRepository<IOptimizationTheory> repository)
        : base(agent, suite, source, priority, rationale, evidenceTestRunIds,
            OptimizationContentHash.ForSystemPrompt(serializer, agent.Id, proposedSystemMessage), repository)
    {
        ProposedSystemMessage = proposedSystemMessage;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SystemPromptTheory"/> class.
    /// </summary>
    public SystemPromptTheory(
        IAgent agent,
        ITestSuite suite,
        TheoryStatus status,
        TheorySource source,
        Priority priority,
        string rationale,
        string proposedSystemMessage,
        IReadOnlyCollection<Guid> evidenceTestRunIds,
        Guid? resultingProposalId,
        double? baselinePassRate,
        double? projectedPassRate,
        double? pValue,
        Guid? abTestRunId,
        string contentHash,
        IDomainEntityData existing,
        IRepository<IOptimizationTheory> repository)
        : base(agent, suite, status, source, priority, rationale, evidenceTestRunIds,
            resultingProposalId, baselinePassRate, projectedPassRate, pValue, abTestRunId, contentHash, existing, repository)
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

        if (string.IsNullOrWhiteSpace(ProposedSystemMessage))
            yield return Validation.NotNullOrWhiteSpace(ProposedSystemMessage);
    }
}
