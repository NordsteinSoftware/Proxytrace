using Nordstein.Core.Common.Random;
using Nordstein.Core.Common.Security;
using Nordstein.Core.Domain;
using Proxytrace.Domain.User;

namespace Proxytrace.Domain.MfaBackupCode.Internal;

internal class MfaBackupCodeGenerator : DomainEntityGenerator<IMfaBackupCode>
{
    private readonly IMfaBackupCode.CreateNew factory;
    private readonly IDomainEntityGenerator<IUser> users;

    /// <summary>
    /// Initializes a new instance of the <see cref="MfaBackupCodeGenerator"/> class.
    /// </summary>
    public MfaBackupCodeGenerator(
        IMfaBackupCode.CreateNew factory,
        IDomainEntityGenerator<IUser> users,
        IRepository<IMfaBackupCode> repository,
        IRandom random) : base(repository, random)
    {
        this.factory = factory;
        this.users = users;
    }

    /// <summary>
    /// Generates asynchronously.
    /// </summary>
    public override async Task<IMfaBackupCode> GenerateAsync(CancellationToken cancellationToken = default)
    {
        var user = await users.GetOrCreateAsync(cancellationToken);
        return factory(user, Sha256.HexHash(random.UniqueString()));
    }
}
