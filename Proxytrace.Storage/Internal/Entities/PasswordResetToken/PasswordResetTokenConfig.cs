using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nordstein.Core.Common.Async;
using Proxytrace.Domain;
using Proxytrace.Domain.PasswordResetToken;
using Proxytrace.Domain.User;
using Proxytrace.Storage.Internal.Entities.User;

namespace Proxytrace.Storage.Internal.Entities.PasswordResetToken;

internal class PasswordResetTokenConfig
    : AbstractEntityConfiguration<PasswordResetTokenEntity>,
      IMapper<IPasswordResetToken, PasswordResetTokenEntity>
{
    private readonly IPasswordResetToken.CreateExisting factory;
    private readonly IRepository<IUser> users;

    /// <summary>
    /// Initializes a new instance of the <see cref="PasswordResetTokenConfig"/> class.
    /// </summary>
    public PasswordResetTokenConfig(IPasswordResetToken.CreateExisting factory, IRepository<IUser> users)
    {
        this.factory = factory;
        this.users = users;
    }

    /// <summary>
    /// Configures the application request pipeline.
    /// </summary>
    public override void Configure(EntityTypeBuilder<PasswordResetTokenEntity> builder)
    {
        builder.HasIndex(e => e.TokenHash).IsUnique();
        builder.Property(e => e.TokenHash).HasMaxLength(64);
        // The token is owned by the user — deleting the user discards their pending reset tokens.
        builder.HasOne<UserEntity>()
            .WithMany()
            .HasForeignKey(e => e.User)
            .OnDelete(DeleteBehavior.Cascade);
    }

    /// <summary>
    /// Maps.
    /// </summary>
    public async Task<IPasswordResetToken> Map(PasswordResetTokenEntity stored, CancellationToken cancellationToken = default)
    {
        var user = await users.GetAsync(stored.User, cancellationToken);
        return factory(user, stored.TokenHash, stored.ExpiresAt, stored.ConsumedAt, stored);
    }

    /// <summary>
    /// Maps.
    /// </summary>
    public Task<PasswordResetTokenEntity> Map(IPasswordResetToken domain, CancellationToken cancellationToken = default)
        => new PasswordResetTokenEntity
        {
            Id = domain.Id,
            User = domain.User.Id,
            TokenHash = domain.TokenHash,
            ExpiresAt = domain.ExpiresAt,
            ConsumedAt = domain.ConsumedAt,
            CreatedAt = domain.CreatedAt,
            UpdatedAt = domain.UpdatedAt,
        }.ToTaskResult();
}
