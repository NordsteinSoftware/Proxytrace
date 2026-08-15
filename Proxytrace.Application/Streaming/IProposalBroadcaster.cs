using System.Threading.Channels;
using Proxytrace.Domain.OptimizationProposal;
using Proxytrace.Domain.Proposal;

namespace Proxytrace.Application.Streaming;

/// <summary>
/// Base type for events on the per-agent proposal stream.
/// </summary>
public abstract record ProposalEvent(Guid Id, Guid AgentId);

/// <summary>
/// Event raised when a proposal created occurs.
/// </summary>
public record ProposalCreatedEvent(
    Guid Id,
    Guid AgentId,
    ProposalKind Kind,
    Priority Priority,
    string Rationale,
    DateTimeOffset CreatedAt) : ProposalEvent(Id, AgentId)
{
    /// <summary>
    /// Creates.
    /// </summary>
    public static ProposalCreatedEvent Create(IOptimizationProposal proposal)
        => new(
            proposal.Id,
            proposal.Agent.Id,
            proposal.Kind,
            proposal.Priority,
            proposal.Rationale,
            proposal.CreatedAt);
}

/// <summary>
/// Emitted whenever a proposal's review/adoption status changes — promote, dismiss,
/// manual mark-adopted, and auto-detected adoption from ingested traffic.
/// </summary>
public record ProposalStatusChangedEvent(
    Guid Id,
    Guid AgentId,
    ProposalKind Kind,
    ProposalStatus Status,
    DateTimeOffset? AdoptedAt,
    Guid? AdoptedAgentVersionId,
    int? AdoptedAgentVersionNumber,
    bool? AdoptedManually,
    DateTimeOffset UpdatedAt) : ProposalEvent(Id, AgentId)
{
    /// <summary>
    /// Creates.
    /// </summary>
    public static ProposalStatusChangedEvent Create(IOptimizationProposal proposal)
        => new(
            proposal.Id,
            proposal.Agent.Id,
            proposal.Kind,
            proposal.Status,
            proposal.AdoptedAt,
            proposal.AdoptedAgentVersionId,
            proposal.AdoptedAgentVersionNumber,
            proposal.AdoptedManually,
            proposal.UpdatedAt);
}

/// <summary>
/// Broadcasts proposal events.
/// </summary>
public interface IProposalBroadcaster
{
    /// <summary>
    /// Subscribes to proposal events for a specific agent.
    /// The returned reader is closed when <paramref name="cancellationToken"/> is cancelled.
    /// </summary>
    ChannelReader<ProposalEvent> Subscribe(Guid agentId, CancellationToken cancellationToken);

    void Publish(ProposalEvent evt);
}
