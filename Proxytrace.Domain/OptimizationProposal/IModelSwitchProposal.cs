using Proxytrace.Domain.Agent;
using Proxytrace.Domain.ModelEndpoint;
using Proxytrace.Domain.Proposal;
using Proxytrace.Domain.TestRun;

namespace Proxytrace.Domain.OptimizationProposal;

/// <summary>
/// Proposal to switch the agent's <see cref="IAgent.Endpoint"/> to a different model endpoint.
/// </summary>
public interface IModelSwitchProposal : IOptimizationProposal
{
    /// <summary>The endpoint proposed as a replacement for the agent's current one.</summary>
    IModelEndpoint ProposedEndpoint { get; }

    /// <summary>Observed cost-per-call delta (proposed - current) from the evidence runs.</summary>
    decimal? ExpectedCostDelta { get; }

    /// <summary>Observed latency delta (proposed - current) from the evidence runs.</summary>
    TimeSpan? ExpectedLatencyDelta { get; }

    /// <summary>
    /// Factory delegate for creating a new new instance.
    /// </summary>
    public delegate IModelSwitchProposal CreateNew(
        IAgent agent,
        Priority priority,
        string rationale,
        IModelEndpoint proposedEndpoint,
        double? currentPassRate,
        double? proposedPassRate,
        decimal? expectedCostDelta,
        TimeSpan? expectedLatencyDelta,
        IReadOnlyCollection<Guid> evidenceTestRunIds,
        ITestRun abTestRun);

    /// <summary>
    /// Factory delegate for creating a new existing instance.
    /// </summary>
    public delegate IModelSwitchProposal CreateExisting(
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
        IDomainEntityData existing);
}
