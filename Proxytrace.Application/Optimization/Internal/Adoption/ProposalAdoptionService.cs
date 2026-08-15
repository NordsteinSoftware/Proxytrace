using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Proxytrace.Application.Streaming;
using Proxytrace.Domain;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.AgentVersion;
using Proxytrace.Domain.AuditLog;
using Nordstein.Core.Domain.Events;
using Proxytrace.Domain.OptimizationProposal;

namespace Proxytrace.Application.Optimization.Internal.Adoption;

/// <summary>
/// Closes the optimization loop after a proposal is promoted. Proxytrace cannot apply a change
/// to the client's agent code — it can only observe ingested traffic. This service watches
/// entity events for the moment the promoted change shows up live and flips the proposal to
/// <see cref="ProposalStatus.Adopted"/>:
/// <list type="bullet">
/// <item>new <see cref="IAgentVersion"/> → exact prompt/tool match against Accepted proposals,</item>
/// <item>agent update → endpoint match against Accepted ModelSwitch proposals,</item>
/// <item>proposal promote → immediate check against the agent's current state,</item>
/// <item>startup sweep over all Accepted proposals (heals events missed while down).</item>
/// </list>
/// Listening on entity events (not the per-call ingestion path) keeps the hot path untouched:
/// prompt/tool adoptions necessarily mint a new version row, and model switches necessarily
/// update the agent's endpoint, so the events carry exactly the state changes that matter.
/// Detection gaps (revert to an already-stored old version, traffic attributed to a different
/// agent, tweaked adoptions) are covered by the manual "Mark adopted" action.
/// </summary>
internal sealed class ProposalAdoptionService : BackgroundService
{
    private readonly IEntityEventService entityEvents;
    private readonly IOptimizationProposalRepository proposals;
    private readonly IRepository<IAgentVersion> versions;
    private readonly ProposalAdoptionMatcher matcher;
    private readonly IProposalBroadcaster proposalBroadcaster;
    private readonly ILogger<ProposalAdoptionService> logger;
    private readonly ILogger<Audit> audit;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProposalAdoptionService"/> class.
    /// </summary>
    public ProposalAdoptionService(
        IEntityEventService entityEvents,
        IOptimizationProposalRepository proposals,
        IRepository<IAgentVersion> versions,
        ProposalAdoptionMatcher matcher,
        IProposalBroadcaster proposalBroadcaster,
        ILogger<ProposalAdoptionService> logger,
        ILogger<Audit> audit)
    {
        this.entityEvents = entityEvents;
        this.proposals = proposals;
        this.versions = versions;
        this.matcher = matcher;
        this.proposalBroadcaster = proposalBroadcaster;
        this.logger = logger;
        this.audit = audit;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Subscribe before sweeping so events raised during the sweep queue up instead of getting lost.
        ChannelReader<EntityChangedEvent> reader = entityEvents.Subscribe(stoppingToken);

        await SweepAcceptedAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!await reader.WaitToReadAsync(stoppingToken))
                {
                    return;
                }
                while (reader.TryRead(out EntityChangedEvent? evt))
                {
                    await DispatchAsync(evt, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // Never let a transient failure kill the subscription loop; the next event (or a
                // restart sweep) heals whatever this one missed.
                logger.LogError(ex, "Proposal-adoption loop error; continuing.");
            }
        }
    }

    private async Task DispatchAsync(EntityChangedEvent evt, CancellationToken cancellationToken)
    {
        try
        {
            if (evt.EntityType == typeof(IAgentVersion) && evt.ChangeType == EntityChangeType.Added)
                await OnVersionAddedAsync(evt.EntityId, cancellationToken);
            else if (evt.EntityType == typeof(IAgent) && evt.ChangeType == EntityChangeType.Updated)
                await OnAgentUpdatedAsync(evt.EntityId, cancellationToken);
            else if (evt.EntityType == typeof(IOptimizationProposal) && evt.ChangeType is EntityChangeType.Added or EntityChangeType.Updated)
                await OnProposalChangedAsync(evt.EntityId, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Adoption check failed for {EntityType} {EntityId}",
                evt.EntityType.Name, evt.EntityId);
        }
    }

    /// <summary>
    /// A new version means the agent's prompt or tools changed in live traffic — the only way a
    /// promoted prompt/tool proposal can become adopted.
    /// </summary>
    private async Task OnVersionAddedAsync(Guid versionId, CancellationToken cancellationToken)
    {
        var version = await versions.FindAsync(versionId, cancellationToken);
        if (version is null)
            return;

        var pending = await proposals.GetByAgentAndStatusAsync(version.AgentId, ProposalStatus.Accepted, cancellationToken);
        foreach (var proposal in pending)
        {
            if (matcher.MatchesVersion(proposal, version))
                await AdoptAsync(proposal, version, cancellationToken);
        }
    }

    /// <summary>
    /// Agent updates include endpoint changes detected by ingestion — the signal a promoted
    /// ModelSwitch proposal waits for. The proposal's agent reference is mapped fresh on load,
    /// so it already reflects the updated endpoint.
    /// </summary>
    private async Task OnAgentUpdatedAsync(Guid agentId, CancellationToken cancellationToken)
    {
        var pending = await proposals.GetByAgentAndStatusAsync(agentId, ProposalStatus.Accepted, cancellationToken);
        foreach (var proposal in pending)
        {
            if (matcher.MatchesEndpoint(proposal, proposal.Agent))
                await AdoptAsync(proposal, null, cancellationToken);
        }
    }

    /// <summary>
    /// On promote (and on any proposal write), check the agent's current state right away —
    /// the user may have applied the change before promoting. Adopted results fail the
    /// Accepted guard, so the Updated event this produces cannot loop.
    /// </summary>
    private async Task OnProposalChangedAsync(Guid proposalId, CancellationToken cancellationToken)
    {
        var proposal = await proposals.FindAsync(proposalId, cancellationToken);
        if (proposal is not { Status: ProposalStatus.Accepted })
            return;

        await CheckAgentStateAsync(proposal, cancellationToken);
    }

    private async Task SweepAcceptedAsync(CancellationToken cancellationToken)
    {
        try
        {
            var pending = await proposals.GetByStatusAsync(ProposalStatus.Accepted, cancellationToken);
            foreach (var proposal in pending)
            {
                await CheckAgentStateAsync(proposal, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Proposal-adoption startup sweep failed; live events still apply.");
        }
    }

    private async Task CheckAgentStateAsync(IOptimizationProposal proposal, CancellationToken cancellationToken)
    {
        if (!matcher.MatchesAgentState(proposal, proposal.Agent))
            return;

        var adoptedVersion = proposal is IModelSwitchProposal ? null : proposal.Agent.CurrentVersion;
        await AdoptAsync(proposal, adoptedVersion, cancellationToken);
    }

    private async Task AdoptAsync(
        IOptimizationProposal proposal,
        IAgentVersion? adoptedVersion,
        CancellationToken cancellationToken)
    {
        var adopted = await proposal.MarkAdopted(adoptedVersion, manual: false, cancellationToken);
        proposalBroadcaster.Publish(ProposalStatusChangedEvent.Create(adopted));
        audit.LogAudit(
            AuditAction.ProposalAutoAdopted, nameof(IOptimizationProposal), adopted.Id, adopted.Agent.Name,
            projectId: adopted.Agent.Project.Id,
            details: JsonSerializer.Serialize(new
            {
                kind = adopted.Kind.ToString(),
                adoptedVersion = adoptedVersion?.VersionNumber,
            }));
        logger.LogInformation(
            "Proposal {ProposalId} ({Kind}) adopted by agent {AgentId}{VersionSuffix}",
            adopted.Id, adopted.Kind, adopted.Agent.Id,
            adoptedVersion is null ? "" : $" in version v{adoptedVersion.VersionNumber}");
    }
}
