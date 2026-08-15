using Proxytrace.Domain.Notification;

namespace Proxytrace.Api.Dto.Notifications;

/// <summary>
/// Maps <see cref="INotification"/> domain entities to <see cref="NotificationDto"/>.
/// </summary>
public sealed class NotificationDtoMapper
{
    /// <summary>
    /// To dto.
    /// </summary>
    public NotificationDto ToDto(INotification n)
        => new(
            n.Id,
            n.Kind,
            n.Severity,
            n.Title,
            n.Message,
            n.Status,
            n.ProjectId,
            n.TargetKind,
            n.TargetId,
            n.CreatedAt,
            n.UpdatedAt);
}
