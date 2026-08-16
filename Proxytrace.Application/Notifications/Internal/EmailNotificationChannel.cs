using Proxytrace.Domain.Notifications;
using System.Net;
using Microsoft.Extensions.Logging;
using Proxytrace.Domain;
using Proxytrace.Domain.Notification;
using Proxytrace.Domain.Project;
using Proxytrace.Domain.User;

namespace Proxytrace.Application.Notifications.Internal;

/// <summary>
/// Emails a notification to its recipients. Recipients are a project's members (project-scoped
/// notifications) or all users (global), filtered to those who have email enabled and whose
/// per-user severity threshold the notification clears. Gated by the operator <see cref="EmailSettings"/>
/// (enabled + a floor severity). One message per recipient; a single failure is logged and skipped.
/// </summary>
internal sealed class EmailNotificationChannel : INotificationChannel
{
    private readonly IEmailSettingsStore settingsStore;
    private readonly IEmailSender sender;
    private readonly IProjectRepository projects;
    private readonly IRepository<IUser> users;
    private readonly ILogger<EmailNotificationChannel> logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="EmailNotificationChannel"/> class.
    /// </summary>
    public EmailNotificationChannel(
        IEmailSettingsStore settingsStore,
        IEmailSender sender,
        IProjectRepository projects,
        IRepository<IUser> users,
        ILogger<EmailNotificationChannel> logger)
    {
        this.settingsStore = settingsStore;
        this.sender = sender;
        this.projects = projects;
        this.users = users;
        this.logger = logger;
    }

    /// <summary>
    /// Gets the name.
    /// </summary>
    public string Name => "Email";

    /// <summary>
    /// Deliver asynchronously.
    /// </summary>
    public async Task DeliverAsync(INotification notification, CancellationToken cancellationToken = default)
    {
        EmailSettings? settings = await settingsStore.GetAsync(cancellationToken);
        if (settings is null || !settings.Enabled)
            return;

        if (notification.Severity < settings.MinSeverity)
            return;

        IReadOnlyCollection<IUser> candidates;
        if (notification.ProjectId is { } projectId)
        {
            var project = await projects.FindAsync(projectId, cancellationToken);
            if (project is null)
                return;
            candidates = project.Members;
        }
        else
        {
            candidates = await users.GetAllAsync(cancellationToken);
        }

        var recipients = candidates
            .Where(u => u.EmailNotificationsEnabled
                        && notification.Severity >= u.EmailNotificationMinSeverity
                        && !string.IsNullOrWhiteSpace(u.Email))
            .ToList();
        if (recipients.Count == 0)
            return;

        var subject = $"[{notification.Severity}] {notification.Title}";
        var link = BuildLink(settings.AppBaseUrl, notification.Id);
        var textBody = BuildText(notification, link);
        var htmlBody = BuildHtml(notification, link);

        foreach (var recipient in recipients)
        {
            try
            {
                await sender.SendAsync(
                    new EmailMessage(recipient.Email, recipient.Email, subject, htmlBody, textBody),
                    cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to email {Recipient} for '{Title}'", recipient.Email, notification.Title);
            }
        }
    }

    // Links to the notification itself rather than to its target: the notification is the only
    // record of an anomaly, it always exists (the target may have been deleted), and the app opens
    // its detail drawer from this stable route on any page.
    private static string? BuildLink(string? appBaseUrl, Guid notificationId)
        => string.IsNullOrWhiteSpace(appBaseUrl)
            ? null
            : $"{appBaseUrl.TrimEnd('/')}/notifications/{notificationId}";

    private static string BuildText(INotification notification, string? link)
    {
        var body = $"{notification.Title}\n\n{notification.Message}\n\nSeverity: {notification.Severity}";
        return link is null ? body : $"{body}\n\nView details: {link}";
    }

    private static string BuildHtml(INotification notification, string? link)
    {
        var title = WebUtility.HtmlEncode(notification.Title);
        var message = WebUtility.HtmlEncode(notification.Message);
        var severity = WebUtility.HtmlEncode(notification.Severity.ToString());
        var button = link is null
            ? string.Empty
            : $"<p><a href=\"{link}\">View details</a></p>";
        return $"<h2>{title}</h2><p>{message}</p><p><strong>Severity:</strong> {severity}</p>{button}";
    }
}
