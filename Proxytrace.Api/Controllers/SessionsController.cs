using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Proxytrace.Api.Auth;
using Proxytrace.Api.Dto.Sessions;
using Nordstein.Core.Domain.Paging;
using Proxytrace.Domain.Session;
using ISession = Proxytrace.Domain.Session.ISession;

namespace Proxytrace.Api.Controllers;

/// <summary>
/// API controller for sessions operations.
/// </summary>
[ApiController]
[Authorize]
[Route("api/sessions")]
public class SessionsController : ControllerBase
{
    private readonly ISessionRepository repository;
    private readonly IProjectAccessGuard accessGuard;

    /// <summary>
    /// Initializes a new instance of the <see cref="SessionsController"/> class.
    /// </summary>
    public SessionsController(ISessionRepository repository, IProjectAccessGuard accessGuard)
    {
        this.repository = repository;
        this.accessGuard = accessGuard;
    }

    /// <summary>
    /// Gets the all.
    /// </summary>
    [HttpGet]
    public async Task<PagedResult<SessionDto>> GetAll(
        [FromQuery] Guid projectId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        (page, pageSize) = Paging.Clamp(page, pageSize);
        // Admins (accessible == null) may query any project; non-admins must scope to a project they
        // belong to, otherwise the list returns nothing rather than leaking another tenant's sessions.
        var accessible = await accessGuard.GetAccessibleProjectIdsAsync(cancellationToken);
        if (accessible is not null && !accessible.Contains(projectId))
            return new PagedResult<SessionDto>([], 0, page, pageSize);

        var (items, total) = await repository.GetRecentAsync(projectId, page, pageSize, cancellationToken);
        return new PagedResult<ISession>(items, total, page, pageSize).Map(SessionDto.From);
    }

    /// <summary>
    /// Gets.
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<SessionDto>> Get(Guid id, CancellationToken cancellationToken)
    {
        var session = await repository.FindAsync(id, cancellationToken);
        // Hide other tenants' sessions behind a 404 rather than disclosing their counters/key.
        if (session is null || !await accessGuard.CanAccessProjectAsync(session.ProjectId, cancellationToken))
            return NotFound();
        return SessionDto.From(session);
    }
}
