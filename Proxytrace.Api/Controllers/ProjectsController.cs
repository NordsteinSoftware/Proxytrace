using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Proxytrace.Api.Dto.Projects;
using Proxytrace.Application.Auth;
using Proxytrace.Application.Evaluator;
using Proxytrace.Application.Tracey;
using Proxytrace.Domain;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.AuditLog;
using Proxytrace.Domain.ModelEndpoint;
using Proxytrace.Domain.Paging;
using Proxytrace.Domain.Project;
using Proxytrace.Domain.User;

namespace Proxytrace.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/projects")]
public class ProjectsController : ControllerBase
{
    private readonly IProjectRepository repository;
    private readonly IRepository<IModelEndpoint> endpointRepository;
    private readonly IRepository<IUser> userRepository;
    private readonly IAgentRepository agentRepository;
    private readonly IProject.CreateNew createNew;
    private readonly IProject.CreateExisting createExisting;
    private readonly ITraceyAgentProvisioner traceyProvisioner;
    private readonly IDefaultEvaluatorProvisioner defaultEvaluatorProvisioner;
    private readonly ICurrentUserAccessor currentUser;
    private readonly ILogger<Audit> audit;

    public ProjectsController(
        IProjectRepository repository,
        IRepository<IModelEndpoint> endpointRepository,
        IRepository<IUser> userRepository,
        IAgentRepository agentRepository,
        IProject.CreateNew createNew,
        IProject.CreateExisting createExisting,
        ITraceyAgentProvisioner traceyProvisioner,
        IDefaultEvaluatorProvisioner defaultEvaluatorProvisioner,
        ICurrentUserAccessor currentUser,
        ILogger<Audit> audit)
    {
        this.repository = repository;
        this.endpointRepository = endpointRepository;
        this.userRepository = userRepository;
        this.agentRepository = agentRepository;
        this.createNew = createNew;
        this.createExisting = createExisting;
        this.traceyProvisioner = traceyProvisioner;
        this.defaultEvaluatorProvisioner = defaultEvaluatorProvisioner;
        this.currentUser = currentUser;
        this.audit = audit;
    }

