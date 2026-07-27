using System.Text;
using Proxytrace.Domain.Notification;

namespace Proxytrace.Domain.Notifications;

/// <summary>How the SMTP connection is secured (maps to MailKit's <c>SecureSocketOptions</c>).</summary>
public enum SmtpSecurity
{
    None,
    StartTls,
    Auto,
    SslOnConnect,
}

/// <summary>
/// Operator-configured outgoing-email settings. A single instance per installation. <see cref="Password"/>
/// is the plaintext SMTP password in memory; it is encrypted at rest by the store via
/// <see cref="Proxytrace.Domain.Security.ISecretProtector"/>.
/// </summary>
public sealed record EmailSettings(
    bool Enabled,
    string SmtpHost,
    int SmtpPort,
    SmtpSecurity Security,
    string? Username,
    string? Password,
    string FromAddress,
    string FromName,
    string? AppBaseUrl,
    NotificationSeverity MinSeverity)
{
    // Redact the SMTP password from the record's generated ToString()/PrintMembers so it never leaks
    // into a log line, exception message or debugger string — the same treatment ModelProvider gives
    // its upstream API key. Password stays a normal member (the mailer reads it; equality keeps it);
    // only its textual rendering is masked. Private, not protected virtual, because this record is
    // sealed and derives from object.
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("Enabled = ").Append(Enabled)
            .Append(", SmtpHost = ").Append(SmtpHost)
            .Append(", SmtpPort = ").Append(SmtpPort)
            .Append(", Security = ").Append(Security)
            .Append(", Username = ").Append(Username)
            .Append(", Password = ***")
            .Append(", FromAddress = ").Append(FromAddress)
            .Append(", FromName = ").Append(FromName)
            .Append(", AppBaseUrl = ").Append(AppBaseUrl)
            .Append(", MinSeverity = ").Append(MinSeverity);
        return true;
    }
}
