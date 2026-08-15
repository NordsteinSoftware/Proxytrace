using Proxytrace.Domain.Notifications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Proxytrace.Api.Dto.EmailSettings;
using Proxytrace.Application.Auth;
using Proxytrace.Application.ErrorLog;
using Proxytrace.Application.Notifications;
using Proxytrace.Domain.AuditLog;
using Proxytrace.Domain.User;

namespace Proxytrace.Api.Controllers;

/// <summary>
/// API controller for email settings operations.
/// </summary>
[ApiController]
[Authorize(Roles = nameof(UserRole.Admin))]
[Route("api/email-settings")]
public class EmailSettingsController : ControllerBase
{
    private readonly IEmailSettingsStore store;
    private readonly IEmailSender sender;
    private readonly ICurrentUserAccessor currentUser;
    private readonly EmailSettingsDtoMapper mapper;
    private readonly ILogger<Audit> audit;
    private readonly ILogger<EmailSettingsController> logger;
    private readonly IWebHostEnvironment env;

    /// <summary>
    /// Initializes a new instance of the <see cref="EmailSettingsController"/> class.
    /// </summary>
    public EmailSettingsController(
        IEmailSettingsStore store,
        IEmailSender sender,
        ICurrentUserAccessor currentUser,
        EmailSettingsDtoMapper mapper,
        ILogger<Audit> audit,
        ILogger<EmailSettingsController> logger,
        IWebHostEnvironment env)
    {
        this.store = store;
        this.sender = sender;
        this.currentUser = currentUser;
        this.mapper = mapper;
        this.audit = audit;
        this.logger = logger;
        this.env = env;
    }

    /// <summary>
    /// Gets.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<EmailSettingsDto>> Get(CancellationToken cancellationToken)
    {
        var settings = await store.GetAsync(cancellationToken);
        return settings is null ? NoContent() : mapper.ToDto(settings);
    }

    /// <summary>
    /// Updates.
    /// </summary>
    [HttpPut]
    public async Task<ActionResult<EmailSettingsDto>> Update(
        [FromBody] UpdateEmailSettingsRequest request,
        CancellationToken cancellationToken)
    {
        var existing = await store.GetAsync(cancellationToken);
        var password = string.IsNullOrEmpty(request.Password) ? existing?.Password : request.Password;

        var settings = new EmailSettings(
            request.Enabled, request.SmtpHost, request.SmtpPort, request.Security,
            request.Username, password, request.FromAddress, request.FromName,
            request.AppBaseUrl, request.MinSeverity);

        await store.SaveAsync(settings, cancellationToken);
        audit.LogAudit(AuditAction.EmailSettingsUpdated, targetType: "EmailSettings", targetLabel: request.SmtpHost);
        return mapper.ToDto(settings);
    }

    /// <summary>
    /// Sends the test.
    /// </summary>
    [HttpPost("test")]
    public async Task<IActionResult> SendTest(CancellationToken cancellationToken)
    {
        var settings = await store.GetAsync(cancellationToken);
        if (settings is null)
            return BadRequest("Email is not configured.");

        var user = await currentUser.GetCurrentUserAsync(cancellationToken);
        if (user is null)
            return Unauthorized();

        try
        {
            await sender.SendAsync(
                new EmailMessage(
                    user.Email, user.Email,
                    "Proxytrace test email",
                    "<p>This is a test email from Proxytrace.</p>",
                    "This is a test email from Proxytrace."),
                cancellationToken);
            return NoContent();
        }
        catch (Exception ex)
        {
            // Mirror ExceptionHandlingMiddleware: capture under an error id and only echo the raw
            // message in Development. Outside Development it may carry SMTP host/credential or other
            // internal detail, so return a generic message carrying the error id for support.
            var errorId = Guid.NewGuid();
            using (logger.BeginScope(new Dictionary<string, object> { [ErrorLogScope.ErrorIdKey] = errorId }))
            {
                logger.LogError(ex, "Failed to send test email");
            }

            var detail = env.IsDevelopment()
                ? ex.Message
                : $"An unexpected error occurred. (Error ID: {errorId})";
            return BadRequest($"Failed to send test email: {detail}");
        }
    }
}