    [HttpGet]
    public async Task<PagedResult<ProjectListItemDto>> GetAll(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        // Clamp before either branch: the admin path clamps inside GetPagedAsync, but the in-memory
        // member path below did not, so pageSize=int.MaxValue returned every member project in one
        // response and echoed the unclamped size back in the PagedResult.
        (page, pageSize) = Paging.Clamp(page, pageSize);

        // Admins see every project; non-admins (e.g. the sidebar project switcher) see only the
        // projects they belong to — never the full cross-tenant list.
        if (User.IsInRole(nameof(UserRole.Admin)))
        {
            var paged = await repository.GetPagedAsync(page, pageSize, cancellationToken);
            return paged.Map(ProjectDtoMapper.ToListItemDto);
        }

        var user = await currentUser.GetCurrentUserAsync(cancellationToken);
        if (user is null)
            return new PagedResult<ProjectListItemDto>([], 0, page, pageSize);

        var memberProjects = await repository.GetByMemberAsync(user.Id, cancellationToken);
        var items = memberProjects
            .Skip(Paging.Offset(page, pageSize))
            .Take(pageSize)
            .Select(ProjectDtoMapper.ToListItemDto)
            .ToArray();
        return new PagedResult<ProjectListItemDto>(items, memberProjects.Count, page, pageSize);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ProjectDto>> Get(Guid id, CancellationToken cancellationToken)
    {
        var project = await repository.FindAsync(id, cancellationToken);
        if (project is null)
            return NotFound();
        // Hide projects the caller cannot access behind a 404 so existence does not leak.
        if (!await CanAccessAsync(project, cancellationToken))
            return NotFound();
        return ToDto(project);
    }

    [HttpPost]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<ActionResult<ProjectDto>> Create(
        [FromBody] CreateProjectRequest request,
        CancellationToken cancellationToken)
    {
        var endpoint = await endpointRepository.FindAsync(request.SystemEndpointId, cancellationToken);
        if (endpoint is null)
            return BadRequest($"SystemEndpoint {request.SystemEndpointId} not found.");

        var members = await ResolveMembersAsync(request.MemberIds, cancellationToken);
        if (members is null)
            return BadRequest("One or more memberIds reference unknown users.");

        var project = createNew(request.Name, endpoint, members);
        var saved = await repository.AddAsync(project, cancellationToken);
        await traceyProvisioner.EnsureTraceyAgentAsync(saved, cancellationToken);
        await defaultEvaluatorProvisioner.EnsureDefaultEvaluatorsAsync(saved, cancellationToken);
        audit.LogAudit(AuditAction.ProjectCreated, nameof(IProject), saved.Id, saved.Name, projectId: saved.Id);
        return CreatedAtAction(nameof(Get), new { id = saved.Id }, ToDto(saved));
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<ActionResult<ProjectDto>> Update(
        Guid id,
        [FromBody] UpdateProjectRequest request,
        CancellationToken cancellationToken)
    {
        var existing = await repository.FindAsync(id, cancellationToken);
        if (existing is null)
            return NotFound();
        var endpoint = existing.SystemEndpoint.Id == request.SystemEndpointId
            ? existing.SystemEndpoint
            : await endpointRepository.GetAsync(request.SystemEndpointId, cancellationToken);

        // Membership is NOT mass-assignable here — it changes only via the dedicated add/remove
        // endpoints. Carry the existing member set through unchanged (snapshot first, since
        // createExisting may mutate existing.Members in place).
        var members = existing.Members.ToArray();
        var priorName = existing.Name;

        var updated = createExisting(request.Name, endpoint, members, existing);
        var saved = await repository.UpdateAsync(updated, cancellationToken);

        // A no-op PUT that leaves the name unchanged records nothing.
        if (!string.Equals(priorName, saved.Name, StringComparison.Ordinal))
            audit.LogAudit(
                AuditAction.ProjectRenamed, nameof(IProject), id, saved.Name, projectId: id,
                details: JsonSerializer.Serialize(new { from = priorName, to = saved.Name }));

        return ToDto(saved);
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var project = await repository.FindAsync(id, cancellationToken);
        if (project is null)
            return NotFound();

        // Every project carries a built-in Tracey system agent, auto-provisioned on creation. It is
        // internal plumbing, not user data, so it must not block deletion — remove it first. Any
        // user-created agents, however, DO block deletion (the Agent→Project FK is Restrict by
        // design): refuse with a clear 409 rather than letting the FK surface as a 500.
        var agents = await agentRepository.GetByProjectAsync(id, cancellationToken);
        if (agents.Any(a => !a.IsSystemAgent))
            return Conflict(new { error = "This project still has agents. Delete its agents before deleting the project." });

        foreach (var systemAgent in agents.Where(a => a.IsSystemAgent))
            await agentRepository.RemoveAsync(systemAgent.Id, cancellationToken);

        try
        {
            var removed = await repository.RemoveAsync(id, cancellationToken);
            if (!removed)
                return NotFound();

            audit.LogAudit(AuditAction.ProjectDeleted, nameof(IProject), id, project.Name, projectId: id);
            return NoContent();
        }
        catch (DbUpdateException)
        {
            // Some other Restrict FK still references the project (e.g. issued API keys). Surface a
            // clear 409 instead of a 500.
            return Conflict(new { error = "This project still has related data (such as API keys). Remove it before deleting the project." });
        }
    }

    [HttpGet("{id:guid}/members")]
    public async Task<ActionResult<IReadOnlyList<ProjectMemberDto>>> GetMembers(
        Guid id,
        CancellationToken cancellationToken)
    {
        var project = await repository.FindAsync(id, cancellationToken);
        if (project is null)
            return NotFound();
        // Members' emails are PII — only an admin or a member of the project may list them.
        if (!await CanAccessAsync(project, cancellationToken))
            return NotFound();
        return project.Members.Select(ProjectDtoMapper.ToMemberDto).ToArray();
    }

    [HttpPost("{id:guid}/members/{userId:guid}")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<ActionResult<ProjectDto>> AddMember(
        Guid id,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var project = await repository.FindAsync(id, cancellationToken);
        if (project is null)
            return NotFound();
        var user = await userRepository.FindAsync(userId, cancellationToken);
        if (user is null)
            return BadRequest($"User {userId} not found.");

        if (project.Members.Any(m => m.Id == userId))
            return ToDto(project);

        var members = project.Members.Append(user).ToArray();
        var updated = createExisting(project.Name, project.SystemEndpoint, members, project);
        var saved = await repository.UpdateAsync(updated, cancellationToken);
        audit.LogAudit(AuditAction.ProjectMemberAdded, nameof(IUser), userId, user.Email, projectId: id);
        return ToDto(saved);
    }

    [HttpDelete("{id:guid}/members/{userId:guid}")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<ActionResult<ProjectDto>> RemoveMember(
        Guid id,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var project = await repository.FindAsync(id, cancellationToken);
        if (project is null)
            return NotFound();

        var member = project.Members.FirstOrDefault(m => m.Id == userId);
        if (member is null)
            return ToDto(project);

        var members = project.Members.Where(m => m.Id != userId).ToArray();
        var updated = createExisting(project.Name, project.SystemEndpoint, members, project);
        var saved = await repository.UpdateAsync(updated, cancellationToken);
        audit.LogAudit(AuditAction.ProjectMemberRemoved, nameof(IUser), userId, member.Email, projectId: id);
        return ToDto(saved);
    }

    // Admins can access any project; everyone else only the projects they belong to. The project is
    // already loaded with its Members, so membership is checked in memory without an extra query.
    private async Task<bool> CanAccessAsync(IProject project, CancellationToken cancellationToken)
    {
        if (User.IsInRole(nameof(UserRole.Admin)))
            return true;
        var user = await currentUser.GetCurrentUserAsync(cancellationToken);
        return user is not null && project.Members.Any(m => m.Id == user.Id);
    }

    private async Task<IReadOnlyCollection<IUser>?> ResolveMembersAsync(
        IReadOnlyList<Guid>? memberIds,
        CancellationToken cancellationToken)
    {
        if (memberIds is null || memberIds.Count == 0)
            return [];

        var distinct = memberIds.Distinct().ToArray();
        foreach (var userId in distinct)
        {
            if (!await userRepository.ContainsAsync(userId, cancellationToken))
                return null;
        }
        return await userRepository.GetManyAsync(distinct, cancellationToken);
    }

    private static ProjectDto ToDto(IProject p) => ProjectDtoMapper.ToDto(p);
}
