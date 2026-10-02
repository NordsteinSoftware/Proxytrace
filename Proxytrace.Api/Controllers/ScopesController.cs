using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Proxytrace.Api.Auth;
using Proxytrace.Api.Dto.Scopes;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.AuditLog;
using Proxytrace.Domain.Scope;

namespace Proxytrace.Api.Controllers;

/// <summary>
/// API controller for scopes — the use-case groups of agents inside a project. Scopes are created
/// by ingestion (never through this API); members of the project can list them, edit their display
/// name and description, and delete them.
/// </summary>
[ApiController]
[Authorize]
[Route("api/scopes")]
public class ScopesController : ControllerBase
{
    private readonly IScopeRepository repository;
    private readonly IAgentRepository agents;
    private readonly IProjectAccessGuard accessGuard;
    private readonly ILogger<Audit> audit;

    /// <summary>
    /// Initializes a new instance of the <see cref="ScopesController"/> class.
    /// </summary>
    public ScopesController(
        IScopeRepository repository,
        IAgentRepository agents,
        IProjectAccessGuard accessGuard,
        ILogger<Audit> audit)
    {
        this.repository = repository;
        this.agents = agents;
        this.accessGuard = accessGuard;
        this.audit = audit;
    }

    /// <summary>
    /// Lists every scope of a project, most recently active first. Unpaged: bounded by the per-project
    /// scope cap, and the UI needs the whole set for its pickers. Empty when the caller cannot access
    /// the project.
    /// </summary>
    [HttpGet]
    public async Task<IReadOnlyList<ScopeListItemDto>> GetAll(
        [FromQuery] Guid projectId,
        CancellationToken cancellationToken = default)
    {
        if (!await accessGuard.CanAccessProjectAsync(projectId, cancellationToken))
            return [];

        var overviews = await repository.GetOverviewsAsync(projectId, cancellationToken);
        return overviews.Select(ScopeListItemDto.From).ToArray();
    }

    /// <summary>
    /// Returns one scope with its member agents. 404 when it does not exist or the caller cannot
    /// access its project.
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ScopeDetailDto>> Get(Guid id, CancellationToken cancellationToken)
    {
        var overview = await repository.GetOverviewAsync(id, cancellationToken);
        if (overview is null || !await accessGuard.CanAccessProjectAsync(overview.Scope.ProjectId, cancellationToken))
            return NotFound();

        return await ToDetailDtoAsync(overview, cancellationToken);
    }

    /// <summary>
    /// Edits a scope's display name and description. 404 when it does not exist or the caller
    /// cannot access its project.
    /// </summary>
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ScopeDetailDto>> Update(
        Guid id,
        [FromBody] UpdateScopeRequest request,
        CancellationToken cancellationToken)
    {
        var scope = await repository.FindAsync(id, cancellationToken);
        if (scope is null || !await accessGuard.CanAccessProjectAsync(scope.ProjectId, cancellationToken))
            return NotFound();

        var saved = await scope.ChangeDetails(request.DisplayName, request.Description, cancellationToken);
        audit.LogAudit(
            AuditAction.ScopeUpdated, nameof(IScope), saved.Id, saved.DisplayName ?? saved.ExternalKey,
            projectId: saved.ProjectId);

        var overview = await repository.GetOverviewAsync(saved.Id, cancellationToken);
        return overview is null ? NotFound() : await ToDetailDtoAsync(overview, cancellationToken);
    }

    /// <summary>
    /// Deletes a scope, freeing its slot under the per-project scope cap. Its traces are kept and keep
    /// their scope id (the column is FK-free, like a session's): a client that sends the key again
    /// re-creates the scope under the same derived id, and those traces belong to it again. 404 when
    /// it does not exist or the caller cannot access its project.
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var scope = await repository.FindAsync(id, cancellationToken);
        if (scope is null || !await accessGuard.CanAccessProjectAsync(scope.ProjectId, cancellationToken))
            return NotFound();

        if (!await repository.RemoveAsync(scope.Id, cancellationToken))
            return NotFound();

        audit.LogAudit(
            AuditAction.ScopeDeleted, nameof(IScope), scope.Id, scope.DisplayName ?? scope.ExternalKey,
            projectId: scope.ProjectId);
        return NoContent();
    }

    private async Task<ScopeDetailDto> ToDetailDtoAsync(ScopeOverview overview, CancellationToken cancellationToken)
    {
        var names = (await agents.GetManyAsync(
                overview.Agents.Select(a => a.AgentId).ToArray(),
                ignoreMissing: true,
                cancellationToken: cancellationToken))
            .ToDictionary(a => a.Id, a => a.Name);

        return new ScopeDetailDto(
            overview.Scope.Id,
            overview.Scope.ProjectId,
            overview.Scope.ExternalKey,
            overview.Scope.DisplayName,
            overview.Scope.Description,
            overview.Scope.CreatedAt,
            overview.LastActivityAt,
            overview.TraceCount,
            overview.TotalTokens,
            overview.Agents
                .Select(a => new ScopeAgentDto(
                    a.AgentId,
                    names.GetValueOrDefault(a.AgentId, "(unknown)"),
                    a.TraceCount,
                    a.TotalTokens,
                    a.FirstSeenAt,
                    a.LastSeenAt))
                .ToArray());
    }
}
