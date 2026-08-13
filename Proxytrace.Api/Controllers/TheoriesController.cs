using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Proxytrace.Api.Auth;
using Proxytrace.Api.Auth.Licensing;
using Proxytrace.Api.Dto.Proposals;
using Proxytrace.Api.Dto.Theories;
using Proxytrace.Application.Optimization;
using Proxytrace.Domain;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.AuditLog;
using Nordstein.Core.Domain.Exceptions;
using Proxytrace.Domain.ModelEndpoint;
using Proxytrace.Domain.OptimizationTheory;
using Proxytrace.Domain.TestSuite;
using Proxytrace.Domain.Tools;
using Proxytrace.Licensing;

namespace Proxytrace.Api.Controllers;

[ApiController]
[Authorize]
[RequiresFeature(LicenseFeature.OptimizationProposals)]
[Route("api/theories")]
public class TheoriesController : ControllerBase
{
    private readonly IOptimizationTheoryRepository repository;
    private readonly ITheoryValidationService validationService;
    private readonly ISystemPromptTheory.CreateNew createSystemPrompt;
    private readonly IModelSwitchTheory.CreateNew createModelSwitch;
    private readonly IToolUpdateTheory.CreateNew createToolUpdate;
    private readonly IAgentRepository agents;
    private readonly IRepository<ITestSuite> suites;
    private readonly IRepository<IModelEndpoint> endpoints;
    private readonly ITestSuite.CreateNew createSuite;
    private readonly TheoryDtoMapper mapper;
    private readonly IProjectAccessGuard accessGuard;
    private readonly ILogger<Audit> audit;

    public TheoriesController(
        IOptimizationTheoryRepository repository,
        ITheoryValidationService validationService,
        ISystemPromptTheory.CreateNew createSystemPrompt,
        IModelSwitchTheory.CreateNew createModelSwitch,
        IToolUpdateTheory.CreateNew createToolUpdate,
        IAgentRepository agents,
        IRepository<ITestSuite> suites,
        IRepository<IModelEndpoint> endpoints,
        ITestSuite.CreateNew createSuite,
        TheoryDtoMapper mapper,
        IProjectAccessGuard accessGuard,
        ILogger<Audit> audit)
    {
        this.repository = repository;
        this.validationService = validationService;
        this.createSystemPrompt = createSystemPrompt;
        this.createModelSwitch = createModelSwitch;
        this.createToolUpdate = createToolUpdate;
        this.agents = agents;
        this.suites = suites;
        this.endpoints = endpoints;
        this.createSuite = createSuite;
        this.mapper = mapper;
        this.accessGuard = accessGuard;
        this.audit = audit;
    }

    // Resolve the effective owning project of a list query and verify access. Admins
    // (accessible == null) pass for any scope. Non-admins must scope to a project they belong to —
    // directly via projectId or via the agent's project — otherwise the query returns nothing rather
    // than leaking other tenants' rows.
    /// <summary>
    /// The theories the caller may list under the optional agent/project filters. An agent filter
    /// authorizes against that agent's project and then queries by agent; otherwise the request is
    /// scoped to the projects the caller may read.
    /// </summary>
    private async Task<IReadOnlyList<IOptimizationTheory>> ListScopedAsync(
        Guid? agentId,
        Guid? projectId,
        CancellationToken cancellationToken)
    {
        var scope = await accessGuard.ResolveListScopeAsync(projectId, cancellationToken);
        if (scope.IsEmpty())
            return [];

        if (agentId is { } aid)
        {
            var agent = await agents.FindAsync(aid, cancellationToken);
            if (agent is null || !scope.Admits(agent.Project.Id))
                return [];
            return await repository.GetByAgentAsync(aid, cancellationToken);
        }

        if (scope.SingleProject() is { } singleProject)
            return await repository.GetByProjectAsync(singleProject, cancellationToken);

        var all = await repository.GetAllAsync(cancellationToken);
        return scope is null ? all : all.Where(t => scope.Contains(t.Agent.Project.Id)).ToArray();
    }

