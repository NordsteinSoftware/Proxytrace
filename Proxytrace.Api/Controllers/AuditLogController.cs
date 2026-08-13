using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Proxytrace.Api.Dto.AuditLog;
using Proxytrace.Application.Auth;
using Proxytrace.Domain.AuditLog;
using Nordstein.Core.Domain.Paging;
using Proxytrace.Domain.Project;
using Proxytrace.Domain.User;

namespace Proxytrace.Api.Controllers;

/// <summary>
/// Read-only access to the audit log. Any authenticated user may query it, but results are scoped:
/// admins see every entry (including instance-wide/global rows); project members see only the entries
/// of projects they belong to, and never global rows.
/// </summary>
[ApiController]
[Authorize]
[Route("api/audit-log")]
public class AuditLogController : ControllerBase
{
    private readonly IAuditLogRepository repository;
    private readonly ICurrentUserAccessor currentUser;
    private readonly IProjectRepository projects;

    public AuditLogController(
        IAuditLogRepository repository,
        ICurrentUserAccessor currentUser,
        IProjectRepository projects)
    {
        this.repository = repository;
        this.currentUser = currentUser;
        this.projects = projects;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<AuditLogEntryDto>>> GetAll(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] AuditAction? action = null,
        [FromQuery] string? actor = null,
        [FromQuery] Guid? projectId = null,
        [FromQuery] string? targetType = null,
        [FromQuery] Guid? targetId = null,
        [FromQuery] DateTimeOffset? from = null,
        [FromQuery] DateTimeOffset? to = null,
        CancellationToken cancellationToken = default)
    {
        var user = await currentUser.GetCurrentUserAsync(cancellationToken);
        if (user is null)
            return Forbid();

        var isAdmin = User.IsInRole(nameof(UserRole.Admin));

        IReadOnlyCollection<Guid>? scopeProjectIds;
        bool includeGlobal;

        if (projectId.HasValue)
        {
            // Narrowing to a single project. Non-admins must be a member of it, else they see nothing.
            if (!isAdmin && !await IsMemberAsync(user.Id, projectId.Value, cancellationToken))
                return new PagedResult<AuditLogEntryDto>([], 0, page, pageSize);

            scopeProjectIds = [projectId.Value];
            includeGlobal = false;
        }
        else if (isAdmin)
        {
            // Admins see everything, including global (null-project) rows.
            scopeProjectIds = null;
            includeGlobal = true;
        }
        else
        {
            // Members see only their projects' rows; never global rows.
            var memberProjects = await projects.GetByMemberAsync(user.Id, cancellationToken);
            scopeProjectIds = memberProjects.Select(p => p.Id).ToArray();
            includeGlobal = false;
        }

        var paged = await repository.GetPagedNewestFirstAsync(
            page, pageSize, action, actor, scopeProjectIds, includeGlobal, targetType, targetId, from, to, cancellationToken);
        return paged.Map(ToDto);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<AuditLogEntryDto>> Get(Guid id, CancellationToken cancellationToken)
    {
        var entry = await repository.FindAsync(id, cancellationToken);
        if (entry is null)
            return NotFound();

        // Hide out-of-scope entries behind a 404 rather than a 403 so existence does not leak.
        if (!await CanViewAsync(entry, cancellationToken))
            return NotFound();

        return ToDto(entry);
    }

    private async Task<bool> CanViewAsync(IAuditLogEntry entry, CancellationToken cancellationToken)
    {
        if (User.IsInRole(nameof(UserRole.Admin)))
            return true;

        // Members never see global rows, and only see rows of projects they belong to.
        if (entry.ProjectId is not { } projectId)
            return false;

        var user = await currentUser.GetCurrentUserAsync(cancellationToken);
        return user is not null && await IsMemberAsync(user.Id, projectId, cancellationToken);
    }

    private async Task<bool> IsMemberAsync(Guid userId, Guid projectId, CancellationToken cancellationToken)
    {
        var memberProjects = await projects.GetByMemberAsync(userId, cancellationToken);
        return memberProjects.Any(p => p.Id == projectId);
    }

    private static AuditLogEntryDto ToDto(IAuditLogEntry e) => new(
        e.Id,
        e.Action,
        e.ActorType,
        e.ActorUserId,
        e.ActorEmail,
        e.ActorApiKeyId,
        e.ProjectId,
        e.TargetType,
        e.TargetId,
        e.TargetLabel,
        e.Details,
        e.Outcome,
        e.CreatedAt);
}
