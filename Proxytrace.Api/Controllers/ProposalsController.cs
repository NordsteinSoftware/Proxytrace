using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Proxytrace.Api.Auth;
using Proxytrace.Api.Auth.Licensing;
using Proxytrace.Api.Dto.Proposals;
using Proxytrace.Application.Streaming;
using Proxytrace.Domain;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.AuditLog;
using Proxytrace.Domain.ModelEndpoint;
using Proxytrace.Domain.OptimizationProposal;
using Proxytrace.Domain.TestRun;
using Proxytrace.Domain.TestRunGroup;
using Proxytrace.Domain.TestSuite;
using Proxytrace.Domain.Tools;
using Proxytrace.Licensing;

namespace Proxytrace.Api.Controllers;

[ApiController]
[Authorize]
[RequiresFeature(LicenseFeature.OptimizationProposals)]
[Route("api/proposals")]
public class ProposalsController : ControllerBase
{
    private readonly IOptimizationProposalRepository repository;
    private readonly IModelSwitchProposal.CreateNew createModelSwitchNew;
    private readonly ISystemPromptProposal.CreateNew createSystemPromptNew;
    private readonly IToolUpdateProposal.CreateNew createToolUpdateNew;
    private readonly IAgentRepository agents;
    private readonly IRepository<IModelEndpoint> endpoints;
    private readonly IRepository<ITestSuite> suites;
    private readonly IRepository<ITestRunGroup> groups;
    private readonly IRepository<ITestRun> testRuns;
    private readonly ITestSuite.CreateNew createSuite;
    private readonly ITestRunGroup.CreateNew createGroup;
    private readonly ITestRun.CreateNew createRun;
    private readonly OptimizationProposalDtoMapper mapper;
    private readonly IProposalBroadcaster proposalBroadcaster;
    private readonly ILogger<Audit> audit;
    private readonly IProjectAccessGuard accessGuard;

    public ProposalsController(
        IOptimizationProposalRepository repository,
        IModelSwitchProposal.CreateNew createModelSwitchNew,
        ISystemPromptProposal.CreateNew createSystemPromptNew,
        IToolUpdateProposal.CreateNew createToolUpdateNew,
        IAgentRepository agents,
        IRepository<IModelEndpoint> endpoints,
        IRepository<ITestSuite> suites,
        IRepository<ITestRunGroup> groups,
        IRepository<ITestRun> testRuns,
        ITestSuite.CreateNew createSuite,
        ITestRunGroup.CreateNew createGroup,
        ITestRun.CreateNew createRun,
        OptimizationProposalDtoMapper mapper,
        IProposalBroadcaster proposalBroadcaster,
        ILogger<Audit> audit,
        IProjectAccessGuard accessGuard)
    {
        this.repository = repository;
        this.createModelSwitchNew = createModelSwitchNew;
        this.createSystemPromptNew = createSystemPromptNew;
        this.createToolUpdateNew = createToolUpdateNew;
        this.agents = agents;
        this.endpoints = endpoints;
        this.suites = suites;
        this.groups = groups;
        this.testRuns = testRuns;
        this.createSuite = createSuite;
        this.createGroup = createGroup;
        this.createRun = createRun;
        this.mapper = mapper;
        this.proposalBroadcaster = proposalBroadcaster;
        this.audit = audit;
        this.accessGuard = accessGuard;
    }

    // The projects this list request may read: null for an admin, the caller's own projects when no
    // narrower filter is given (#482), and [] when a named agent/project is out of reach.
    private async Task<IReadOnlyCollection<Guid>?> ListScopeAsync(
        Guid? agentId,
        Guid? projectId,
        CancellationToken cancellationToken)
    {
        var scope = await accessGuard.ResolveListScopeAsync(projectId, cancellationToken);
        if (scope is null || scope.IsEmpty())
            return scope;

        if (agentId is { } aid)
        {
            var agent = await agents.FindAsync(aid, cancellationToken);
            return agent is not null && scope.Contains(agent.Project.Id) ? scope : [];
        }

        return scope;
    }

