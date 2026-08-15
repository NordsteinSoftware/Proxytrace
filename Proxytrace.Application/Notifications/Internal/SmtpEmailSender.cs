using Proxytrace.Domain.Notifications;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace Proxytrace.Application.Notifications.Internal;

internal sealed class SmtpEmailSender : IEmailSender
{
    private const int ConnectionTimeoutMs = 30_000;

    private readonly IEmailSettingsStore settingsStore;

    /// <summary>
    /// Initializes a new instance of the <see cref="SmtpEmailSender"/> class.
    /// </summary>
    public SmtpEmailSender(IEmailSettingsStore settingsStore)
    {
        this.settingsStore = settingsStore;
    }

    /// <summary>
    /// Sends asynchronously.
    /// </summary>
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        EmailSettings? settings = await settingsStore.GetAsync(cancellationToken);
        if (settings is null)
            throw new InvalidOperationException("Email is not configured.");

        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(settings.FromName, settings.FromAddress));
        mime.To.Add(new MailboxAddress(message.ToName ?? message.To, message.To));
        mime.Subject = message.Subject;
        mime.Body = new BodyBuilder { HtmlBody = message.HtmlBody, TextBody = message.TextBody }.ToMessageBody();

        using var client = new SmtpClient { Timeout = ConnectionTimeoutMs };
        await client.ConnectAsync(settings.SmtpHost, settings.SmtpPort, Map(settings.Security), cancellationToken);
        if (!string.IsNullOrEmpty(settings.Username))
            await client.AuthenticateAsync(settings.Username, settings.Password ?? string.Empty, cancellationToken);
        await client.SendAsync(mime, cancellationToken);
        await client.DisconnectAsync(quit: true, cancellationToken);
    }

    private static SecureSocketOptions Map(SmtpSecurity security) => security switch
    {
        SmtpSecurity.None => SecureSocketOptions.None,
        SmtpSecurity.StartTls => SecureSocketOptions.StartTls,
        SmtpSecurity.SslOnConnect => SecureSocketOptions.SslOnConnect,
        _ => SecureSocketOptions.Auto,
    };
}
