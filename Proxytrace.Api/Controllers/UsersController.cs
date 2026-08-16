using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Proxytrace.Api.Dto.Users;
using Proxytrace.Application.Auth;
using Proxytrace.Application.Auth.Local;
using Proxytrace.Domain;
using Proxytrace.Domain.AuditLog;
using Proxytrace.Domain.Notification;
using Nordstein.Core.Domain.Paging;
using Proxytrace.Domain.Project;
using Proxytrace.Domain.User;
using Proxytrace.Domain.UserTotpEnrollment;

namespace Proxytrace.Api.Controllers;

/// <summary>
/// API controller for users operations.
/// </summary>
[ApiController]
[Authorize]
[Route("api/users")]
public class UsersController : ControllerBase
{
    private readonly IRepository<IUser> repository;
    private readonly IProjectRepository projects;
    private readonly IUserAdministrationService administration;
    private readonly ICurrentUserAccessor currentUser;
    private readonly IPasswordResetService passwordReset;
    private readonly IMfaService mfa;
    private readonly IUserTotpEnrollmentRepository totpEnrollments;
    private readonly IConfiguration config;
    private readonly ILogger<Audit> audit;

    /// <summary>
    /// Initializes a new instance of the <see cref="UsersController"/> class.
    /// </summary>
    public UsersController(
        IRepository<IUser> repository,
        IProjectRepository projects,
        IUserAdministrationService administration,
        ICurrentUserAccessor currentUser,
        IPasswordResetService passwordReset,
        IMfaService mfa,
        IUserTotpEnrollmentRepository totpEnrollments,
        IConfiguration config,
        ILogger<Audit> audit)
    {
        this.repository = repository;
        this.projects = projects;
        this.administration = administration;
        this.currentUser = currentUser;
        this.passwordReset = passwordReset;
        this.mfa = mfa;
        this.totpEnrollments = totpEnrollments;
        this.config = config;
        this.audit = audit;
    }

    /// <summary>
    /// Gets the all.
    /// </summary>
    [HttpGet]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<PagedResult<UserDto>> GetAll(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var paged = await repository.GetPagedAsync(page, pageSize, cancellationToken);
        var mfaUsers = (await totpEnrollments.ListConfirmedUserIdsAsync(cancellationToken)).ToHashSet();
        return paged.Map(u => ToDto(u, mfaUsers.Contains(u.Id)));
    }

    /// <summary>
    /// Me.
    /// </summary>
    [HttpGet("me")]
    public async Task<ActionResult<UserDto>> Me(CancellationToken cancellationToken)
    {
        var user = await currentUser.GetCurrentUserAsync(cancellationToken);
        if (user is null) return Unauthorized();
        return ToDto(user, await mfa.IsEnabledAsync(user.Id, cancellationToken));
    }

