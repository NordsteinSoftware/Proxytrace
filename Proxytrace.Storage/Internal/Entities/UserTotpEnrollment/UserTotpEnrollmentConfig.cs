using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.Logging;
using Nordstein.Core.Common.Security;
using Nordstein.Core.Common.Async;
using Proxytrace.Domain;
using Proxytrace.Domain.User;
using Proxytrace.Domain.UserTotpEnrollment;
using Proxytrace.Storage.Internal.Entities.User;

namespace Proxytrace.Storage.Internal.Entities.UserTotpEnrollment;

internal class UserTotpEnrollmentConfig
    : AbstractEntityConfiguration<UserTotpEnrollmentEntity>,
      IMapper<IUserTotpEnrollment, UserTotpEnrollmentEntity>
{
    private readonly IUserTotpEnrollment.CreateExisting factory;
    private readonly IRepository<IUser> users;

    // Lazy so the EF model can be built (design-time migrations) without resolving the Data Protection
    // key ring — Configure() never calls Map(). Mirrors ModelProviderConfig.
    private readonly Lazy<ISecretProtector> protector;
    private readonly ILogger<UserTotpEnrollmentConfig> logger;

    public UserTotpEnrollmentConfig(
        IUserTotpEnrollment.CreateExisting factory,
        IRepository<IUser> users,
        Lazy<ISecretProtector> protector,
        ILogger<UserTotpEnrollmentConfig> logger)
    {
        this.factory = factory;
        this.users = users;
        this.protector = protector;
        this.logger = logger;
    }

    public override void Configure(EntityTypeBuilder<UserTotpEnrollmentEntity> builder)
    {
        // One enrollment per user — re-running setup replaces the row, never accumulates.
        builder.HasIndex(e => e.User).IsUnique();
        builder.Property(e => e.Secret).IsRequired();
        // The enrollment is owned by the user — deleting the user discards it.
        builder.HasOne<UserEntity>()
            .WithMany()
            .HasForeignKey(e => e.User)
            .OnDelete(DeleteBehavior.Cascade);
    }

    public async Task<IUserTotpEnrollment> Map(UserTotpEnrollmentEntity stored, CancellationToken cancellationToken = default)
    {
        var user = await users.GetAsync(stored.User, cancellationToken);
        return factory(user, Decrypt(stored.Secret), stored.ConfirmedAt, stored.LastUsedStep, stored);
    }

    public Task<UserTotpEnrollmentEntity> Map(IUserTotpEnrollment domain, CancellationToken cancellationToken = default)
        => new UserTotpEnrollmentEntity
        {
            Id = domain.Id,
            User = domain.User.Id,
            Secret = protector.Value.Protect(domain.Secret),
            ConfirmedAt = domain.ConfirmedAt,
            LastUsedStep = domain.LastUsedStep,
            CreatedAt = domain.CreatedAt,
            UpdatedAt = domain.UpdatedAt,
        }.ToTaskResult();

    /// <summary>
    /// Decrypts the stored TOTP secret, degrading to an empty string (rather than throwing) when the
    /// ciphertext can't be decrypted — e.g. an ephemeral Data Protection key ring after a restart
    /// without <c>PROXYTRACE_DATA_DIR</c>. Listing/login must never crash; verification simply fails
    /// until the user re-enrolls. Mirrors <c>ModelProviderConfig.Decrypt</c>.
    /// </summary>
    private string Decrypt(string cipher)
    {
        try
        {
            return protector.Value.Unprotect(cipher);
        }
        catch (CryptographicException ex)
        {
            logger.LogWarning(ex, "Could not decrypt a stored TOTP secret; treating it as unset.");
            return string.Empty;
        }
    }
}
