using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Proxytrace.Api.Auth;
using Proxytrace.Api.Auth.Licensing;
using Proxytrace.Api.Dto.Costs;
using Proxytrace.Application.CostControl;
using Proxytrace.Domain;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.ApiKey;
using Proxytrace.Domain.AuditLog;
using Proxytrace.Domain.CostLimit;
using Proxytrace.Domain.CostLimitBreach;
using Proxytrace.Domain.Project;
using Proxytrace.Domain.User;
using Proxytrace.Licensing;

namespace Proxytrace.Api.Controllers;

/// <summary>
/// CRUD for monthly cost budgets. Listing is free and open to every project member so the Costs
/// page renders the same regardless of tier; mutations are admin-only and gated behind
/// <see cref="LicenseFeature.CostControls"/>, so an unlicensed install keeps its configuration
/// (and can restore enforcement by re-licensing) but cannot add to it.
/// </summary>
[ApiController]
[Authorize]
[Route("api/cost-limits")]
public class CostLimitsController : ControllerBase
{
    private readonly ICostLimitRepository costLimits;
    private readonly ICostLimitBreachRepository breaches;
    private readonly ICostStatistics costStatistics;
    private readonly IProjectRepository projects;
    private readonly IAgentRepository agents;
    private readonly IApiKeyRepository apiKeys;
    private readonly ICostLimit.CreateNew createCostLimit;
    private readonly ITransaction transaction;
    private readonly IProjectAccessGuard accessGuard;
    private readonly ILogger<Audit> audit;

    public CostLimitsController(
        ICostLimitRepository costLimits,
        ICostLimitBreachRepository breaches,
        ICostStatistics costStatistics,
        IProjectRepository projects,
        IAgentRepository agents,
        IApiKeyRepository apiKeys,
        ICostLimit.CreateNew createCostLimit,
        ITransaction transaction,
        IProjectAccessGuard accessGuard,
        ILogger<Audit> audit)
    {
        this.costLimits = costLimits;
        this.breaches = breaches;
        this.costStatistics = costStatistics;
        this.projects = projects;
        this.agents = agents;
        this.apiKeys = apiKeys;
        this.createCostLimit = createCostLimit;
        this.transaction = transaction;
        this.accessGuard = accessGuard;
        this.audit = audit;
    }

    [HttpGet]
    public async Task<IReadOnlyList<CostLimitDto>> GetAll(
        [FromQuery] Guid projectId,
        CancellationToken cancellationToken = default)
    {
        // Empty rather than 404: a non-member must not learn whether the project exists.
        if (!await accessGuard.CanAccessProjectAsync(projectId, cancellationToken))
            return [];

        IReadOnlyList<ICostLimit> limits = await costLimits.GetByProjectAsync(projectId, cancellationToken);
        return limits.Select(ToDto).ToArray();
    }