    /// <summary>
    /// Self-service: the current user changes their own UI language. Any authenticated user may
    /// call this (unlike the admin-only role endpoint).
    /// </summary>
    [HttpPatch("me")]
    public async Task<IActionResult> UpdateMyLanguage(
        [FromBody] UpdateMyLanguageRequest request,
        CancellationToken cancellationToken)
    {
        if (!SupportedLanguages.IsSupported(request.Language))
            return BadRequest($"Unsupported language '{request.Language}'.");

        var user = await currentUser.GetCurrentUserAsync(cancellationToken);
        if (user is null)
            return Unauthorized();

        await user.ChangeLanguage(request.Language, cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// Self-service: the current user changes their own email notification preferences. Any
    /// authenticated user may call this (unlike the admin-only settings endpoint).
    /// </summary>
    [HttpPatch("me/email-notifications")]
    public async Task<IActionResult> UpdateMyEmailNotifications(
        [FromBody] UpdateMyEmailNotificationsRequest request,
        CancellationToken cancellationToken)
    {
        var user = await currentUser.GetCurrentUserAsync(cancellationToken);
        if (user is null)
            return Unauthorized();

        await user.ChangeEmailNotificationPreferences(request.Enabled, request.MinSeverity, cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// Gets.
    /// </summary>
    [HttpGet("{id:guid}")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<ActionResult<UserDto>> Get(Guid id, CancellationToken cancellationToken)
    {
        var user = await repository.FindAsync(id, cancellationToken);
        if (user is null)
            return NotFound();
        return ToDto(user, await mfa.IsEnabledAsync(user.Id, cancellationToken));
    }

    /// <summary>
    /// Gets the projects.
    /// </summary>
    [HttpGet("{id:guid}/projects")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<ActionResult<IReadOnlyList<UserProjectDto>>> GetProjects(
        Guid id,
        CancellationToken cancellationToken)
    {
        if (!await repository.ContainsAsync(id, cancellationToken))
            return NotFound();
        var memberships = await projects.GetByMemberAsync(id, cancellationToken);
        return memberships.Select(p => new UserProjectDto(p.Id, p.Name)).ToArray();
    }

    /// <summary>
    /// Updates the role.
    /// </summary>
    [HttpPut("{id:guid}/role")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<ActionResult<UserDto>> UpdateRole(
        Guid id,
        [FromBody] UpdateUserRoleRequest request,
        CancellationToken cancellationToken)
    {
        var actingUser = await currentUser.GetCurrentUserAsync(cancellationToken);
        if (actingUser is null)
            return Unauthorized();
        var updated = await administration.ChangeRoleAsync(actingUser.Id, id, request.Role, cancellationToken);
        if (updated is null)
            return NotFound();
        audit.LogAudit(AuditAction.UserRoleChanged, nameof(IUser), id, updated.Email);
        return ToDto(updated, await mfa.IsEnabledAsync(updated.Id, cancellationToken));
    }

    /// <summary>
    /// Deletes.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var actingUser = await currentUser.GetCurrentUserAsync(cancellationToken);
        if (actingUser is null)
            return Unauthorized();
        var target = await repository.FindAsync(id, cancellationToken);
        if (target is null)
            return NotFound();
        var removed = await administration.RemoveAsync(actingUser.Id, id, cancellationToken);
        if (!removed)
            return NotFound();
        audit.LogAudit(AuditAction.UserDeleted, nameof(IUser), id, target.Email);
        return NoContent();
    }

    /// <summary>
    /// Admin-initiated password reset: mints a one-time reset link for the user and returns it once.
    /// Lets an admin recover a locked-out user out-of-band when self-service email is unavailable. The
    /// link is never emailed from here — the admin relays it however they choose.
    /// </summary>
    [HttpPost("{id:guid}/reset-link")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<ActionResult<ResetLinkResponse>> CreateResetLink(Guid id, CancellationToken cancellationToken)
    {
        var target = await repository.FindAsync(id, cancellationToken);
        if (target is null)
            return NotFound();

        var link = await passwordReset.IssueResetLinkAsync(id, BuildResetUrl, cancellationToken);
        if (link is null)
            return NotFound();

        audit.LogAudit(AuditAction.PasswordResetLinkIssued, nameof(IUser), id, target.Email);
        return new ResetLinkResponse(link.Link, link.ExpiresAt);
    }

    private string BuildResetUrl(string token)
    {
        // Fall back to the configured frontend origin (the browser-facing URL, set per environment)
        // before the request host — Request.Host is the backend's own port, which is not where the
        // user opens the reset link.
        var baseUrl = config["Frontend:BaseUrl"] ?? config["Frontend:AllowedOrigin"] ?? $"{Request.Scheme}://{Request.Host}";
        return $"{baseUrl.TrimEnd('/')}/reset-password?token={Uri.EscapeDataString(token)}";
    }

    /// <summary>
    /// Admin lockout recovery: turns off a user's two-factor authentication (removes the enrollment and
    /// all backup codes). The only escape when a user has lost both their authenticator and backup
    /// codes. Idempotent — succeeds whether or not the user currently had MFA.
    /// </summary>
    [HttpPost("{id:guid}/mfa/disable")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<IActionResult> DisableMfa(Guid id, CancellationToken cancellationToken)
    {
        var target = await repository.FindAsync(id, cancellationToken);
        if (target is null)
            return NotFound();

        var removed = await mfa.AdminDisableAsync(id, cancellationToken);
        if (removed)
            audit.LogAudit(AuditAction.MfaDisabled, nameof(IUser), id, target.Email);
        return NoContent();
    }

    private static UserDto ToDto(IUser u, bool mfaEnabled) =>
        new(u.Id, u.Email, u.Role, u.ExternalSubject is not null, u.CreatedAt, u.UpdatedAt, mfaEnabled);
}
