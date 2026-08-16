using Proxytrace.Domain.Notifications;
using Proxytrace.Domain.Notification;

namespace Proxytrace.Storage.Internal.Entities.EmailSettings;

/// <summary>
/// The single-row operator email/SMTP configuration. <see cref="Password"/> holds ciphertext only
/// (encrypted via ISecretProtector in the store).
/// </summary>
internal record EmailSettingsEntity : Entity
{
    /// <summary>
    /// Gets or sets the enabled.
    /// </summary>
    public required bool Enabled { get; init; }
    /// <summary>
    /// Gets or sets the smtp host.
    /// </summary>
    public required string SmtpHost { get; init; }
    /// <summary>
    /// Gets or sets the smtp port.
    /// </summary>
    public required int SmtpPort { get; init; }
    /// <summary>
    /// Gets or sets the security.
    /// </summary>
    public required SmtpSecurity Security { get; init; }
    /// <summary>
    /// Gets or sets the username.
    /// </summary>
    public string? Username { get; init; }
    /// <summary>
    /// Gets or sets the password.
    /// </summary>
    public string? Password { get; init; }
    /// <summary>
    /// Gets or sets the from address.
    /// </summary>
    public required string FromAddress { get; init; }
    /// <summary>
    /// Gets or sets the from name.
    /// </summary>
    public required string FromName { get; init; }
    /// <summary>
    /// Gets or sets the app base url.
    /// </summary>
    public string? AppBaseUrl { get; init; }
    /// <summary>
    /// Gets or sets the min severity.
    /// </summary>
    public required NotificationSeverity MinSeverity { get; init; }
}
