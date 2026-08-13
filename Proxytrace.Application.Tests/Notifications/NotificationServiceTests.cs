using Autofac;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Proxytrace.Application.Notifications;
using Proxytrace.Application.Notifications.Internal;
using Proxytrace.Domain.Notification;
using Nordstein.Core.Testing;

namespace Proxytrace.Application.Tests.Notifications;

[TestClass]
public sealed class NotificationServiceTests : BaseTest<Module>
{
    [TestMethod]
    public async Task NotifyAsync_DeliversToEveryChannel_EvenWhenOneThrows()
    {
        var good = Substitute.For<INotificationChannel>();
        good.Name.Returns("Good");

        var bad = Substitute.For<INotificationChannel>();
        bad.Name.Returns("Bad");
        bad.DeliverAsync(Arg.Any<INotification>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("boom")));

        IServiceProvider services = Build(builder =>
        {
            builder.RegisterInstance(good).As<INotificationChannel>();
            builder.RegisterInstance(bad).As<INotificationChannel>();
        });

        var service = services.GetRequiredService<INotificationService>();
        // Null target → dedup is skipped, both channels are invoked.
        var request = new NotificationRequest(
            NotificationKind.Anomaly, NotificationSeverity.Warning, "title", "message", ProjectId: null);

        await service.NotifyAsync(request, CancellationToken);

        // The bad channel throwing must not stop the good channel from being delivered to.
        await good.Received(1).DeliverAsync(Arg.Is<INotification>(n => n != null && n.Title == "title"), CancellationToken);
        await bad.Received(1).DeliverAsync(Arg.Is<INotification>(n => n != null && n.Title == "title"), CancellationToken);
    }

    [TestMethod]
    public async Task NotifyAsync_WhenActiveDuplicateExistsForTarget_SkipsAllChannels()
    {
        var channel = Substitute.For<INotificationChannel>();
        channel.Name.Returns("Any");

        var targetId = Guid.NewGuid();
        IServiceProvider services = Build(builder => builder.RegisterInstance(channel).As<INotificationChannel>());
        await SeedActiveAsync(services, NotificationTargetKind.TestRunGroup, targetId);

        var request = new NotificationRequest(
            NotificationKind.Anomaly, NotificationSeverity.Warning, "t", "m", ProjectId: null,
            TargetKind: NotificationTargetKind.TestRunGroup, TargetId: targetId);

        await services.GetRequiredService<INotificationService>().NotifyAsync(request, CancellationToken);

        await channel.DidNotReceive().DeliverAsync(Arg.Any<INotification>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task NotifyAsync_WhenActiveDuplicateExistsForTarget_DoesNotCreateASecondNotification()
    {
        IServiceProvider services = Build();
        var repository = services.GetRequiredService<INotificationRepository>();
        var targetId = Guid.NewGuid();
        await SeedActiveAsync(services, NotificationTargetKind.TestRunGroup, targetId);

        var request = new NotificationRequest(
            NotificationKind.Anomaly, NotificationSeverity.Warning, "t", "m", ProjectId: null,
            TargetKind: NotificationTargetKind.TestRunGroup, TargetId: targetId);

        await services.GetRequiredService<INotificationService>().NotifyAsync(request, CancellationToken);

        var stored = await repository.GetForScopeAsync(projectId: null, includeRead: true, CancellationToken);
        stored.Should().ContainSingle();
    }

    [TestMethod]
    public async Task NotifyAsync_PersistsTheNotificationBeforeDelivering()
    {
        IServiceProvider services = Build();
        var repository = services.GetRequiredService<INotificationRepository>();
        var targetId = Guid.NewGuid();

        var request = new NotificationRequest(
            NotificationKind.Anomaly, NotificationSeverity.Critical, "persisted", "body", ProjectId: null,
            TargetKind: NotificationTargetKind.Agent, TargetId: targetId);

        await services.GetRequiredService<INotificationService>().NotifyAsync(request, CancellationToken);

        var stored = await repository.GetForScopeAsync(projectId: null, includeRead: true, CancellationToken);
        var notification = stored.Should().ContainSingle().Which;
        notification.Title.Should().Be("persisted");
        notification.Message.Should().Be("body");
        notification.Severity.Should().Be(NotificationSeverity.Critical);
        notification.TargetKind.Should().Be(NotificationTargetKind.Agent);
        notification.TargetId.Should().Be(targetId);
    }

    [TestMethod]
    public async Task NotifyAsync_DeliversThePersistedEntity_SoChannelsCanReferenceItById()
    {
        var channel = Substitute.For<INotificationChannel>();
        channel.Name.Returns("Any");

        IServiceProvider services = Build(builder => builder.RegisterInstance(channel).As<INotificationChannel>());
        var repository = services.GetRequiredService<INotificationRepository>();

        var request = new NotificationRequest(
            NotificationKind.Anomaly, NotificationSeverity.Warning, "t", "m", ProjectId: null);

        await services.GetRequiredService<INotificationService>().NotifyAsync(request, CancellationToken);

        var stored = await repository.GetForScopeAsync(projectId: null, includeRead: true, CancellationToken);
        var persistedId = stored.Should().ContainSingle().Which.Id;
        await channel.Received(1).DeliverAsync(Arg.Is<INotification>(n => n != null && n.Id == persistedId), CancellationToken);
    }

    [TestMethod]
    public async Task NotifyAsync_WhenPersistFails_SwallowsAndDoesNotDeliver()
    {
        var channel = Substitute.For<INotificationChannel>();
        channel.Name.Returns("Any");

        var repository = Substitute.For<INotificationRepository>();
        repository.AddAsync(Arg.Any<INotification>(), Arg.Any<CancellationToken>())
            .Returns<INotification>(_ => throw new InvalidOperationException("db down"));

        IServiceProvider services = Build(builder =>
        {
            builder.RegisterInstance(repository).As<INotificationRepository>();
            builder.RegisterInstance(channel).As<INotificationChannel>();
        });

        // Null target → dedup is skipped, so AddAsync is reached and throws.
        var request = new NotificationRequest(
            NotificationKind.Anomaly, NotificationSeverity.Warning, "t", "m", ProjectId: null);

        // Best-effort: a transient persist failure must not throw out of NotifyAsync, because
        // producers call it as an unguarded trailing statement.
        var act = () => services.GetRequiredService<INotificationService>().NotifyAsync(request, CancellationToken);
        await act.Should().NotThrowAsync();

        // Nothing persisted → nothing to deliver.
        await channel.DidNotReceive().DeliverAsync(Arg.Any<INotification>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Builds a provider running the real <see cref="NotificationService"/> over the in-memory
    /// notification repository, so the tests exercise the actual create-then-fan-out sequence.
    /// </summary>
    private IServiceProvider Build(Action<ContainerBuilder>? configure = null)
        => GetServices(builder =>
        {
            builder.RegisterType<NotificationService>().As<INotificationService>().SingleInstance();
            builder.Register(_ => NullLogger<NotificationService>.Instance)
                .As<ILogger<NotificationService>>()
                .SingleInstance();
            configure?.Invoke(builder);
        });

    private async Task SeedActiveAsync(IServiceProvider services, NotificationTargetKind kind, Guid targetId)
    {
        var create = services.GetRequiredService<INotification.CreateNew>();
        var repository = services.GetRequiredService<INotificationRepository>();
        await repository.AddAsync(
            create(NotificationKind.Anomaly, NotificationSeverity.Warning, "existing", "existing", null, kind, targetId),
            CancellationToken);
    }
}
