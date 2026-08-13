using Nordstein.Core.Common.Random;
using Nordstein.Core.Common.Security;
using Nordstein.Core.Domain;
using Proxytrace.Domain.User;

namespace Proxytrace.Domain.Invite.Internal;

internal class InviteGenerator : DomainEntityGenerator<IInvite>
{
    private readonly IInvite.CreateNew factory;
    private readonly IDomainEntityGenerator<IUser> users;

    public InviteGenerator(
        IInvite.CreateNew factory,
        IDomainEntityGenerator<IUser> users,
        IRepository<IInvite> repository,
        IRandom random) : base(repository, random)
    {
        this.factory = factory;
        this.users = users;
    }

    public override async Task<IInvite> GenerateAsync(CancellationToken cancellationToken = default)
    {
        var user = await users.CreateAsync(cancellationToken);
        return factory(
            email: random.Email(),
            role: random.Enum<UserRole>(),
            tokenHash: Sha256.HexHash(random.UniqueString()),
            expiresAt: DateTimeOffset.UtcNow.AddDays(7),
            invitedBy: user);
    }
}
