using Proxytrace.Domain.AuditLog;
using Proxytrace.Domain.Notifications;
using AwesomeAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Proxytrace.Api.Controllers;
using Proxytrace.Api.Dto.EmailSettings;
using Proxytrace.Application.Auth;
using Proxytrace.Application.Notifications;
using Proxytrace.Domain.Notification;
using Proxytrace.Domain.User;
using Nordstein.Core.Testing;

namespace Proxytrace.Api.Tests;

[TestClass]
public sealed class EmailSettingsControllerTests : BaseTest<Module>
{
    [TestMethod]
    public async Task Get_WhenUnset_ReturnsNoContent()
    {
        var store = Substitute.For<IEmailSettingsStore>();
        var controller = Controller(store, Substitute.For<IEmailSender>(), Substitute.For<ICurrentUserAccessor>());

        var result = await controller.Get(CancellationToken);

        result.Result.Should().BeOfType<NoContentResult>();
    }

    [TestMethod]
    public async Task Update_SavesSettings_AndOmitsPasswordFromResponse()
    {
        var store = Substitute.For<IEmailSettingsStore>();
        var controller = Controller(store, Substitute.For<IEmailSender>(), Substitute.For<ICurrentUserAccessor>());
        var request = new UpdateEmailSettingsRequest(
            true, "smtp.example", 587, SmtpSecurity.StartTls, "user", "secret",
            "a@b.c", "PT", "https://app.example", NotificationSeverity.Warning);

        var result = await controller.Update(request, CancellationToken);

        await store.Received(1).SaveAsync(
            Arg.Is<EmailSettings>(s => s != null && s.SmtpHost == "smtp.example" && s.Password == "secret"),
            Arg.Any<CancellationToken>());
        result.Value.Should().NotBeNull().And.Match<EmailSettingsDto>(d => d.PasswordSet);
    }

    [TestMethod]
    public async Task SendTest_WhenConfigured_SendsToCurrentUser()
    {
        var store = Substitute.For<IEmailSettingsStore>();
        store.GetAsync(Arg.Any<CancellationToken>()).Returns(Sample());
        var sender = Substitute.For<IEmailSender>();
        var accessor = Substitute.For<ICurrentUserAccessor>();
        var me = await CreateUserAsync();
        accessor.GetCurrentUserAsync(Arg.Any<CancellationToken>()).Returns(me);
        var controller = Controller(store, sender, accessor);

        var result = await controller.SendTest(CancellationToken);

        result.Should().BeOfType<NoContentResult>();
        await sender.Received(1).SendAsync(Arg.Is<EmailMessage>(m => m != null && m.To == me.Email), CancellationToken);
    }

    [TestMethod]
    public async Task SendTest_WhenUnconfigured_ReturnsBadRequest()
    {
        var store = Substitute.For<IEmailSettingsStore>();
        var controller = Controller(store, Substitute.For<IEmailSender>(), Substitute.For<ICurrentUserAccessor>());

        var result = await controller.SendTest(CancellationToken);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [TestMethod]
    public async Task SendTest_WhenNoCurrentUser_ReturnsUnauthorized()
    {
        var store = Substitute.For<IEmailSettingsStore>();
        store.GetAsync(Arg.Any<CancellationToken>()).Returns(Sample());
        var accessor = Substitute.For<ICurrentUserAccessor>();
        // NSubstitute auto-substitutes interface returns; force null to mean "no authenticated user".
        accessor.GetCurrentUserAsync(Arg.Any<CancellationToken>()).Returns((IUser?)null);
        var controller = Controller(store, Substitute.For<IEmailSender>(), accessor);

        var result = await controller.SendTest(CancellationToken);

        result.Should().BeOfType<UnauthorizedResult>();
    }

    [TestMethod]
    public async Task SendTest_WhenSenderThrows_OutsideDevelopment_SuppressesRawMessage()
    {
        var store = Substitute.For<IEmailSettingsStore>();
        store.GetAsync(Arg.Any<CancellationToken>()).Returns(Sample());
        var sender = Substitute.For<IEmailSender>();
        sender.When(s => s.SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>()))
              .Do(_ => throw new InvalidOperationException("SMTP timeout connecting to smtp.internal:587"));
        var accessor = Substitute.For<ICurrentUserAccessor>();
        accessor.GetCurrentUserAsync(Arg.Any<CancellationToken>()).Returns(await CreateUserAsync());
        var controller = Controller(store, sender, accessor);

        var result = await controller.SendTest(CancellationToken);

        var message = result.Should().BeOfType<BadRequestObjectResult>().Subject.Value as string;
        message.Should().NotBeNull();
        // Outside Development the raw exception message (host/port/internal detail) must not leak.
        message.Should().NotContain("smtp.internal");
        message.Should().Contain("An unexpected error occurred");
    }

    [TestMethod]
    public async Task SendTest_WhenSenderThrows_InDevelopment_KeepsRawMessage()
    {
        var store = Substitute.For<IEmailSettingsStore>();
        store.GetAsync(Arg.Any<CancellationToken>()).Returns(Sample());
        var sender = Substitute.For<IEmailSender>();
        sender.When(s => s.SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>()))
              .Do(_ => throw new InvalidOperationException("SMTP timeout connecting to smtp.internal:587"));
        var accessor = Substitute.For<ICurrentUserAccessor>();
        accessor.GetCurrentUserAsync(Arg.Any<CancellationToken>()).Returns(await CreateUserAsync());
        var controller = Controller(store, sender, accessor, isDevelopment: true);

        var result = await controller.SendTest(CancellationToken);

        var message = result.Should().BeOfType<BadRequestObjectResult>().Subject.Value as string;
        message.Should().Contain("smtp.internal");
    }

    private EmailSettingsController Controller(
        IEmailSettingsStore store, IEmailSender sender, ICurrentUserAccessor accessor, bool isDevelopment = false)
        => new(
            store, sender, accessor, new EmailSettingsDtoMapper(),
            NullLogger<Audit>.Instance, NullLogger<EmailSettingsController>.Instance, Env(isDevelopment));

    private static IWebHostEnvironment Env(bool isDevelopment)
    {
        var env = Substitute.For<IWebHostEnvironment>();
        env.EnvironmentName.Returns(isDevelopment ? "Development" : "Production");
        return env;
    }

    private async Task<IUser> CreateUserAsync()
    {
        var services = GetServices();
        var create = services.GetRequiredService<IUser.CreateNew>();
        return await create($"{Guid.NewGuid():N}@example.test", null, "h", UserRole.Admin).AddAsync(CancellationToken);
    }

    private static EmailSettings Sample() => new(
        true, "smtp", 25, SmtpSecurity.None, null, null, "a@b.c", "PT", null, NotificationSeverity.Warning);
}
