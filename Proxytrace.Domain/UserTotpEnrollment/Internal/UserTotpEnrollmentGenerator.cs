using Nordstein.Core.Common.Random;
using Nordstein.Core.Domain;
using Proxytrace.Domain.User;

namespace Proxytrace.Domain.UserTotpEnrollment.Internal;

internal class UserTotpEnrollmentGenerator : DomainEntityGenerator<IUserTotpEnrollment>
{
    private readonly IUserTotpEnrollment.CreateNew factory;
    private readonly IDomainEntityGenerator<IUser> users;

    /// <summary>
    /// Initializes a new instance of the <see cref="UserTotpEnrollmentGenerator"/> class.
    /// </summary>
    public UserTotpEnrollmentGenerator(
        IUserTotpEnrollment.CreateNew factory,
        IDomainEntityGenerator<IUser> users,
        IRepository<IUserTotpEnrollment> repository,
        IRandom random) : base(repository, random)
    {
        this.factory = factory;
        this.users = users;
    }

    /// <summary>
    /// Generates asynchronously.
    /// </summary>
    public override async Task<IUserTotpEnrollment> GenerateAsync(CancellationToken cancellationToken = default)
    {
        var user = await users.GetOrCreateAsync(cancellationToken);
        return factory(user, random.UniqueString());
    }
}
