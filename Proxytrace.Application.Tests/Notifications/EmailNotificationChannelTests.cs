using Proxytrace.Domain.Notifications;
using Autofac;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Proxytrace.Application.Notifications;
using Proxytrace.Application.Notifications.Internal;
using Proxytrace.Domain;
using Proxytrace.Domain.ModelEndpoint;
using Proxytrace.Domain.Notification;
using Proxytrace.Domain.Project;
using Proxytrace.Domain.User;
using Nordstein.Core.Testing;

namespace Proxytrace.Application.Tests.Notifications;

[TestClass]
public sealed class EmailNotificationChannelTests : BaseTest<Module>
{
    [TestMethod]
    public async Task DeliverAsync_WhenDisabled_DoesNotSend()
    {
        var sender = Substitute.For<IEmailSender>();
        IServiceProvider services = Build(sender, Settings() with { Enabled = false });
        var channel = services.GetRequiredService<EmailNotificationChannel>();

        await channel.DeliverAsync(GlobalNotification(services, NotificationSeverity.Critical), CancellationToken);

        await sender.DidNotReceive().SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task DeliverAsync_BelowOperatorFloor_DoesNotSend()
    {
        var sender = Substitute.For<IEmailSender>();
        IServiceProvider services = Build(sender, Settings() with { MinSeverity = NotificationSeverity.Critical });
        var channel = services.GetRequiredService<EmailNotificationChannel>();

        await channel.DeliverAsync(GlobalNotification(services, NotificationSeverity.Warning), CancellationToken);

        await sender.DidNotReceive().SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task DeliverAsync_GlobalNotification_EmailsOptedInUsersAboveThreshold()
    {
        var sender = Substitute.For<IEmailSender>();
        IServiceProvider services = Build(sender, Settings());
        var create = services.GetRequiredService<IUser.CreateNew>();

        await create("on@example.test", null, "h", UserRole.Member, "en", true, NotificationSeverity.Warning).AddAsync(CancellationToken);
        await create("off@example.test", null, "h", UserRole.Member, "en", false, NotificationSeverity.Warning).AddAsync(CancellationToken);
        await create("high@example.test", null, "h", UserRole.Member, "en", true, NotificationSeverity.Critical).AddAsync(CancellationToken);

        await channelFor(services).DeliverAsync(GlobalNotification(services, NotificationSeverity.Warning), CancellationToken);

        await sender.Received(1).SendAsync(Arg.Is<EmailMessage>(m => m != null && m.To == "on@example.test"), CancellationToken);
        await sender.DidNotReceive().SendAsync(Arg.Is<EmailMessage>(m => m != null && m.To == "off@example.test"), Arg.Any<CancellationToken>());
        await sender.DidNotReceive().SendAsync(Arg.Is<EmailMessage>(m => m != null && m.To == "high@example.test"), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task DeliverAsync_ProjectNotification_EmailsProjectMembersOnly()
    {
        var sender = Substitute.For<IEmailSender>();
        IServiceProvider services = Build(sender, Settings());
        var create = services.GetRequiredService<IUser.CreateNew>();
        var member = await create("member@example.test", null, "h", UserRole.Member, "en", true, NotificationSeverity.Warning).AddAsync(CancellationToken);
        await create("outsider@example.test", null, "h", UserRole.Member, "en", true, NotificationSeverity.Warning).AddAsync(CancellationToken);

        var endpoint = await services.GetRequiredService<IDomainEntityGenerator<IModelEndpoint>>().GetOrCreateAsync(CancellationToken);
        var project = services.GetRequiredService<IProject.CreateNew>()("P", endpoint, [member]);
        await services.GetRequiredService<IRepository<IProject>>().AddAsync(project, CancellationToken);

        var notification = Notification(services, NotificationSeverity.Critical, "t", "m", project.Id);

        await channelFor(services).DeliverAsync(notification, CancellationToken);

        await sender.Received(1).SendAsync(Arg.Is<EmailMessage>(m => m != null && m.To == "member@example.test"), CancellationToken);
        await sender.DidNotReceive().SendAsync(Arg.Is<EmailMessage>(m => m != null && m.To == "outsider@example.test"), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task DeliverAsync_TitleWithHtmlChars_EncodesInHtmlBody()
    {
        var captured = new List<EmailMessage>();
        IServiceProvider services = Build(CapturingSender(captured), Settings());
        var create = services.GetRequiredService<IUser.CreateNew>();
        await create("enc@example.test", null, "h", UserRole.Member, "en", true, NotificationSeverity.Warning).AddAsync(CancellationToken);

        var notification = Notification(
            services, NotificationSeverity.Warning, "<b>x</b>", "<script>alert(1)</script>", projectId: null);

        await channelFor(services).DeliverAsync(notification, CancellationToken);

        var html = captured.Should().ContainSingle().Which.HtmlBody;
        html.Should().Contain("&lt;b&gt;");
        html.Should().NotContain("<b>");
        html.Should().Contain("&lt;script&gt;");
        html.Should().NotContain("<script>");
    }

    [TestMethod]
    public async Task DeliverAsync_LinksToTheNotificationItself_NotItsTarget()
    {
        var captured = new List<EmailMessage>();
        IServiceProvider services = Build(CapturingSender(captured), Settings());
        var create = services.GetRequiredService<IUser.CreateNew>();
        await create("link@example.test", null, "h", UserRole.Member, "en", true, NotificationSeverity.Warning).AddAsync(CancellationToken);

        // A target is set, but the link must still point at the notification — the target may be
        // deleted, and the notification is the record the email is about.
        var notification = Notification(
            services, NotificationSeverity.Warning, "t", "m", projectId: null,
            targetKind: NotificationTargetKind.TestRunGroup, targetId: Guid.NewGuid());

        await channelFor(services).DeliverAsync(notification, CancellationToken);

        var message = captured.Should().ContainSingle().Which;
        var expected = $"https://app.example/notifications/{notification.Id}";
        message.HtmlBody.Should().Contain(expected);
        message.TextBody.Should().Contain(expected);
    }

    [TestMethod]
    public async Task DeliverAsync_WithoutAppBaseUrl_OmitsTheLink()
    {
        var captured = new List<EmailMessage>();
        IServiceProvider services = Build(CapturingSender(captured), Settings() with { AppBaseUrl = null });
        var create = services.GetRequiredService<IUser.CreateNew>();
        await create("nolink@example.test", null, "h", UserRole.Member, "en", true, NotificationSeverity.Warning).AddAsync(CancellationToken);

        await channelFor(services).DeliverAsync(GlobalNotification(services, NotificationSeverity.Warning), CancellationToken);

        captured.Should().ContainSingle().Which.HtmlBody.Should().NotContain("/notifications/");
    }

    private static IEmailSender CapturingSender(List<EmailMessage> captured)
    {
        var sender = Substitute.For<IEmailSender>();
        sender.When(s => s.SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>()))
              .Do(call =>
              {
                  var message = call.Arg<EmailMessage>();
                  ArgumentNullException.ThrowIfNull(message);
                  captured.Add(message);
              });
        return sender;
    }

    private static EmailNotificationChannel channelFor(IServiceProvider services)
        => services.GetRequiredService<EmailNotificationChannel>();

    private IServiceProvider Build(IEmailSender sender, EmailSettings settings)
    {
        var store = Substitute.For<IEmailSettingsStore>();
        store.GetAsync(Arg.Any<CancellationToken>()).Returns(settings);
        return GetServices(builder =>
        {
            builder.RegisterType<EmailNotificationChannel>().AsSelf().SingleInstance();
            builder.RegisterInstance(sender).As<IEmailSender>();
            builder.RegisterInstance(store).As<IEmailSettingsStore>();
            builder.Register(_ => NullLogger<EmailNotificationChannel>.Instance)
                .As<ILogger<EmailNotificationChannel>>().SingleInstance();
        });
    }

    private static INotification GlobalNotification(IServiceProvider services, NotificationSeverity severity)
        => Notification(services, severity, "title", "message", projectId: null);

    private static INotification Notification(
        IServiceProvider services,
        NotificationSeverity severity,
        string title,
        string message,
        Guid? projectId,
        NotificationTargetKind? targetKind = null,
        Guid? targetId = null)
        => services.GetRequiredService<INotification.CreateNew>()(
            NotificationKind.Anomaly, severity, title, message, projectId, targetKind, targetId);

    private static EmailSettings Settings() => new(
        Enabled: true, SmtpHost: "smtp", SmtpPort: 25, Security: SmtpSecurity.None,
        Username: null, Password: null, FromAddress: "a@b.c", FromName: "PT",
        AppBaseUrl: "https://app.example", MinSeverity: NotificationSeverity.Warning);
}
