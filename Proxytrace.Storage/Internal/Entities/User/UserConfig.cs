using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nordstein.Core.Common.Async;
using Proxytrace.Domain.Notification;
using Proxytrace.Domain.User;

namespace Proxytrace.Storage.Internal.Entities.User;

internal class UserConfig : AbstractEntityConfiguration<UserEntity>, IMapper<IUser, UserEntity>
{
    private readonly IUser.CreateExisting factory;

    /// <summary>
    /// Initializes a new instance of the <see cref="UserConfig"/> class.
    /// </summary>
    public UserConfig(IUser.CreateExisting factory)
    {
        this.factory = factory;
    }

    /// <summary>
    /// Configures the application request pipeline.
    /// </summary>
    public override void Configure(EntityTypeBuilder<UserEntity> builder)
    {
        builder.HasIndex(e => e.Email).IsUnique();
        builder.HasIndex(e => e.ExternalSubject)
            .IsUnique()
            .HasFilter("\"ExternalSubject\" IS NOT NULL");

        // Backfills existing rows (and any insert that omits the column) to English. The mapper
        // always sets Language explicitly, so this only matters for the migration backfill.
        builder.Property(e => e.Language).HasDefaultValue("en");

        builder.Property(e => e.EmailNotificationsEnabled).HasDefaultValue(true);
        builder.Property(e => e.EmailNotificationMinSeverity).HasDefaultValue(NotificationSeverity.Info);
    }

    /// <summary>
    /// Maps.
    /// </summary>
    public Task<IUser> Map(UserEntity stored, CancellationToken cancellationToken = default)
        => factory(stored.Email, stored.ExternalSubject, stored.PasswordHash, stored.Role, stored.Language, stored.EmailNotificationsEnabled, stored.EmailNotificationMinSeverity, stored).ToTaskResult();

    /// <summary>
    /// Maps.
    /// </summary>
    public Task<UserEntity> Map(IUser domain, CancellationToken cancellationToken = default)
        => new UserEntity
        {
            Id = domain.Id,
            Email = domain.Email,
            ExternalSubject = domain.ExternalSubject,
            PasswordHash = domain.PasswordHash,
            Role = domain.Role,
            Language = domain.Language,
            EmailNotificationsEnabled = domain.EmailNotificationsEnabled,
            EmailNotificationMinSeverity = domain.EmailNotificationMinSeverity,
            CreatedAt = domain.CreatedAt,
            UpdatedAt = domain.UpdatedAt,
        }.ToTaskResult();
}
