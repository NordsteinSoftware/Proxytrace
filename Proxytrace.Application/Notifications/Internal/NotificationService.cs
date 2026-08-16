using Microsoft.Extensions.Logging;
using Proxytrace.Domain.Notification;

namespace Proxytrace.Application.Notifications.Internal;

internal sealed class NotificationService : INotificationService
{
    private readonly IEnumerable<INotificationChannel> channels;
    private readonly INotificationRepository notifications;
    private readonly INotification.CreateNew createNotification;
    private readonly ILogger<NotificationService> logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="NotificationService"/> class.
    /// </summary>
    public NotificationService(
        IEnumerable<INotificationChannel> channels,
        INotificationRepository notifications,
        INotification.CreateNew createNotification,
        ILogger<NotificationService> logger)
    {
        this.channels = channels;
        this.notifications = notifications;
        this.createNotification = createNotification;
        this.logger = logger;
    }

    /// <summary>
    /// Notifies asynchronously.
    /// </summary>
    public async Task NotifyAsync(NotificationRequest request, CancellationToken cancellationToken = default)
    {
        if (request.TargetKind is { } targetKind && request.TargetId is { } targetId)
        {
            var existing = await notifications.FindActiveByTargetAsync(targetKind, targetId, cancellationToken);
            if (existing is not null)
            {
                logger.LogDebug(
                    "Suppressing duplicate notification for {TargetKind} {TargetId}",
                    targetKind, targetId);
                return;
            }
        }

        // The notification *is* the record: it is created here, once, before any channel runs, so
        // every channel delivers the same persisted entity and can reference it by id.
        //
        // The persist is best-effort, exactly as it was when it lived inside the dashboard channel's
        // try/catch: a transient DB failure here must not throw out of NotifyAsync, which producers
        // (BlockedCallRecorder, AnomalyDetectionService's per-anomaly loop) call as an unguarded
        // trailing statement. A failed persist is logged and swallowed.
        INotification notification;
        try
        {
            notification = await notifications.AddAsync(
                createNotification(
                    request.Kind,
                    request.Severity,
                    request.Title,
                    request.Message,
                    request.ProjectId,
                    request.TargetKind,
                    request.TargetId),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to persist notification '{Title}'", request.Title);
            return;
        }

        foreach (var channel in channels)
        {
            try
            {
                await channel.DeliverAsync(notification, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // One channel failing must not block the others (e.g. email down shouldn't drop the
                // dashboard alert).
                logger.LogWarning(
                    ex,
                    "Notification channel {Channel} failed to deliver '{Title}'",
                    channel.Name,
                    notification.Title);
            }
        }
    }
}
