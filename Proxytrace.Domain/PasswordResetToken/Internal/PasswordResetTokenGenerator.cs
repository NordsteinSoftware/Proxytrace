using Nordstein.Core.Common.Random;
using Nordstein.Core.Common.Security;
using Nordstein.Core.Domain;
using Proxytrace.Domain.User;

namespace Proxytrace.Domain.PasswordResetToken.Internal;

internal class PasswordResetTokenGenerator : DomainEntityGenerator<IPasswordResetToken>
{
    private readonly IPasswordResetToken.CreateNew factory;
    private readonly IDomainEntityGenerator<IUser> users;

    public PasswordResetTokenGenerator(
        IPasswordResetToken.CreateNew factory,
        IDomainEntityGenerator<IUser> users,
        IRepository<IPasswordResetToken> repository,
        IRandom random) : base(repository, random)
    {
        this.factory = factory;
        this.users = users;
    }

    public override async Task<IPasswordResetToken> GenerateAsync(CancellationToken cancellationToken = default)
    {
        var user = await users.GetOrCreateAsync(cancellationToken);
        return factory(
            user: user,
            tokenHash: Sha256.HexHash(random.UniqueString()),
            expiresAt: DateTimeOffset.UtcNow.AddHours(1));
    }
}
