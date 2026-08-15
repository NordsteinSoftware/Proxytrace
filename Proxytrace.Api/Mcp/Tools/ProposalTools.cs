using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using Proxytrace.Api.Dto.Proposals;
using Proxytrace.Application.Streaming;
using Proxytrace.Domain.AuditLog;
using Proxytrace.Domain.OptimizationProposal;
using Proxytrace.Licensing;

namespace Proxytrace.Api.Mcp.Tools;

/// <summary>
/// MCP tools for reviewing and acting on optimization proposals in the current project. Gated by the
/// <see cref="LicenseFeature.OptimizationProposals"/> license feature, mirroring the REST controller.
/// </summary>
[McpServerToolType]
internal sealed class ProposalTools
{
    private readonly IMcpProjectAccessor project;
    private readonly IOptimizationProposalRepository repository;
    private readonly OptimizationProposalDtoMapper mapper;
    private readonly IProposalBroadcaster broadcaster;
    private readonly ILicenseService license;
    private readonly ILogger<Audit> audit;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProposalTools"/> class.
    /// </summary>
    public ProposalTools(
        IMcpProjectAccessor project,
        IOptimizationProposalRepository repository,
        OptimizationProposalDtoMapper mapper,
        IProposalBroadcaster broadcaster,
        ILicenseService license,
        ILogger<Audit> audit)
    {
        this.project = project;
        this.repository = repository;
        this.mapper = mapper;
        this.broadcaster = broadcaster;
        this.license = license;
        this.audit = audit;
    }

    [McpServerTool(Name = "list_proposals")]
    [Description("List the optimization proposals in the current project (most recent first), with their " +
                 "kind, status and expected pass-rate delta.")]
    /// <summary>
    /// Lists the proposals.
    /// </summary>
    public async Task<IReadOnlyList<OptimizationProposalDto>> ListProposals(CancellationToken cancellationToken)
    {
        EnsureFeature();
        var p = await project.GetProjectAsync(cancellationToken);
        var proposals = await repository.GetByProjectAsync(p.Id, cancellationToken);
        return proposals.Select(mapper.ToDto).ToArray();
    }

    /// <summary>
    /// Gets the proposal.
    /// </summary>
    [McpServerTool(Name = "get_proposal")]
    [Description("Get a single optimization proposal by id. It must belong to the current project.")]
    public async Task<OptimizationProposalDto> GetProposal(
        [Description("The proposal id (GUID), from list_proposals.")] Guid proposalId,
        CancellationToken cancellationToken)
    {
        var proposal = await RequireProposalAsync(proposalId, cancellationToken);
        return mapper.ToDto(proposal);
    }

    [McpServerTool(Name = "get_proposal_artifact")]
    [Description("Get the machine-readable handoff package for applying a proposal's change to the agent's " +
                 "actual implementation. The proposal must belong to the current project.")]
    /// <summary>
    /// Gets the proposal artifact.
    /// </summary>
    public async Task<ProposalArtifactDto> GetProposalArtifact(
        [Description("The proposal id (GUID), from list_proposals.")] Guid proposalId,
        CancellationToken cancellationToken)
    {
        var proposal = await RequireProposalAsync(proposalId, cancellationToken);
        return mapper.ToArtifactDto(proposal);
    }

    [McpServerTool(Name = "set_proposal_status")]
    [Description("Approve, reject or mark-adopted an optimization proposal. Valid transitions: Draft->Accepted, " +
                 "Draft->Rejected, Accepted->Adopted. The proposal must belong to the current project.")]
    /// <summary>
    /// Sets the proposal status.
    /// </summary>
    public async Task<OptimizationProposalDto> SetProposalStatus(
        [Description("The proposal id (GUID), from list_proposals.")] Guid proposalId,
        [Description("The target status: Accepted, Rejected or Adopted.")] ProposalStatus status,
        CancellationToken cancellationToken)
    {
        project.RequireWriteScope();
        var existing = await RequireProposalAsync(proposalId, cancellationToken);

        IOptimizationProposal updated;
        switch (status)
        {
            case ProposalStatus.Accepted when existing.Status == ProposalStatus.Draft:
                updated = await existing.Accept(cancellationToken);
                break;
            case ProposalStatus.Rejected when existing.Status == ProposalStatus.Draft:
                updated = await existing.Reject(cancellationToken);
                break;
            case ProposalStatus.Adopted when existing.Status == ProposalStatus.Accepted:
                updated = await existing.MarkAdopted(null, manual: true, cancellationToken);
                break;
            default:
                throw new McpException($"Cannot change proposal status from {existing.Status} to {status}.");
        }

        broadcaster.Publish(ProposalStatusChangedEvent.Create(updated));
        audit.LogAudit(
            AuditAction.ProposalStatusChanged, nameof(IOptimizationProposal), updated.Id, updated.Agent.Name,
            projectId: updated.Agent.Project.Id,
            details: JsonSerializer.Serialize(new { from = existing.Status.ToString(), to = updated.Status.ToString() }));
        return mapper.ToDto(updated);
    }

    private async Task<IOptimizationProposal> RequireProposalAsync(Guid proposalId, CancellationToken cancellationToken)
    {
        EnsureFeature();
        var p = await project.GetProjectAsync(cancellationToken);
        var proposal = await repository.FindAsync(proposalId, cancellationToken);
        if (proposal is null || proposal.Agent.Project.Id != p.Id)
            throw new McpException($"Proposal '{proposalId}' was not found in this project.");
        return proposal;
    }

    private void EnsureFeature()
    {
        if (!license.IsFeatureEnabled(LicenseFeature.OptimizationProposals))
            throw new McpException("Optimization proposals are not available on the current license tier.");
    }
}
