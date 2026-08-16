using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nordstein.Core.Common.Async;
using Proxytrace.Domain;
using Proxytrace.Domain.Invite;
using Proxytrace.Domain.User;
using Proxytrace.Storage.Internal.Entities.User;

namespace Proxytrace.Storage.Internal.Entities.Invite;

internal class InviteConfig : AbstractEntityConfiguration<InviteEntity>, IMapper<IInvite, InviteEntity>
{
    private readonly IInvite.CreateExisting factory;
    private readonly IRepository<IUser> users;

    /// <summary>
    /// Initializes a new instance of the <see cref="InviteConfig"/> class.
    /// </summary>
    public InviteConfig(IInvite.CreateExisting factory, IRepository<IUser> users)
    {
        this.factory = factory;
        this.users = users;
    }

    /// <summary>
    /// Configures the application request pipeline.
    /// </summary>
    public override void Configure(EntityTypeBuilder<InviteEntity> builder)
    {
        builder.HasIndex(e => e.TokenHash).IsUnique();
        builder.Property(e => e.TokenHash).HasMaxLength(64);
        builder.HasIndex(e => e.Email);
        builder.HasOne<UserEntity>()
            .WithMany()
            .HasForeignKey(e => e.InvitedBy)
            .OnDelete(DeleteBehavior.Restrict);
    }

    /// <summary>
    /// Maps.
    /// </summary>
    public async Task<IInvite> Map(InviteEntity stored, CancellationToken cancellationToken = default)
    {
        var inviter = await users.GetAsync(stored.InvitedBy, cancellationToken);
        return factory(stored.Email, stored.Role, stored.TokenHash, stored.ExpiresAt, stored.ConsumedAt, inviter, stored);
    }

    /// <summary>
    /// Maps.
    /// </summary>
    public Task<InviteEntity> Map(IInvite domain, CancellationToken cancellationToken = default)
        => new InviteEntity
        {
            Id = domain.Id,
            Email = domain.Email,
            Role = domain.Role,
            TokenHash = domain.TokenHash,
            ExpiresAt = domain.ExpiresAt,
            ConsumedAt = domain.ConsumedAt,
            InvitedBy = domain.InvitedBy.Id,
            CreatedAt = domain.CreatedAt,
            UpdatedAt = domain.UpdatedAt,
        }.ToTaskResult();
}
