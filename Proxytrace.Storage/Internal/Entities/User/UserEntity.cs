using Proxytrace.Domain.Notification;
using Proxytrace.Domain.User;

namespace Proxytrace.Storage.Internal.Entities.User;

[StoredDomainEntity(typeof(IUser))]
[Cacheable]
internal record UserEntity : Entity
{
    /// <summary>
    /// Gets or sets the email.
    /// </summary>
    public required string Email { get; init; }

    /// <summary><see cref="Proxytrace.Domain.User.IUser.ExternalSubject"/>. Null for local-auth users.</summary>
    public string? ExternalSubject { get; init; }

    /// <summary><see cref="Proxytrace.Domain.User.IUser.PasswordHash"/>. Null for OIDC users.</summary>
    public string? PasswordHash { get; init; }

    /// <summary>
    /// Gets or sets the role.
    /// </summary>
    public required UserRole Role { get; init; }

    /// <summary><see cref="Proxytrace.Domain.User.IUser.Language"/>. BCP-47 culture code; defaults to English.</summary>
    public string Language { get; init; } = "en";

    /// <summary><see cref="Proxytrace.Domain.User.IUser.EmailNotificationsEnabled"/>. Defaults to true.</summary>
    public bool EmailNotificationsEnabled { get; init; } = true;

    /// <summary><see cref="Proxytrace.Domain.User.IUser.EmailNotificationMinSeverity"/>. Defaults to Info.</summary>
    public NotificationSeverity EmailNotificationMinSeverity { get; init; } = NotificationSeverity.Info;
}
