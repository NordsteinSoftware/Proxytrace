using System.Threading.Channels;
using Proxytrace.Domain.Notification;

namespace Proxytrace.Application.Streaming;

/// <summary>
/// Base type for events on the global notification stream. The stream is not project-scoped on the
/// server; each event carries <see cref="ProjectId"/> and the client filters to the active project
/// (global, null-project notifications are shown everywhere).
/// </summary>
public abstract record NotificationEvent(Guid Id, Guid? ProjectId);

/// <summary>
/// Event raised when a notification created occurs.
/// </summary>
public record NotificationCreatedEvent(
    Guid Id,
    Guid? ProjectId,
    NotificationKind Kind,
    NotificationSeverity Severity,
    string Title,
    string Message,
    NotificationStatus Status,
    NotificationTargetKind? TargetKind,
    Guid? TargetId,
    DateTimeOffset CreatedAt) : NotificationEvent(Id, ProjectId)
{
    /// <summary>
    /// Creates.
    /// </summary>
    public static NotificationCreatedEvent Create(INotification notification)
        => new(
            notification.Id,
            notification.ProjectId,
            notification.Kind,
            notification.Severity,
            notification.Title,
            notification.Message,
            notification.Status,
            notification.TargetKind,
            notification.TargetId,
            notification.CreatedAt);
}

/// <summary>Emitted when a notification is marked read or dismissed.</summary>
public record NotificationStatusChangedEvent(
    Guid Id,
    Guid? ProjectId,
    NotificationStatus Status,
    DateTimeOffset UpdatedAt) : NotificationEvent(Id, ProjectId)
{
    /// <summary>
    /// Creates.
    /// </summary>
    public static NotificationStatusChangedEvent Create(INotification notification)
        => new(
            notification.Id,
            notification.ProjectId,
            notification.Status,
            notification.UpdatedAt);
}

/// <summary>
/// Broadcasts notification events.
/// </summary>
public interface INotificationBroadcaster
{
    /// <summary>
    /// Subscribes to all notification events. The returned reader is closed when
    /// <paramref name="cancellationToken"/> is cancelled.
    /// </summary>
    ChannelReader<NotificationEvent> Subscribe(CancellationToken cancellationToken);

    void Publish(NotificationEvent evt);
}
