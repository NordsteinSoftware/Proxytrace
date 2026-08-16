using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Proxytrace.Api.Auth;
using Proxytrace.Api.Dto.Agents;
using Proxytrace.Domain;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.AgentVersion;
using Proxytrace.Domain.AuditLog;

namespace Proxytrace.Api.Controllers;

/// <summary>
/// API controller for agent versions operations.
/// </summary>
[ApiController]
[Authorize]
[Route("api/agent-versions")]
public class AgentVersionsController : ControllerBase
{
    private readonly IAgentVersionRepository versions;
    private readonly IAgentRepository agents;
    private readonly ITransaction transaction;
    private readonly AgentDtoMapper mapper;
    private readonly IProjectAccessGuard accessGuard;
    private readonly ILogger<Audit> audit;

    /// <summary>
    /// Initializes a new instance of the <see cref="AgentVersionsController"/> class.
    /// </summary>
    public AgentVersionsController(
        IAgentVersionRepository versions,
        IAgentRepository agents,
        ITransaction transaction,
        AgentDtoMapper mapper,
        IProjectAccessGuard accessGuard,
        ILogger<Audit> audit)
    {
        this.versions = versions;
        this.agents = agents;
        this.transaction = transaction;
        this.mapper = mapper;
        this.accessGuard = accessGuard;
        this.audit = audit;
    }

    /// <summary>
    /// Gets.
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<AgentVersionDto>> Get(Guid id, CancellationToken cancellationToken)
    {
        var version = await versions.FindAsync(id, cancellationToken);
        if (version is null)
        {
            return NotFound();
        }
        if (!await accessGuard.CanAccessProjectAsync(version.ProjectId, cancellationToken))
        {
            return NotFound();
        }
        var fingerprint = versions.GetStrictFingerprint(version.SystemPrompt, version.Tools);
        return mapper.ToDto(version, fingerprint);
    }

    /// <summary>
    /// Re-parents an <see cref="IAgentVersion"/> from its current agent to <c>request.TargetAgentId</c>.
    /// The moved version is renumbered to <c>max(target.VersionNumber) + 1</c>, becoming the target
    /// agent's newest version, and is promoted to the target's current version. The source agent's
    /// current version is rolled back to its remaining newest version, or the source agent is deleted
    /// entirely if no versions remain. Calls referencing the moved version follow it via
    /// <c>AgentCall.AgentVersionId</c> — no call rewriting needed.
    /// </summary>
    [HttpPost("{id:guid}/move")]
    public async Task<IActionResult> Move(
        Guid id,
        [FromBody] MoveVersionRequest request,
        CancellationToken cancellationToken)
    {
        var version = await versions.FindAsync(id, cancellationToken);
        if (version is null)
        {
            return NotFound("Version not found.");
        }

        var source = await version.GetAgentAsync(cancellationToken);
        var target = await agents.FindAsync(request.TargetAgentId, cancellationToken);
        if (target is null)
        {
            return NotFound("Target agent not found.");
        }
        if (!await accessGuard.CanAccessProjectAsync(source.Project.Id, cancellationToken))
        {
            return NotFound("Version not found.");
        }
        if (!await accessGuard.CanAccessProjectAsync(target.Project.Id, cancellationToken))
        {
            return NotFound("Target agent not found.");
        }
        if (target.Id == source.Id)
        {
            return BadRequest("Target agent is the same as the source.");
        }
        if (target.Project.Id != source.Project.Id)
        {
            return BadRequest("Target agent is in a different project.");
        }
        if (source.IsSystemAgent || target.IsSystemAgent)
        {
            return BadRequest("Cannot move versions into or out of a system agent.");
        }

        await transaction.InvokeAsync(async () =>
        {
            await version.MoveToAgentAsync(target, cancellationToken);
            await agents.SetCurrentVersionAsync(target.Id, version.Id, cancellationToken);

            var remaining = await versions.GetByAgentAsync(source, cancellationToken);
            if (remaining.Count == 0)
            {
                await agents.RemoveAsync(source.Id, cancellationToken);
            }
            else
            {
                var newest = remaining.OrderByDescending(v => v.VersionNumber).First();
                if (source.CurrentVersion.Id != newest.Id)
                {
                    await agents.SetCurrentVersionAsync(source.Id, newest.Id, cancellationToken);
                }
            }
        });

        audit.LogAudit(
            AuditAction.AgentVersionMoved, nameof(IAgentVersion), version.Id, target.Name,
            projectId: target.Project.Id,
            details: JsonSerializer.Serialize(new { fromAgentId = source.Id, toAgentId = target.Id }));

        return NoContent();
    }
}