    [HttpGet]
    public async Task<IReadOnlyList<TheoryDto>> GetAll(
        [FromQuery] Guid? agentId = null,
        [FromQuery] Guid? projectId = null,
        [FromQuery] TheoryStatus? status = null,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<IOptimizationTheory> theories = await ListScopedAsync(agentId, projectId, cancellationToken);

        if (status.HasValue)
            theories = theories.Where(t => t.Status == status.Value).ToList();

        return theories.Select(mapper.ToDto).ToList();
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<TheoryDto>> Get(Guid id, CancellationToken cancellationToken)
    {
        var theory = await repository.FindAsync(id, cancellationToken);
        if (theory is null)
            return NotFound();

        if (!await accessGuard.CanAccessProjectAsync(theory.Agent.Project.Id, cancellationToken))
            return NotFound();

        return Ok(mapper.ToDto(theory));
    }

    /// <summary>
    /// Submits a new optimization theory from any producer. The theory is deduplicated and
    /// rate-limited, then validated by an A/B run in the background.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<TheoryDto>> Submit(
        [FromBody] SubmitTheoryRequest request,
        CancellationToken cancellationToken)
    {
        var agent = await agents.FindAsync(request.AgentId, cancellationToken);
        if (agent is null)
            return NotFound($"Agent {request.AgentId} does not exist.");

        if (!await accessGuard.CanAccessProjectAsync(agent.Project.Id, cancellationToken))
            return NotFound($"Agent {request.AgentId} does not exist.");

        var suite = await suites.FindAsync(request.SuiteId, cancellationToken);
        if (suite is null)
            return NotFound($"Suite {request.SuiteId} does not exist.");

        // The suite is supplied by id, independently of the agent — verify the caller can access its
        // project too, otherwise a foreign suite could be paired with the caller's agent. (The
        // proposed endpoint is a globally-shared resource with no project owner, so it needs no check.)
        if (!await accessGuard.CanAccessProjectAsync(suite.Agent.Project.Id, cancellationToken))
            return NotFound($"Suite {request.SuiteId} does not exist.");

        IOptimizationTheory theory;
        switch (request.Details)
        {
            case SystemPromptDetailsDto systemPrompt:
                theory = createSystemPrompt(
                    agent, suite, request.Source, request.Priority, request.Rationale,
                    systemPrompt.ProposedSystemMessage, evidenceTestRunIds: []);
                break;

            case ModelSwitchSeedDetailsDto modelSwitch:
            {
                var proposedEndpoint = await endpoints.FindAsync(modelSwitch.ProposedEndpointId, cancellationToken);
                if (proposedEndpoint is null)
                    return BadRequest($"Proposed endpoint {modelSwitch.ProposedEndpointId} does not exist.");

                theory = createModelSwitch(
                    agent, suite, request.Source, request.Priority, request.Rationale,
                    proposedEndpoint, evidenceTestRunIds: []);
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

                theory = createToolUpdate(
                    agent, suite, request.Source, request.Priority, request.Rationale,
                    proposedTools, evidenceTestRunIds: []);
                break;
            }

            default:
                return BadRequest("Unsupported theory details kind.");
        }

        var result = await validationService.SubmitAsync(theory, cancellationToken);
        if (result is { Outcome: TheorySubmissionOutcome.Accepted, Theory: { } acceptedTheory })
        {
            audit.LogAudit(
                AuditAction.TheorySubmitted, nameof(IOptimizationTheory), acceptedTheory.Id, agent.Name,
                projectId: agent.Project.Id,
                details: JsonSerializer.Serialize(new { source = request.Source.ToString(), priority = request.Priority.ToString() }));
        }

        return (result.Outcome, result.Theory) switch
        {
            (TheorySubmissionOutcome.Accepted, { } accepted) => Accepted(mapper.ToDto(accepted)),
            (TheorySubmissionOutcome.Duplicate, _) => Conflict("An identical theory or proposal already exists."),
            (TheorySubmissionOutcome.QuotaExceeded, _) => StatusCode(
                StatusCodes.Status429TooManyRequests,
                "The project has too many theories awaiting validation. Try again later."),
            _ => StatusCode(StatusCodes.Status500InternalServerError),
        };
    }

    /// <summary>
    /// Resets a Validated, Invalidated, or Failed theory back to Proposed, deleting any proposal it
    /// spawned and re-queuing it for fresh validation. Refused when the spawned proposal was already
    /// accepted.
    /// </summary>
    [HttpPost("{id:guid}/reset")]
    public async Task<ActionResult<TheoryDto>> Reset(Guid id, CancellationToken cancellationToken)
    {
        var existing = await repository.FindAsync(id, cancellationToken);
        if (existing is null)
            return NotFound($"Theory {id} does not exist.");

        if (!await accessGuard.CanAccessProjectAsync(existing.Agent.Project.Id, cancellationToken))
            return NotFound($"Theory {id} does not exist.");

        var result = await validationService.ResetToProposedAsync(id, cancellationToken);
        if (result.Outcome == TheoryResetOutcome.Reset)
        {
            audit.LogAudit(
                AuditAction.TheoryReset, nameof(IOptimizationTheory), id, existing.Agent.Name,
                projectId: existing.Agent.Project.Id);
        }

        return (result.Outcome, result.Theory) switch
        {
            (TheoryResetOutcome.Reset, { } reset) => Ok(mapper.ToDto(reset)),
            (TheoryResetOutcome.NotFound, _) => NotFound($"Theory {id} does not exist."),
            (TheoryResetOutcome.NotResettable, _) => Conflict("Only Validated, Invalidated, or Failed theories can be reset."),
            (TheoryResetOutcome.BlockedByAcceptedProposal, _) => Conflict(
                "This proposal was already promoted; resetting would not revert the applied change."),
            _ => StatusCode(StatusCodes.Status500InternalServerError),
        };
    }