    [HttpGet]
    public async Task<IReadOnlyList<OptimizationProposalDto>> GetAll(
        [FromQuery] Guid? agentId = null,
        [FromQuery] Guid? projectId = null,
        CancellationToken cancellationToken = default)
    {
        var scope = await ListScopeAsync(agentId, projectId, cancellationToken);
        if (scope.IsEmpty())
            return [];

        IReadOnlyList<IOptimizationProposal> proposals;
        if (agentId.HasValue)
            proposals = await repository.GetByAgentAsync(agentId.Value, cancellationToken);
        else if (scope is null)
            proposals = await repository.GetAllAsync(cancellationToken);
        else
            proposals = await repository.GetByProjectsAsync(scope, cancellationToken);

        return proposals.Select(mapper.ToDto).ToList();
    }

    /// <summary>
    /// Returns a single optimization proposal by id, or 404 if it does not exist.
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<OptimizationProposalDto>> Get(Guid id, CancellationToken cancellationToken)
    {
        var proposal = await repository.FindAsync(id, cancellationToken);
        if (proposal is null)
            return NotFound();

        if (!await accessGuard.CanAccessProjectAsync(proposal.Agent.Project.Id, cancellationToken))
            return NotFound();

        return Ok(mapper.ToDto(proposal));
    }

    /// <summary>
    /// Test-only: seeds an optimization proposal directly, bypassing the optimizer pipeline.
    /// Supports SystemPrompt, ModelSwitch and ToolUpdate proposals. A minimal A/B test run is
    /// created for the agent's endpoint to satisfy the proposal's mandatory evidence reference.
    /// </summary>
    [HttpPost("seed")]
    [TestOnlyEndpoint]
    public async Task<ActionResult<OptimizationProposalDto>> Seed(
        [FromBody] SeedProposalRequest request,
        CancellationToken cancellationToken)
    {
        var agent = await agents.FindAsync(request.AgentId, cancellationToken);
        if (agent is null)
            return NotFound();

        IOptimizationProposal saved;

        switch (request.Details)
        {
            case SystemPromptDetailsDto systemPrompt:
            {
                var abTestRun = await BuildAbTestRunAsync(agent, cancellationToken);
                saved = await repository.AddAsync(
                    createSystemPromptNew(
                        agent,
                        request.Priority,
                        request.Rationale,
                        systemPrompt.ProposedSystemMessage,
                        currentPassRate: null,
                        proposedPassRate: null,
                        evidenceTestRunIds: [],
                        abTestRun: abTestRun),
                    cancellationToken);

                break;
            }

            case ModelSwitchSeedDetailsDto modelSwitch:
            {
                var proposedEndpoint = await endpoints.FindAsync(modelSwitch.ProposedEndpointId, cancellationToken);
                if (proposedEndpoint is null)
                    return BadRequest($"Proposed endpoint {modelSwitch.ProposedEndpointId} does not exist.");

                var abTestRun = await BuildAbTestRunAsync(agent, cancellationToken);
                saved = await repository.AddAsync(
                    createModelSwitchNew(
                        agent,
                        request.Priority,
                        request.Rationale,
                        proposedEndpoint,
                        currentPassRate: null,
                        proposedPassRate: null,
                        expectedCostDelta: null,
                        expectedLatencyDelta: null,
                        evidenceTestRunIds: [],
                        abTestRun: abTestRun),
                    cancellationToken);

                break;
            }

            case ToolUpdateSeedDetailsDto toolUpdate:
            {
                var proposedTools = toolUpdate.ProposedTools
                    .Select(t => new ToolSpecification(
                        t.Name,
                        t.Description,
                        string.IsNullOrWhiteSpace(t.ParametersJson)
                            ? ToolArguments.None
                            : ToolArguments.FromJsonSchema(t.ParametersJson)))
                    .ToList();

                var abTestRun = await BuildAbTestRunAsync(agent, cancellationToken);
                saved = await repository.AddAsync(
                    createToolUpdateNew(
                        agent,
                        request.Priority,
                        request.Rationale,
                        proposedTools,
                        currentPassRate: null,
                        proposedPassRate: null,
                        evidenceTestRunIds: [],
                        abTestRun: abTestRun),
                    cancellationToken);

                break;
            }

            default:
                return BadRequest("Unsupported proposal details kind.");
        }

        // CreateNew always starts in Draft; walk the domain transitions to the requested status.
        saved = request.Status switch
        {
            ProposalStatus.Accepted => await saved.Accept(cancellationToken),
            ProposalStatus.Rejected => await saved.Reject(cancellationToken),
            ProposalStatus.Adopted => await (await saved.Accept(cancellationToken))
                .MarkAdopted(null, manual: true, cancellationToken),
            _ => saved,
        };

        return Ok(mapper.ToDto(saved));
    }

