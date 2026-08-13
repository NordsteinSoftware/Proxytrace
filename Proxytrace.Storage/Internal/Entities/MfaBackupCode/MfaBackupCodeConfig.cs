using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nordstein.Core.Common.Async;
using Proxytrace.Domain;
using Proxytrace.Domain.MfaBackupCode;
using Proxytrace.Domain.User;
using Proxytrace.Storage.Internal.Entities.User;

namespace Proxytrace.Storage.Internal.Entities.MfaBackupCode;

internal class MfaBackupCodeConfig
    : AbstractEntityConfiguration<MfaBackupCodeEntity>,
      IMapper<IMfaBackupCode, MfaBackupCodeEntity>
{
    private readonly IMfaBackupCode.CreateExisting factory;
    private readonly IRepository<IUser> users;

    public MfaBackupCodeConfig(IMfaBackupCode.CreateExisting factory, IRepository<IUser> users)
    {
        this.factory = factory;
        this.users = users;
    }

    public override void Configure(EntityTypeBuilder<MfaBackupCodeEntity> builder)
    {
        builder.HasIndex(e => e.CodeHash).IsUnique();
        // Backup codes are hashed with PBKDF2 + a per-code salt (IPasswordService), whose encoded
        // form is far longer than the 64-char hex SHA-256 this column originally held. Legacy rows
        // keep their 64-char value and still verify.
        builder.Property(e => e.CodeHash).HasMaxLength(256);
        builder.HasIndex(e => e.User);
        // The codes are owned by the user — deleting the user discards them.
        builder.HasOne<UserEntity>()
            .WithMany()
            .HasForeignKey(e => e.User)
            .OnDelete(DeleteBehavior.Cascade);
    }

    public async Task<IMfaBackupCode> Map(MfaBackupCodeEntity stored, CancellationToken cancellationToken = default)
    {
        var user = await users.GetAsync(stored.User, cancellationToken);
        return factory(user, stored.CodeHash, stored.ConsumedAt, stored);
    }

    public Task<MfaBackupCodeEntity> Map(IMfaBackupCode domain, CancellationToken cancellationToken = default)
        => new MfaBackupCodeEntity
        {
            Id = domain.Id,
            User = domain.User.Id,
            CodeHash = domain.CodeHash,
            ConsumedAt = domain.ConsumedAt,
            CreatedAt = domain.CreatedAt,
            UpdatedAt = domain.UpdatedAt,
        }.ToTaskResult();
}
