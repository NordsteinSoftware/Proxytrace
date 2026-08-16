using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Proxytrace.Api.Auth.Licensing;
using Proxytrace.Api.Dto.Tracey;
using Proxytrace.Application.Auth;
using Proxytrace.Application.Tracey;
using Proxytrace.Domain;
using Proxytrace.Domain.Project;
using Proxytrace.Domain.User;
using Proxytrace.Licensing;

namespace Proxytrace.Api.Controllers;

/// <summary>
/// API controller for tracey operations.
/// </summary>
[ApiController]
[Authorize]
[RequiresFeature(LicenseFeature.Tracey)]
[Route("api/tracey")]
public class TraceyController : ControllerBase
{
    private readonly ITraceySessionService sessionService;
    private readonly IRepository<IProject> projects;
    private readonly ICurrentUserAccessor currentUser;

    /// <summary>
    /// Initializes a new instance of the <see cref="TraceyController"/> class.
    /// </summary>
    public TraceyController(
        ITraceySessionService sessionService,
        IRepository<IProject> projects,
        ICurrentUserAccessor currentUser)
    {
        this.sessionService = sessionService;
        this.projects = projects;
        this.currentUser = currentUser;
    }

    /// <summary>
    /// Resolves the Tracey browser session (model + agent id) for the given (or first) project the
    /// caller belongs to.
    /// </summary>
    [HttpGet("session")]
    public async Task<ActionResult<TraceySessionDto>> GetSession(
        [FromQuery] Guid? projectId = null,
        CancellationToken cancellationToken = default)
    {
        IProject? project = projectId.HasValue
            ? await projects.FindAsync(projectId.Value, cancellationToken)
            : await projects.FindFirstAsync(cancellationToken);
        if (project is null)
        {
            return NotFound();
        }

        // The session exposes a chat channel backed by the project provider's credential, so the
        // caller must belong to the project (admins may reach any project).
        var user = await currentUser.GetCurrentUserAsync(cancellationToken);
        if (user is null || !IsMember(user, project))
        {
            return Forbid();
        }

        var session = await sessionService.CreateSessionAsync(project, cancellationToken);
        return new TraceySessionDto(session.Model, session.AgentId);
    }

    private static bool IsMember(IUser user, IProject project)
        => user.Role == UserRole.Admin || project.Members.Any(m => m.Id == user.Id);
}
