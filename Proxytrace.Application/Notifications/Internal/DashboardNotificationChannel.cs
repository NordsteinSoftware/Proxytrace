using Proxytrace.Application.Streaming;
using Proxytrace.Domain.Notification;

namespace Proxytrace.Application.Notifications.Internal;

/// <summary>
/// Pushes a live SSE event so open dashboards show the notification immediately. Persistence and
/// de-duplication both happen upstream in <see cref="NotificationService"/> before any channel is
/// invoked — this channel only broadcasts the already-stored entity.
/// </summary>
internal sealed class DashboardNotificationChannel : INotificationChannel
{
    private readonly INotificationBroadcaster broadcaster;

    /// <summary>
    /// Initializes a new instance of the <see cref="DashboardNotificationChannel"/> class.
    /// </summary>
    public DashboardNotificationChannel(INotificationBroadcaster broadcaster)
    {
        this.broadcaster = broadcaster;
    }

    /// <summary>
    /// Gets the name.
    /// </summary>
    public string Name => "Dashboard";

    /// <summary>
    /// Deliver asynchronously.
    /// </summary>
    public Task DeliverAsync(INotification notification, CancellationToken cancellationToken = default)
    {
        broadcaster.Publish(NotificationCreatedEvent.Create(notification));
        return Task.CompletedTask;
    }
}
