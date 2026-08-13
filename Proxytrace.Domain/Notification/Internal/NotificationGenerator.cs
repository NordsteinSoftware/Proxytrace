using Nordstein.Core.Common.Random;
using Nordstein.Core.Domain;

namespace Proxytrace.Domain.Notification.Internal;

internal class NotificationGenerator : DomainEntityGenerator<INotification>
{
    private readonly INotification.CreateNew factory;

    public NotificationGenerator(
        INotification.CreateNew factory,
        IRepository<INotification> repository,
        IRandom random) : base(repository, random)
    {
        this.factory = factory;
    }

    public override Task<INotification> GenerateAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(factory(
            kind: random.Enum<NotificationKind>(),
            severity: random.Enum<NotificationSeverity>(),
            title: random.String(),
            message: random.String(),
            projectId: null,
            targetKind: null,
            targetId: null));
}