    /// <summary>
    /// Dismisses a theory at the user's request. A Proposed theory is rejected without running A/B
    /// validation; a Validating theory has its in-flight A/B run cancelled; a Failed theory is filed
    /// away without a retry. Either way it lands in Invalidated. Returns 409 when the theory is
    /// already Validated or Invalidated.
    /// </summary>
    [HttpPost("{id:guid}/reject")]
    public async Task<ActionResult<TheoryDto>> Reject(Guid id, CancellationToken cancellationToken)
    {
        var existing = await repository.FindAsync(id, cancellationToken);
        if (existing is null)
            return NotFound($"Theory {id} does not exist.");

        if (!await accessGuard.CanAccessProjectAsync(existing.Agent.Project.Id, cancellationToken))
            return NotFound($"Theory {id} does not exist.");

        var result = await validationService.RejectAsync(id, cancellationToken);
        if (result.Outcome == TheoryRejectOutcome.Rejected)
        {
            audit.LogAudit(
                AuditAction.TheoryRejected, nameof(IOptimizationTheory), id, existing.Agent.Name,
                projectId: existing.Agent.Project.Id);
        }

        return (result.Outcome, result.Theory) switch
        {
            (TheoryRejectOutcome.Rejected, { } rejected) => Ok(mapper.ToDto(rejected)),
            (TheoryRejectOutcome.NotFound, _) => NotFound($"Theory {id} does not exist."),
            (TheoryRejectOutcome.NotActive, _) => Conflict("Only a Proposed, Validating, or Failed theory can be rejected."),
            _ => StatusCode(StatusCodes.Status500InternalServerError),
        };
    }

    /// <summary>
    /// Test-only: seeds a theory directly in a chosen lifecycle state, bypassing the asynchronous
    /// validation pipeline so board states are deterministic in tests.
    /// </summary>
    [HttpPost("seed")]
    [TestOnlyEndpoint]
    public async Task<ActionResult<TheoryDto>> Seed(
        [FromBody] SeedTheoryRequest request,
        CancellationToken cancellationToken)
    {
        var agent = await agents.FindAsync(request.AgentId, cancellationToken);
        if (agent is null)
            return NotFound($"Agent {request.AgentId} does not exist.");

        if (request.Status == TheoryStatus.Validated && request.ResultingProposalId is null)
            return BadRequest("A Validated theory must reference a ResultingProposalId.");

        var suite = await suites.AddAsync(
            createSuite($"Seeded theory suite {DateTimeOffset.UtcNow:O}", agent, [], []),
            cancellationToken);

        IOptimizationTheory theory;
        switch (request.Details)
        {
            case SystemPromptDetailsDto systemPrompt:
                theory = createSystemPrompt(
                    agent, suite, request.Source, request.Priority, request.Rationale,
                    systemPrompt.ProposedSystemMessage, evidenceTestRunIds: []);
                break;

            case ModelSwitchSeedDetailsDto modelSwitch:
            {
                var proposedEndpoint = await endpoints.FindAsync(modelSwitch.ProposedEndpointId, cancellationToken);
                if (proposedEndpoint is null)
                    return BadRequest($"Proposed endpoint {modelSwitch.ProposedEndpointId} does not exist.");

                theory = createModelSwitch(
                    agent, suite, request.Source, request.Priority, request.Rationale,
                    proposedEndpoint, evidenceTestRunIds: []);
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

                theory = createToolUpdate(
                    agent, suite, request.Source, request.Priority, request.Rationale,
                    proposedTools, evidenceTestRunIds: []);
                break;
            }

            default:
                return BadRequest("Unsupported theory details kind.");
        }

        await repository.AddAsync(theory, cancellationToken);
        var saved = await DriveToStateAsync(theory.Id, request, cancellationToken);
        return Ok(mapper.ToDto(saved));
    }

    /// <summary>
    /// Drives a freshly-added (Proposed) theory to the requested lifecycle state. Each step is a
    /// separate persisted transition, so the sequence is reloaded and retried on an optimistic
    /// concurrency conflict — the transition is resumable from whatever state actually committed.
    /// </summary>
    private async Task<IOptimizationTheory> DriveToStateAsync(
        Guid theoryId,
        SeedTheoryRequest request,
        CancellationToken cancellationToken)
    {
        const int maxAttempts = 5;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var theory = await repository.GetAsync(theoryId, cancellationToken);

                if (theory.Status == TheoryStatus.Proposed && request.Status != TheoryStatus.Proposed)
                    theory = await theory.SetValidating(cancellationToken);

                if (theory.Status == TheoryStatus.Validating)
                {
                    // The Seed guard guarantees a non-null ResultingProposalId for Validated.
                    if (request.Status == TheoryStatus.Validated && request.ResultingProposalId is { } proposalId)
                        theory = await theory.SetValidated(
                            proposalId, request.BaselinePassRate, request.ProjectedPassRate, request.PValue, abTestRunId: null, cancellationToken);
                    else if (request.Status == TheoryStatus.Invalidated)
                        theory = await theory.SetInvalidated(
                            request.BaselinePassRate, request.ProjectedPassRate, request.PValue, abTestRunId: null, cancellationToken);
                    else if (request.Status == TheoryStatus.Failed)
                        theory = await theory.SetFailed(abTestRunId: null, cancellationToken);
                }

                return theory;
            }
            catch (OptimisticConcurrencyException) when (attempt < maxAttempts)
            {
                await Task.Delay(25 * attempt, cancellationToken);
            }
        }
    }
}
