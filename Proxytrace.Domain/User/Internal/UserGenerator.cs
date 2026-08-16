using Nordstein.Core.Common.Async;
using Nordstein.Core.Common.Random;
using Nordstein.Core.Domain;
using Proxytrace.Domain.Notification;

namespace Proxytrace.Domain.User.Internal;

internal class UserGenerator : DomainEntityGenerator<IUser>
{
    private readonly IUser.CreateNew factory;

    /// <summary>
    /// Initializes a new instance of the <see cref="UserGenerator"/> class.
    /// </summary>
    public UserGenerator(
        IUser.CreateNew factory,
        IRepository<IUser> repository,
        IRandom random) : base(repository, random)
    {
        this.factory = factory;
    }

    /// <summary>
    /// Generates asynchronously.
    /// </summary>
    public override Task<IUser> GenerateAsync(CancellationToken cancellationToken = default)
        => factory(
                email: random.Email(),
                externalSubject: $"test|{random.UniqueString()}",
                passwordHash: null,
                role: random.Enum<UserRole>(),
                language: random.Any(SupportedLanguages.All),
                emailNotificationsEnabled: random.Bool(),
                emailNotificationMinSeverity: random.Enum<NotificationSeverity>())
            .ToTaskResult();
}
