using System.ComponentModel.DataAnnotations;
using JetBrains.Annotations;
using Nordstein.Core.Common.Serialization;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.ModelEndpoint;
using Proxytrace.Domain.Proposal;
using Proxytrace.Domain.TestRun;

namespace Proxytrace.Domain.OptimizationProposal.Internal;

[UsedImplicitly]
internal record ModelSwitchProposal : OptimizationProposal, IModelSwitchProposal
{
    /// <summary>
    /// Gets the kind.
    /// </summary>
    public override ProposalKind Kind => ProposalKind.ModelSwitch;
    /// <summary>
    /// Gets or sets the proposed endpoint.
    /// </summary>
    public IModelEndpoint ProposedEndpoint { get; private init; }
    /// <summary>
    /// Gets or sets the expected cost delta.
    /// </summary>
    public decimal? ExpectedCostDelta { get; private init; }
    /// <summary>
    /// Gets or sets the expected latency delta.
    /// </summary>
    public TimeSpan? ExpectedLatencyDelta { get; private init; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ModelSwitchProposal"/> class.
    /// </summary>
    public ModelSwitchProposal(
        IAgent agent,
        Priority priority,
        string rationale,
        IModelEndpoint proposedEndpoint,
        double? currentPassRate,
        double? proposedPassRate,
        decimal? expectedCostDelta,
        TimeSpan? expectedLatencyDelta,
        IReadOnlyCollection<Guid> evidenceTestRunIds,
        ITestRun abTestRun,
        ISerializer serializer,
        IRepository<IOptimizationProposal> repository)
        : base(agent, priority, rationale, currentPassRate, proposedPassRate, evidenceTestRunIds, abTestRun,
            OptimizationContentHash.ForModelSwitch(serializer, agent.Id, proposedEndpoint.Id), repository)
    {
        ProposedEndpoint = proposedEndpoint;
        ExpectedCostDelta = expectedCostDelta;
        ExpectedLatencyDelta = expectedLatencyDelta;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ModelSwitchProposal"/> class.
    /// </summary>
    public ModelSwitchProposal(
        IAgent agent,
        ProposalStatus status,
        Priority priority,
        string rationale,
        IModelEndpoint proposedEndpoint,
        double? currentPassRate,
        double? proposedPassRate,
        decimal? expectedCostDelta,
        TimeSpan? expectedLatencyDelta,
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
        ProposedEndpoint = proposedEndpoint;
        ExpectedCostDelta = expectedCostDelta;
        ExpectedLatencyDelta = expectedLatencyDelta;
    }

    /// <summary>
    /// Validates.
    /// </summary>
    public override IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var result in base.Validate(validationContext))
            yield return result;

        foreach (var result in ProposedEndpoint.Validate(validationContext))
            yield return result;
    }
}
