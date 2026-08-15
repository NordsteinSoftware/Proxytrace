namespace Proxytrace.Domain.Notification;

/// <summary>
/// A multi-purpose, user-facing notification. Surfaced live on the dashboard notifications
/// section and (in future) over additional channels such as email. Today raised by anomaly
/// detection, but the same entity carries any <see cref="NotificationKind"/>.
/// </summary>
public interface INotification : IDomainEntity<INotification>
{
    /// <summary>What this notification represents.</summary>
    NotificationKind Kind { get; }

    /// <summary>Relative urgency, used for ordering and colour in the UI.</summary>
    NotificationSeverity Severity { get; }

    /// <summary>Short headline shown in the list.</summary>
    string Title { get; }

    /// <summary>Human-readable body explaining the notification.</summary>
    string Message { get; }

    /// <summary>Current lifecycle state.</summary>
    NotificationStatus Status { get; }

    /// <summary>
    /// The project this notification belongs to, or <see langword="null"/> for a global/system
    /// notification shown across all projects.
    /// </summary>
    Guid? ProjectId { get; }

    /// <summary>
    /// What <see cref="TargetId"/> refers to, enabling a deep-link. Both this and
    /// <see cref="TargetId"/> are set together or both <see langword="null"/>.
    /// </summary>
    NotificationTargetKind? TargetKind { get; }

    /// <summary>
    /// Soft reference to the entity this notification is about (e.g. the failed test-run group).
    /// Not a foreign key: the target may be deleted independently, leaving the notification
    /// dangling (it can still be dismissed).
    /// </summary>
    Guid? TargetId { get; }

    /// <summary>Marks the notification as read. Only valid from <see cref="NotificationStatus.Unread"/>.</summary>
    Task<INotification> MarkRead(CancellationToken cancellationToken = default);

    /// <summary>
    /// Dismisses the notification (hidden from the list). Valid from any non-dismissed state.
    /// </summary>
    Task<INotification> Dismiss(CancellationToken cancellationToken = default);

    /// <summary>
    /// Factory delegate for creating a new new instance.
    /// </summary>
    public delegate INotification CreateNew(
        NotificationKind kind,
        NotificationSeverity severity,
        string title,
        string message,
        Guid? projectId,
        NotificationTargetKind? targetKind,
        Guid? targetId);

    /// <summary>
    /// Factory delegate for creating a new existing instance.
    /// </summary>
    public delegate INotification CreateExisting(
        NotificationKind kind,
        NotificationSeverity severity,
        string title,
        string message,
        NotificationStatus status,
        Guid? projectId,
        NotificationTargetKind? targetKind,
        Guid? targetId,
        IDomainEntityData existing);
}