    /// <summary>
    /// The project's budgets joined with this month's spend and breach state — what the Costs page
    /// draws its consumption meters from.
    /// </summary>
    /// <remarks>
    /// Its own endpoint rather than a slice of <c>GET /api/statistics/cost-overview</c>: this is the
    /// read a budget create/edit/delete invalidates, and it needs one aggregate scan of the trace
    /// table (two when a key-scoped budget exists) against the overview's seven. Free for every
    /// project member, exactly like the budget list itself.
    ///
    /// The literal route sits above <c>{id:guid}</c>, and "status" is not a GUID, so the two can
    /// never collide.
    /// </remarks>
    [HttpGet("status")]
    public async Task<IReadOnlyList<CostBudgetStatusDto>> GetStatus(
        [FromQuery] Guid projectId,
        CancellationToken cancellationToken = default)
    {
        // Empty rather than 404, matching GetAll: a non-member must not learn whether the project
        // exists.
        if (!await accessGuard.CanAccessProjectAsync(projectId, cancellationToken))
            return [];

        IReadOnlyList<CostBudgetStatus> statuses =
            await costStatistics.GetBudgetStatusAsync(projectId, cancellationToken);

        return statuses
            .Select(b => new CostBudgetStatusDto(
                b.CostLimitId, b.AgentId, b.AgentName, b.ApiKeyId, b.ApiKeyName,
                b.SoftLimitEur, b.HardLimitEur,
                b.Enabled, b.MonthToDateSpendEur, b.SoftBreached, b.HardBreached))
            .ToArray();
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<CostLimitDto>> Get(Guid id, CancellationToken cancellationToken)
    {
        ICostLimit? limit = await costLimits.FindAsync(id, cancellationToken);
        if (limit is null)
            return NotFound();
        if (!await accessGuard.CanAccessProjectAsync(limit.Project.Id, cancellationToken))
            return NotFound();

        return ToDto(limit);
    }

    [HttpPost]
    [Authorize(Roles = nameof(UserRole.Admin))]
    [RequiresFeature(LicenseFeature.CostControls)]
    public async Task<ActionResult<CostLimitDto>> Create(
        [FromBody] CreateCostLimitRequest request,
        CancellationToken cancellationToken)
    {
        // Emit the audit only AFTER the transaction commits — LogAudit writes to a decoupled
        // channel, so emitting inside the lambda would leave a phantom row if the commit fails.
        (Guid Id, string Label, Guid ProjectId, string Details)? created = null;
        var result = await transaction.InvokeAsync<ActionResult<CostLimitDto>>(async () =>
        {
            if (ValidateThresholds(request.SoftLimitEur, request.HardLimitEur) is { } error)
                return BadRequest(error);

            IProject? project = await projects.FindAsync(request.ProjectId, cancellationToken);
            if (project is null)
                return BadRequest($"Project {request.ProjectId} not found.");
            if (!await accessGuard.CanAccessProjectAsync(project.Id, cancellationToken))
                return NotFound();

            // A budget has exactly one scope. Rejected here as a readable 400 rather than left to
            // surface as a domain validation error from the repository.
            if (request.AgentId is not null && request.ApiKeyId is not null)
                return BadRequest("A budget is scoped to an agent or to an API key, not to both.");

            IAgent? agent = null;
            if (request.AgentId is { } agentId)
            {
                agent = await agents.FindAsync(agentId, cancellationToken);
                if (agent is null || agent.Project.Id != project.Id)
                    return BadRequest($"Agent {agentId} not found.");
            }

            IApiKey? apiKey = null;
            if (request.ApiKeyId is { } apiKeyId)
            {
                apiKey = await apiKeys.FindAsync(apiKeyId, cancellationToken);
                if (apiKey is null || apiKey.Project.Id != project.Id)
                    return BadRequest($"API key {apiKeyId} not found.");
            }

            // Checked here as well as by the partial unique indexes: the index turns a race into a
            // 500, this turns the ordinary "already configured" case into a readable 409.
            IReadOnlyList<ICostLimit> existing = await costLimits.GetByProjectAsync(project.Id, cancellationToken);
            if (existing.Any(l => l.Agent?.Id == request.AgentId && l.ApiKey?.Id == request.ApiKeyId))
                return Conflict((request.AgentId, request.ApiKeyId) switch
                {
                    (not null, _) => "This agent already has a budget.",
                    (_, not null) => "This API key already has a budget.",
                    _ => "This project already has a budget.",
                });

            ICostLimit saved = await costLimits.AddAsync(
                createCostLimit(project, agent, apiKey, request.SoftLimitEur, request.HardLimitEur, request.Enabled),
                cancellationToken);

            created = (saved.Id, ScopeLabel(saved), project.Id, BuildAuditDetails(saved));
            return CreatedAtAction(nameof(Get), new { id = saved.Id }, ToDto(saved));
        });

        if (created is { } c)
            audit.LogAudit(
                AuditAction.CostLimitCreated, nameof(ICostLimit), c.Id, c.Label,
                projectId: c.ProjectId, details: c.Details);

        return result;
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    [RequiresFeature(LicenseFeature.CostControls)]
    public async Task<ActionResult<CostLimitDto>> Update(
        Guid id,
        [FromBody] UpdateCostLimitRequest request,
        CancellationToken cancellationToken)
    {
        (Guid Id, string Label, Guid ProjectId, string Details)? updated = null;
        var result = await transaction.InvokeAsync<ActionResult<CostLimitDto>>(async () =>
        {
            if (ValidateThresholds(request.SoftLimitEur, request.HardLimitEur) is { } error)
                return BadRequest(error);

            ICostLimit? limit = await costLimits.FindAsync(id, cancellationToken);
            if (limit is null)
                return NotFound();
            if (!await accessGuard.CanAccessProjectAsync(limit.Project.Id, cancellationToken))
                return NotFound();

            ICostLimit saved = await limit.Update(
                request.SoftLimitEur, request.HardLimitEur, request.Enabled, cancellationToken);

            // Editing thresholds re-arms the budget: without this a limit raised after its hard
            // breach would keep blocking (the breach row is what the proxy reads) and a lowered soft
            // limit would never warn again this month.
            await breaches.DeleteForLimitAsync(saved.Id, cancellationToken);

            updated = (saved.Id, ScopeLabel(saved), saved.Project.Id, BuildAuditDetails(saved));
            return ToDto(saved);
        });

        if (updated is { } u)
            audit.LogAudit(
                AuditAction.CostLimitUpdated, nameof(ICostLimit), u.Id, u.Label,
                projectId: u.ProjectId, details: u.Details);

        return result;
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    [RequiresFeature(LicenseFeature.CostControls)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        ICostLimit? limit = await costLimits.FindAsync(id, cancellationToken);
        if (limit is null)
            return NotFound();
        if (!await accessGuard.CanAccessProjectAsync(limit.Project.Id, cancellationToken))
            return NotFound();

        string label = ScopeLabel(limit);
        Guid projectId = limit.Project.Id;

        bool removed = await transaction.InvokeAsync(async () =>
        {
            // The FK cascade would take the breach rows anyway; clearing them explicitly keeps the
            // "delete lifts the block" behaviour visible at the call site rather than implied.
            await breaches.DeleteForLimitAsync(id, cancellationToken);
            return await costLimits.RemoveAsync(id, cancellationToken);
        });

        if (!removed)
            return NotFound();

        audit.LogAudit(
            AuditAction.CostLimitDeleted, nameof(ICostLimit), id, label, projectId: projectId);
        return NoContent();
    }

    private static string? ValidateThresholds(decimal? soft, decimal? hard)
    {
        if (soft is null && hard is null)
            return "A budget must set at least a soft or a hard limit.";

        if (soft is <= 0m || hard is <= 0m)
            return "Budget amounts must be greater than zero.";

        if (soft is { } s && hard is { } h && s > h)
            return "The soft limit must not exceed the hard limit.";

        return null;
    }

    /// <summary>
    /// Names what the budget is scoped to, for the audit entry: the agent, the key, or — for the
    /// project-wide budget — the project itself.
    /// </summary>
    private static string ScopeLabel(ICostLimit limit)
        => limit.Agent?.Name ?? limit.ApiKey?.Name ?? limit.Project.Name;

    private static string BuildAuditDetails(ICostLimit limit)
        => JsonSerializer.Serialize(new
        {
            agentId = limit.Agent?.Id,
            apiKeyId = limit.ApiKey?.Id,
            softLimitEur = limit.SoftLimitEur,
            hardLimitEur = limit.HardLimitEur,
            enabled = limit.Enabled,
        });

    private static CostLimitDto ToDto(ICostLimit limit)
        => new(
            Id: limit.Id,
            ProjectId: limit.Project.Id,
            AgentId: limit.Agent?.Id,
            AgentName: limit.Agent?.Name,
            ApiKeyId: limit.ApiKey?.Id,
            ApiKeyName: limit.ApiKey?.Name,
            SoftLimitEur: limit.SoftLimitEur,
            HardLimitEur: limit.HardLimitEur,
            Enabled: limit.Enabled,
            CreatedAt: limit.CreatedAt,
            UpdatedAt: limit.UpdatedAt);
}
