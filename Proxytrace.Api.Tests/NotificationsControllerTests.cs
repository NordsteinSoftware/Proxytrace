using AwesomeAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Proxytrace.Api.Auth;
using Proxytrace.Api.Controllers;
using Proxytrace.Api.Dto.Notifications;
using Proxytrace.Application.Streaming;
using Proxytrace.Domain.Notification;
using Nordstein.Core.Testing;

namespace Proxytrace.Api.Tests;

/// <summary>
/// The by-id endpoint backing the notification deep-link (`?notification=&lt;id&gt;` / the email
/// link). It must resolve rows the list endpoint hides — dismissed ones — while never leaking
/// another tenant's row or an admin-only global one.
/// </summary>
[TestClass]
public sealed class NotificationsControllerTests : BaseTest<Module>
{
    [TestMethod]
    public async Task Get_AsMember_OwnProjectNotification_ReturnsIt()
    {
        var services = GetServices();
        var projectId = Guid.NewGuid();
        var notification = await SeedAsync(services, projectId);
        var controller = BuildController(services, accessibleProjectIds: [projectId]);

        var result = await controller.Get(notification.Id, CancellationToken);

        result.Value.Should().NotBeNull();
        result.Value.Id.Should().Be(notification.Id);
    }

    [TestMethod]
    public async Task Get_UnknownId_ReturnsNotFound()
    {
        var services = GetServices();
        var controller = BuildController(services, accessibleProjectIds: null);

        var result = await controller.Get(Guid.NewGuid(), CancellationToken);

        result.Result.Should().BeOfType<NotFoundResult>();
    }

    [TestMethod]
    public async Task Get_AsMember_OtherTenantsNotification_ReturnsNotFound()
    {
        var services = GetServices();
        var notification = await SeedAsync(services, Guid.NewGuid());
        var controller = BuildController(services, accessibleProjectIds: [Guid.NewGuid()]);

        var result = await controller.Get(notification.Id, CancellationToken);

        // 404 rather than 403 — a 403 would confirm the id exists.
        result.Result.Should().BeOfType<NotFoundResult>();
    }

    [TestMethod]
    public async Task Get_AsMember_GlobalNotification_ReturnsNotFound()
    {
        var services = GetServices();
        var notification = await SeedAsync(services, projectId: null);
        var controller = BuildController(services, accessibleProjectIds: [Guid.NewGuid()]);

        var result = await controller.Get(notification.Id, CancellationToken);

        result.Result.Should().BeOfType<NotFoundResult>();
    }

    [TestMethod]
    public async Task Get_AsAdmin_GlobalNotification_ReturnsIt()
    {
        var services = GetServices();
        var notification = await SeedAsync(services, projectId: null);
        var controller = BuildController(services, accessibleProjectIds: null);

        var result = await controller.Get(notification.Id, CancellationToken);

        result.Value.Should().NotBeNull();
        result.Value.Id.Should().Be(notification.Id);
    }

    [TestMethod]
    public async Task Get_DismissedNotification_StillResolves()
    {
        // The list endpoint hard-excludes dismissed rows, so a deep link to one can only be served
        // by this endpoint — dismissing a notification must not break its link.
        var services = GetServices();
        var projectId = Guid.NewGuid();
        var notification = await SeedAsync(services, projectId);
        await notification.Dismiss(CancellationToken);
        var controller = BuildController(services, accessibleProjectIds: [projectId]);

        var result = await controller.Get(notification.Id, CancellationToken);

        result.Value.Should().NotBeNull();
        result.Value.Status.Should().Be(NotificationStatus.Dismissed);
    }

    private async Task<INotification> SeedAsync(IServiceProvider services, Guid? projectId)
    {
        var create = services.GetRequiredService<INotification.CreateNew>();
        var repository = services.GetRequiredService<INotificationRepository>();
        return await repository.AddAsync(
            create(
                NotificationKind.Anomaly,
                NotificationSeverity.Warning,
                "title",
                "message",
                projectId,
                NotificationTargetKind.TestRunGroup,
                Guid.NewGuid()),
            CancellationToken);
    }

    /// <summary>
    /// Builds the controller with a guard stub. <paramref name="accessibleProjectIds"/> is
    /// <see langword="null"/> for an admin (sees everything, including global rows).
    /// </summary>
    private static NotificationsController BuildController(
        IServiceProvider services,
        IReadOnlyCollection<Guid>? accessibleProjectIds)
    {
        var guard = Substitute.For<IProjectAccessGuard>();
        guard.GetAccessibleProjectIdsAsync(Arg.Any<CancellationToken>()).Returns(accessibleProjectIds);
        guard.CanAccessProjectAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(call => accessibleProjectIds is null || accessibleProjectIds.Contains(call.Arg<Guid>()));

        return new NotificationsController(
            services.GetRequiredService<INotificationRepository>(),
            services.GetRequiredService<INotificationBroadcaster>(),
            new NotificationDtoMapper(),
            guard,
            services.GetRequiredService<INotification.CreateNew>());
    }
}
