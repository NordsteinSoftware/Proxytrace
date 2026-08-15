using Proxytrace.Domain.Notifications;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Logging;
using Proxytrace.Api.Auth;
using Proxytrace.Api.Dto.Auth;
using Proxytrace.Application.Auth;
using Proxytrace.Application.Auth.Local;
using Proxytrace.Application.Setup;
using Proxytrace.Domain.AuditLog;
using Proxytrace.Domain.Invite;
using Proxytrace.Domain.User;

namespace Proxytrace.Api.Controllers;

/// <summary>
/// API controller for auth operations.
/// </summary>
[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly AuthOptions options;
    private readonly ISetupService setup;
    private readonly ILoginService login;
    private readonly ILegacyClaimService legacyClaim;
    private readonly IInviteService invites;
    private readonly IInviteRepository inviteRepo;
    private readonly IPasswordResetService passwordReset;
    private readonly IMfaService mfa;
    private readonly IPasswordPolicy policy;
    private readonly ICurrentUserAccessor currentUser;
    private readonly IStreamTicketService streamTickets;
    private readonly IConfiguration config;
    private readonly ILogger<Audit> audit;
    private readonly IEmailSettingsStore emailSettings;
    private readonly ISessionCookie sessionCookie;

    /// <summary>
    /// Initializes a new instance of the <see cref="AuthController"/> class.
    /// </summary>
    public AuthController(
        AuthOptions options,
        ISetupService setup,
        ILoginService login,
        ILegacyClaimService legacyClaim,
        IInviteService invites,
        IInviteRepository inviteRepo,
        IPasswordResetService passwordReset,
        IMfaService mfa,
        IPasswordPolicy policy,
        ICurrentUserAccessor currentUser,
        IStreamTicketService streamTickets,
        IConfiguration config,
        ILogger<Audit> audit,
        IEmailSettingsStore emailSettings,
        ISessionCookie sessionCookie)
    {
        this.options = options;
        this.setup = setup;
        this.login = login;
        this.legacyClaim = legacyClaim;
        this.invites = invites;
        this.inviteRepo = inviteRepo;
        this.passwordReset = passwordReset;
        this.mfa = mfa;
        this.policy = policy;
        this.currentUser = currentUser;
        this.streamTickets = streamTickets;
        this.config = config;
        this.audit = audit;
        this.emailSettings = emailSettings;
        this.sessionCookie = sessionCookie;
    }

    /// <summary>
    /// Gets the mode.
    /// </summary>
    [HttpGet("mode")]
    [AllowAnonymous]
    public async Task<AuthModeDto> GetMode(CancellationToken ct)
    {
        var isLocal = options.Mode == AuthMode.Local;
        var setupRequired = isLocal && !await setup.AnyUsersExistAsync(ct);
        var legacyClaimAvailable = isLocal && !setupRequired && await legacyClaim.IsClaimAvailableAsync(ct);
        return new AuthModeDto(isLocal ? "local" : "oidc", setupRequired, legacyClaimAvailable);
    }

    /// <summary>
    /// Claim legacy.
    /// </summary>
    [HttpPost("claim-legacy")]
    [AllowAnonymous]
    [RequireLocalMode]
    [EnableRateLimiting(AuthRateLimiterConfigurator.LoginPolicy)]
    public async Task<ActionResult<TokenResponse>> ClaimLegacy([FromBody] ClaimLegacyRequest req, CancellationToken ct)
    {
        var v = policy.Validate(req.Password);
        if (!v.IsValid) return BadRequest(v.Errors);

        var result = await legacyClaim.ClaimAsync(req.Email, req.Password, ct);
        if (result is null) return Conflict("No eligible legacy account.");
        sessionCookie.Append(Response, result.Token, result.ExpiresAt);
        audit.LogAudit(AuditAction.LegacyAccountClaimed, nameof(IUser), result.User.Id, result.User.Email);
        return new TokenResponse(result.Token, result.ExpiresAt);
    }

    /// <summary>
    /// Setup.
    /// </summary>
    [HttpPost("setup")]
    [AllowAnonymous]
    [RequireLocalMode]
    public async Task<ActionResult<TokenResponse>> Setup([FromBody] SetupAdminRequest req, CancellationToken ct)
    {
        if (await setup.AnyUsersExistAsync(ct))
            return Conflict("Setup already completed.");
        var v = policy.Validate(req.Password);
        if (!v.IsValid) return BadRequest(v.Errors);

        var result = await setup.CreateFirstAdminAsync(req.Email, req.Password, ct);
        sessionCookie.Append(Response, result.Token, result.ExpiresAt);
        audit.LogAudit(AuditAction.AdminBootstrapped, nameof(IUser), result.UserId, req.Email);
        return new TokenResponse(result.Token, result.ExpiresAt);
    }

    // Issues a short-lived, single-use token for SSE connections, so the long-lived
    // session JWT never has to ride in the EventSource query string (where it would
    // leak via browser history / Referer / proxy logs). Validate this ticket in
    // JwtBearerEventsFactory.OnMessageReceived alongside the existing access_token path.
    /// <summary>
    /// Stream ticket.
    /// </summary>
    [HttpGet("stream-ticket")]
    [Authorize]
    public async Task<ActionResult<StreamTicketResponse>> StreamTicket(CancellationToken ct)
    {
        var me = await currentUser.GetCurrentUserAsync(ct);
        if (me is null) return Unauthorized();

        var ticket = streamTickets.Issue(me);
        return new StreamTicketResponse(ticket.Token, ticket.ExpiresAt);
    }

    // Rate-limited per client IP: there is no per-account failed-attempt counter, so without this
    // password guessing against a known address is bounded only by request throughput.
    /// <summary>
    /// Login.
    /// </summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [RequireLocalMode]
    [EnableRateLimiting(AuthRateLimiterConfigurator.LoginPolicy)]
    public async Task<ActionResult<LoginResponseDto>> Login([FromBody] LoginRequest req, CancellationToken ct)
    {
        var outcome = await login.LoginAsync(req.Email, req.Password, ct);
        if (outcome is null)
        {
            // No user context on a failed login (pre-auth, anonymous) — recorded as the System actor
            // with the submitted email as the target so brute-force/credential-stuffing is visible.
            audit.LogAudit(AuditAction.LoginFailed, nameof(IUser), targetLabel: req.Email, outcome: AuditOutcome.Failure);
            return Unauthorized();
        }

        // On a confirmed-MFA account no session is issued yet; the UserLoggedIn audit fires once the
        // second factor is verified (mfa/verify). A non-MFA login is audited here.
        if (outcome is LoginSucceeded s)
        {
            audit.LogAudit(AuditAction.UserLoggedIn, nameof(IUser), s.User.Id, s.User.Email);
        }
        return IssueLoginResponse(outcome);
    }

    // Sets the session cookie when a session was issued and shapes the response either way. Never
    // audits — callers do that with the right action (login vs. reset vs. signup).
    private LoginResponseDto IssueLoginResponse(LoginOutcome outcome)
    {
        if (outcome is LoginSucceeded s)
        {
            sessionCookie.Append(Response, s.Token, s.ExpiresAt);
            return new LoginResponseDto(s.Token, s.ExpiresAt, MfaRequired: false, null, null);
        }

        var mfaRequired = (MfaRequired)outcome;
        return new LoginResponseDto(null, null, MfaRequired: true, mfaRequired.ChallengeToken, mfaRequired.ChallengeExpiresAt);
    }

    // Completes the second step of login: verifies a TOTP code (or backup code) against the challenge
    // ticket and, on success, issues the session. Rate-limited because the TOTP code space is small.
    /// <summary>
    /// Mfa verify.
    /// </summary>
    [HttpPost("mfa/verify")]
    [AllowAnonymous]
    [RequireLocalMode]
    [EnableRateLimiting(AuthRateLimiterConfigurator.MfaPolicy)]
    public async Task<ActionResult<LoginResponseDto>> MfaVerify([FromBody] MfaVerifyRequest req, CancellationToken ct)
    {
        var result = await mfa.VerifyChallengeAsync(req.ChallengeToken, req.Code, ct);
        if (result is null)
        {
            audit.LogAudit(AuditAction.MfaChallengeFailed, nameof(IUser), outcome: AuditOutcome.Failure);
            return Unauthorized();
        }

        sessionCookie.Append(Response, result.Token, result.ExpiresAt);
        audit.LogAudit(AuditAction.UserLoggedIn, nameof(IUser), result.User.Id, result.User.Email);
        return new LoginResponseDto(result.Token, result.ExpiresAt, MfaRequired: false, null, null);
    }

    // Starts TOTP enrollment: returns a fresh secret + otpauth URI for the caller to add to their
    // authenticator app. The enrollment is pending until confirmed via mfa/activate.
    /// <summary>
    /// Mfa setup.
    /// </summary>
    [HttpPost("mfa/setup")]
    [Authorize]
    [RequireLocalMode]
    public async Task<ActionResult<MfaSetupResponse>> MfaSetup(CancellationToken ct)
    {
        var me = await currentUser.GetCurrentUserAsync(ct);
        if (me is null) return Unauthorized();

        var setupResult = await mfa.SetupAsync(me, ct);
        if (setupResult is null) return Conflict("MFA is already enabled. Disable it before setting it up again.");
        return new MfaSetupResponse(setupResult.Secret, setupResult.OtpAuthUri);
    }

    // Confirms enrollment with a first code, turning MFA on and returning one-time backup codes (shown once).
    /// <summary>
    /// Mfa activate.
    /// </summary>
    [HttpPost("mfa/activate")]
    [Authorize]
    [RequireLocalMode]
    public async Task<ActionResult<MfaActivateResponse>> MfaActivate([FromBody] MfaActivateRequest req, CancellationToken ct)
    {
        var me = await currentUser.GetCurrentUserAsync(ct);
        if (me is null) return Unauthorized();

        var codes = await mfa.ActivateAsync(me, req.Code, ct);
        if (codes is null) return BadRequest("Invalid code. Make sure your authenticator app is set up and try again.");

        audit.LogAudit(AuditAction.MfaEnabled, nameof(IUser), me.Id, me.Email);
        return new MfaActivateResponse(codes);
    }

    // Self-service disable: requires the account password as re-authentication.
    /// <summary>
    /// Mfa disable.
    /// </summary>
    [HttpPost("mfa/disable")]
    [Authorize]
    [RequireLocalMode]
    public async Task<IActionResult> MfaDisable([FromBody] MfaDisableRequest req, CancellationToken ct)
    {
        var me = await currentUser.GetCurrentUserAsync(ct);
        if (me is null) return Unauthorized();

        var result = await mfa.DisableAsync(me, req.Password, ct);
        if (result is null) return BadRequest("Incorrect password.");
        if (result is true) audit.LogAudit(AuditAction.MfaDisabled, nameof(IUser), me.Id, me.Email);
        return NoContent();
    }

    // The session rides in an httpOnly cookie (see SessionCookie), so the SPA cannot read
    // or clear it itself — logout clears it server-side. Anonymous and idempotent.
    /// <summary>
    /// Logout.
    /// </summary>
    [HttpPost("logout")]
    [AllowAnonymous]
    [RequireLocalMode]
    public IActionResult Logout()
    {
        // Only audit a logout that actually ended a session — a spurious anonymous call records
        // nothing. The actor (who logged out) is enriched from the session cookie's context.
        var hadSession = Request.Cookies.ContainsKey(SessionCookie.Name);
        sessionCookie.Delete(Response);
        if (hadSession)
            audit.LogAudit(AuditAction.UserLoggedOut, nameof(IUser));
        return NoContent();
    }

    // Session restore for the SPA: identifies the caller from the httpOnly session cookie
    // (or bearer token), since the client cannot decode the cookie itself.
    /// <summary>
    /// Me.
    /// </summary>
    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<MeDto>> Me(CancellationToken ct)
    {
        var me = await currentUser.GetCurrentUserAsync(ct);
        if (me is null) return Unauthorized();
        var settings = await emailSettings.GetAsync(ct);
        var mfaEnabled = await mfa.IsEnabledAsync(me.Id, ct);
        return new MeDto(
            me.Id, me.Email, me.Role, me.Language,
            me.EmailNotificationsEnabled, me.EmailNotificationMinSeverity,
            EmailEnabled: settings?.Enabled ?? false,
            MfaEnabled: mfaEnabled);
    }

    /// <summary>
    /// Signup.
    /// </summary>
    [HttpPost("signup")]
    [AllowAnonymous]
    [RequireLocalMode]
    [EnableRateLimiting(AuthRateLimiterConfigurator.LoginPolicy)]
    public async Task<ActionResult<TokenResponse>> Signup([FromBody] SignupRequest req, CancellationToken ct)
    {
        var v = policy.Validate(req.Password);
        if (!v.IsValid) return BadRequest(v.Errors);

        var user = await invites.ConsumeAsync(req.Token, req.Password, ct);
        if (user is null) return StatusCode(410, "Invite invalid, expired, or already used.");
        audit.LogAudit(AuditAction.UserSignedUp, nameof(IUser), user.Id, user.Email);

        // A freshly created user has no MFA enrollment, so the password login always yields a session.
        if (await login.LoginAsync(user.Email, req.Password, ct) is not LoginSucceeded session) return NotFound();
        sessionCookie.Append(Response, session.Token, session.ExpiresAt);
        return new TokenResponse(session.Token, session.ExpiresAt);
    }

    // Self-service password reset. Always responds the same way whether or not the email maps to an
    // account, so the response never reveals which addresses are registered. When SMTP is configured
    // the reset link is emailed; otherwise it is written to the server log for the operator to relay
    // (the only escape from a sole-admin lockout). Rate-limited to blunt enumeration/abuse.
    /// <summary>
    /// Forgot password.
    /// </summary>
    [HttpPost("forgot-password")]
    [AllowAnonymous]
    [RequireLocalMode]
    [EnableRateLimiting(AuthRateLimiterConfigurator.PasswordResetPolicy)]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest req, CancellationToken ct)
    {
        await passwordReset.RequestResetAsync(req.Email, BuildResetUrl, ct);
        audit.LogAudit(AuditAction.PasswordResetRequested, nameof(IUser), targetLabel: req.Email);
        return Accepted();
    }

    /// <summary>
    /// Reset password.
    /// </summary>
    [HttpPost("reset-password")]
    [AllowAnonymous]
    [RequireLocalMode]
    [EnableRateLimiting(AuthRateLimiterConfigurator.PasswordResetPolicy)]
    public async Task<ActionResult<LoginResponseDto>> ResetPassword([FromBody] ResetPasswordRequest req, CancellationToken ct)
    {
        var v = policy.Validate(req.Password);
        if (!v.IsValid) return BadRequest(v.Errors);

        var outcome = await passwordReset.CompleteResetAsync(req.Token, req.Password, ct);
        if (outcome is null) return StatusCode(410, "Reset link invalid, expired, or already used.");

        // The password was changed regardless of the second-factor outcome — audit the completion with
        // the user from whichever branch we got. The session is only issued here when MFA is not
        // enabled; an MFA account must still pass the challenge (mirrors the login flow).
        var user = outcome switch
        {
            LoginSucceeded s => s.User,
            MfaRequired m => m.User,
            _ => throw new InvalidOperationException("Unknown reset outcome."),
        };
        audit.LogAudit(AuditAction.PasswordResetCompleted, nameof(IUser), user.Id, user.Email);
        return IssueLoginResponse(outcome);
    }

    private string BuildResetUrl(string token)
    {
        // Fall back to the configured frontend origin (the browser-facing URL, set per environment)
        // before the request host — Request.Host is the backend's own port, which is not where the
        // user opens the emailed link.
        var baseUrl = config["Frontend:BaseUrl"] ?? config["Frontend:AllowedOrigin"] ?? $"{Request.Scheme}://{Request.Host}";
        return $"{baseUrl.TrimEnd('/')}/reset-password?token={Uri.EscapeDataString(token)}";
    }

    // Anonymous lookup of an invite by its raw token — rate-limited so the token space cannot be
    // swept, sharing the login bucket because both are anonymous credential guesses.
    /// <summary>
    /// Preview.
    /// </summary>
    [HttpGet("invites/by-token/{token}")]
    [AllowAnonymous]
    [RequireLocalMode]
    [EnableRateLimiting(AuthRateLimiterConfigurator.LoginPolicy)]
    public async Task<ActionResult<InvitePreviewDto>> Preview(string token, CancellationToken ct)
    {
        var invite = await invites.GetByTokenAsync(token, ct);
        if (invite is null) return StatusCode(410);
        return new InvitePreviewDto(invite.Email, invite.Role, invite.ExpiresAt);
    }

    /// <summary>
    /// Creates.
    /// </summary>
    [Authorize(Roles = nameof(UserRole.Admin))]
    [RequireLocalMode]
    [HttpPost("invites")]
    public async Task<ActionResult<CreateInviteResponse>> Create([FromBody] CreateInviteRequest req, CancellationToken ct)
    {
        var me = await currentUser.GetCurrentUserAsync(ct);
        if (me is null) return Unauthorized();

        var created = await invites.CreateAsync(req.Email, req.Role, me, ct);
        audit.LogAudit(
            AuditAction.UserInvited, nameof(IInvite), created.Invite.Id, req.Email,
            details: JsonSerializer.Serialize(new { role = req.Role.ToString() }));
        // The raw token is available only here; the list endpoint can no longer rebuild the link.
        return new CreateInviteResponse(created.RawToken, BuildInviteUrl(created.RawToken), created.Invite.ExpiresAt);
    }

    private string BuildInviteUrl(string token)
    {
        // Fall back to the configured frontend origin (the browser-facing URL, set per environment)
        // before the request host — Request.Host is the backend's own port, which is not where the
        // user opens the emailed link.
        var baseUrl = config["Frontend:BaseUrl"] ?? config["Frontend:AllowedOrigin"] ?? $"{Request.Scheme}://{Request.Host}";
        return $"{baseUrl.TrimEnd('/')}/signup?token={Uri.EscapeDataString(token)}";
    }

    /// <summary>
    /// Lists.
    /// </summary>
    [Authorize(Roles = nameof(UserRole.Admin))]
    [RequireLocalMode]
    [HttpGet("invites")]
    public async Task<IReadOnlyList<InviteDto>> List(CancellationToken ct)
    {
        var all = await inviteRepo.GetAllAsync(ct);
        // The token is hashed at rest, so the invite link cannot be reconstructed — the list shows
        // status only; the link is shown once when the invite is created.
        return all.Select(i => new InviteDto(i.Id, i.Email, i.Role, i.ExpiresAt, i.ConsumedAt)).ToArray();
    }

    /// <summary>
    /// Deletes.
    /// </summary>
    [Authorize(Roles = nameof(UserRole.Admin))]
    [RequireLocalMode]
    [HttpDelete("invites/{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var invite = await inviteRepo.FindAsync(id, ct);
        if (invite is null)
            return NotFound();
        if (!await inviteRepo.RemoveAsync(id, ct))
            return NotFound();
        audit.LogAudit(AuditAction.InviteRevoked, nameof(IInvite), id, invite.Email);
        return NoContent();
    }
}
