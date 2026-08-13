using System.Security.Cryptography;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Proxytrace.Domain.Security;
using Nordstein.Core.Common.Time;
using AppEmailSettings = Proxytrace.Domain.Notifications.EmailSettings;
using IEmailSettingsStore = Proxytrace.Domain.Notifications.IEmailSettingsStore;

namespace Proxytrace.Storage.Internal.Entities.EmailSettings;

[UsedImplicitly]
internal sealed class EmailSettingsStore : IEmailSettingsStore
{
    private readonly Func<StorageDbContext> contextFactory;
    private readonly ISecretProtector secretProtector;
    private readonly IClock clock;
    private readonly ILogger<EmailSettingsStore> logger;

    public EmailSettingsStore(
        Func<StorageDbContext> contextFactory,
        ISecretProtector secretProtector,
        IClock clock,
        ILogger<EmailSettingsStore> logger)
    {
        this.contextFactory = contextFactory;
        this.secretProtector = secretProtector;
        this.clock = clock;
        this.logger = logger;
    }

    public async Task<AppEmailSettings?> GetAsync(CancellationToken cancellationToken = default)
    {
        EmailSettingsEntity? entity = await contextFactory()
            .Set<EmailSettingsEntity>()
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken);
        if (entity is null)
            return null;

        return new AppEmailSettings(
            entity.Enabled,
            entity.SmtpHost,
            entity.SmtpPort,
            entity.Security,
            entity.Username,
            DecryptPassword(entity.Password),
            entity.FromAddress,
            entity.FromName,
            entity.AppBaseUrl,
            entity.MinSeverity);
    }

    /// <summary>
    /// Decrypts the stored password, degrading to <see langword="null"/> (rather than throwing) when the
    /// ciphertext can't be decrypted — e.g. an ephemeral Data Protection key ring after a restart without
    /// <c>PROXYTRACE_DATA_DIR</c>. <see cref="GetAsync"/> is on the hot auth path (<c>/api/auth/me</c>), so a
    /// crash here would lock every user out; instead email delivery degrades and the operator re-enters the password.
    /// </summary>
    private string? DecryptPassword(string? cipher)
    {
        if (string.IsNullOrEmpty(cipher))
            return null;

        try
        {
            return secretProtector.Unprotect(cipher);
        }
        catch (CryptographicException ex)
        {
            logger.LogWarning(ex, "Could not decrypt the stored SMTP password; treating it as unset.");
            return null;
        }
    }

    public async Task SaveAsync(AppEmailSettings settings, CancellationToken cancellationToken = default)
    {
        var cipher = string.IsNullOrEmpty(settings.Password)
            ? null
            : secretProtector.Protect(settings.Password);

        StorageDbContext context = contextFactory();
        DbSet<EmailSettingsEntity> set = context.Set<EmailSettingsEntity>();
        var now = clock.UtcNow;

        EmailSettingsEntity? existing = await set.FirstOrDefaultAsync(cancellationToken);
        if (existing is null)
        {
            set.Add(new EmailSettingsEntity
            {
                Id = Guid.NewGuid(),
                Enabled = settings.Enabled,
                SmtpHost = settings.SmtpHost,
                SmtpPort = settings.SmtpPort,
                Security = settings.Security,
                Username = settings.Username,
                Password = cipher,
                FromAddress = settings.FromAddress,
                FromName = settings.FromName,
                AppBaseUrl = settings.AppBaseUrl,
                MinSeverity = settings.MinSeverity,
                CreatedAt = now,
                UpdatedAt = now,
            });
        }
        else
        {
            context.Entry(existing).CurrentValues.SetValues(existing with
            {
                Enabled = settings.Enabled,
                SmtpHost = settings.SmtpHost,
                SmtpPort = settings.SmtpPort,
                Security = settings.Security,
                Username = settings.Username,
                Password = cipher,
                FromAddress = settings.FromAddress,
                FromName = settings.FromName,
                AppBaseUrl = settings.AppBaseUrl,
                MinSeverity = settings.MinSeverity,
                UpdatedAt = now,
            });
        }

        await context.SaveChangesAsync(cancellationToken);
    }
}