    /// <summary>
    /// Builds the minimal suite + group + A/B test run a seeded proposal must reference, for the
    /// given agent's endpoint.
    /// </summary>
    private async Task<ITestRun> BuildAbTestRunAsync(IAgent agent, CancellationToken cancellationToken)
    {
        var suite = await suites.AddAsync(
            createSuite($"Seeded proposal suite {DateTimeOffset.UtcNow:O}", agent, [], []),
            cancellationToken);
        var group = await groups.AddAsync(createGroup(suite, isSystemRun: true, null, sampleCount: 1), cancellationToken);
        return await testRuns.AddAsync(createRun(group, agent.Endpoint, sampleIndex: 0), cancellationToken);
    }

    [HttpPatch("{id:guid}/status")]
    public async Task<ActionResult<OptimizationProposalDto>> UpdateStatus(
        Guid id,
        [FromBody] UpdateProposalStatusRequest request,
        CancellationToken cancellationToken)
    {
        var existing = await repository.FindAsync(id, cancellationToken);
        if (existing is null)
            return NotFound();

        if (!await accessGuard.CanAccessProjectAsync(existing.Agent.Project.Id, cancellationToken))
            return NotFound();

        IOptimizationProposal updated;
        switch (request.Status)
        {
            case ProposalStatus.Accepted when existing.Status == ProposalStatus.Draft:
                updated = await existing.Accept(cancellationToken);
                break;
            case ProposalStatus.Rejected when existing.Status == ProposalStatus.Draft:
                updated = await existing.Reject(cancellationToken);
                break;
            case ProposalStatus.Adopted when existing.Status == ProposalStatus.Accepted:
                // Manual "mark adopted" — the user confirmed the change is live; no observed version.
                updated = await existing.MarkAdopted(null, manual: true, cancellationToken);
                break;
            default:
                return Conflict(new
                {
                    error = $"Cannot change proposal status from {existing.Status} to {request.Status}.",
                });
        }

        proposalBroadcaster.Publish(ProposalStatusChangedEvent.Create(updated));
        audit.LogAudit(
            AuditAction.ProposalStatusChanged, nameof(IOptimizationProposal), updated.Id, updated.Agent.Name,
            projectId: updated.Agent.Project.Id,
            details: JsonSerializer.Serialize(new { from = existing.Status.ToString(), to = updated.Status.ToString() }));
        return Ok(mapper.ToDto(updated));
    }

    /// <summary>
    /// Machine-readable handoff package for applying the proposed change to the agent's actual
    /// implementation (Proxytrace only observes traffic; it cannot apply the change itself).
    /// </summary>
    [HttpGet("{id:guid}/artifact")]
    public async Task<ActionResult<ProposalArtifactDto>> GetArtifact(Guid id, CancellationToken cancellationToken)
    {
        var proposal = await repository.FindAsync(id, cancellationToken);
        if (proposal is null)
            return NotFound();

        if (!await accessGuard.CanAccessProjectAsync(proposal.Agent.Project.Id, cancellationToken))
            return NotFound();

        return Ok(mapper.ToArtifactDto(proposal));
    }
}
