namespace Proxytrace.Api.Dto.EmailSettings;

/// <summary>
/// Maps email settings dto between representations.
/// </summary>
public sealed class EmailSettingsDtoMapper
{
    /// <summary>
    /// To dto.
    /// </summary>
    public EmailSettingsDto ToDto(Domain.Notifications.EmailSettings s) => new(
        s.Enabled, s.SmtpHost, s.SmtpPort, s.Security, s.Username,
        PasswordSet: !string.IsNullOrEmpty(s.Password),
        s.FromAddress, s.FromName, s.AppBaseUrl, s.MinSeverity);
}
