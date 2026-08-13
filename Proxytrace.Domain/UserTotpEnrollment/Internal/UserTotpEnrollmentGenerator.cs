using Nordstein.Core.Common.Random;
using Proxytrace.Domain.Internal;
using Proxytrace.Domain.User;

namespace Proxytrace.Domain.UserTotpEnrollment.Internal;

internal class UserTotpEnrollmentGenerator : DomainEntityGenerator<IUserTotpEnrollment>
{
    private readonly IUserTotpEnrollment.CreateNew factory;
    private readonly IDomainEntityGenerator<IUser> users;

    public UserTotpEnrollmentGenerator(
        IUserTotpEnrollment.CreateNew factory,
        IDomainEntityGenerator<IUser> users,
        IRepository<IUserTotpEnrollment> repository,
        IRandom random) : base(repository, random)
    {
        this.factory = factory;
        this.users = users;
    }

    public override async Task<IUserTotpEnrollment> GenerateAsync(CancellationToken cancellationToken = default)
    {
        var user = await users.GetOrCreateAsync(cancellationToken);
        return factory(user, random.UniqueString());
    }
}
