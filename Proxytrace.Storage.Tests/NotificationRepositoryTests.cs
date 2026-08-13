using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Proxytrace.Domain.Notification;
using Nordstein.Core.Testing;

namespace Proxytrace.Storage.Tests;

[TestClass]
public sealed class NotificationRepositoryTests : BaseTest<Module>
{
    [TestMethod]
    public async Task GetForScopeAsync_ExcludesDismissed_AndHonoursIncludeRead()
    {
        IServiceProvider services = GetServices();
        var factory = services.GetRequiredService<INotification.CreateNew>();
        var repository = services.GetRequiredService<INotificationRepository>();

        var unread = await New(factory, "unread").AddAsync(CancellationToken);
        var read = await (await New(factory, "read").AddAsync(CancellationToken)).MarkRead(CancellationToken);
        var dismissed = await (await New(factory, "dismissed").AddAsync(CancellationToken)).Dismiss(CancellationToken);

        var all = await repository.GetForScopeAsync(projectId: null, includeRead: true, CancellationToken);
        all.Select(n => n.Id).Should().Contain(unread.Id).And.Contain(read.Id);
        all.Select(n => n.Id).Should().NotContain(dismissed.Id);

        var unreadOnly = await repository.GetForScopeAsync(projectId: null, includeRead: false, CancellationToken);
        unreadOnly.Select(n => n.Id).Should().Contain(unread.Id);
        unreadOnly.Select(n => n.Id).Should().NotContain(read.Id);
    }

    [TestMethod]
    public async Task CountUnreadAsync_CountsOnlyUnread()
    {
        IServiceProvider services = GetServices();
        var factory = services.GetRequiredService<INotification.CreateNew>();
        var repository = services.GetRequiredService<INotificationRepository>();

        await New(factory, "a").AddAsync(CancellationToken);
        await New(factory, "b").AddAsync(CancellationToken);
        await (await New(factory, "c").AddAsync(CancellationToken)).MarkRead(CancellationToken);

        var count = await repository.CountUnreadAsync(projectId: null, CancellationToken);

        count.Should().Be(2);
    }

    [TestMethod]
    public async Task FindActiveByTargetAsync_ReturnsActive_AndNullAfterDismiss()
    {
        IServiceProvider services = GetServices();
        var factory = services.GetRequiredService<INotification.CreateNew>();
        var repository = services.GetRequiredService<INotificationRepository>();

        var targetId = Guid.NewGuid();
        var notification = await factory(
                NotificationKind.Anomaly, NotificationSeverity.Critical, "t", "m",
                projectId: null, NotificationTargetKind.TestRunGroup, targetId)
            .AddAsync(CancellationToken);

        var found = await repository.FindActiveByTargetAsync(
            NotificationTargetKind.TestRunGroup, targetId, CancellationToken);
        found.Should().NotBeNull();
        found?.Id.Should().Be(notification.Id);

        await notification.Dismiss(CancellationToken);

        var afterDismiss = await repository.FindActiveByTargetAsync(
            NotificationTargetKind.TestRunGroup, targetId, CancellationToken);
        afterDismiss.Should().BeNull();
    }

    private static INotification New(INotification.CreateNew factory, string title)
        => factory(
            NotificationKind.Anomaly, NotificationSeverity.Warning, title, "message",
            projectId: null, targetKind: null, targetId: null);
}
