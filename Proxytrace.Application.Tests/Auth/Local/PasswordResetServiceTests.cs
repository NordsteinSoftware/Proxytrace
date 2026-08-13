using Proxytrace.Domain.Notifications;
using Autofac;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Proxytrace.Application.Auth.Local;
using Proxytrace.Application.Notifications;
using Proxytrace.Domain;
using Proxytrace.Domain.Notification;
using Proxytrace.Domain.PasswordResetToken;
using Proxytrace.Domain.User;
using Nordstein.Core.Testing;

namespace Proxytrace.Application.Tests.Auth.Local;

[TestClass]
public sealed class PasswordResetServiceTests : BaseTest<Module>
{
    private const string OldPassword = "Abcdef1!";
    private const string NewPassword = "NewPassw0rd!";

    [TestMethod]
    public async Task RequestReset_WithKnownEmail_SmtpOff_PersistsTokenWithoutEmailing()
    {
        var sender = Substitute.For<IEmailSender>();
        var services = GetServices(b => b.RegisterInstance(sender).As<IEmailSender>());
        await SeedUser(services, "u@b.com");
        var svc = services.GetRequiredService<IPasswordResetService>();

        // Default IEmailSettingsStore stub returns null => SMTP is "not configured" (log path).
        await svc.RequestResetAsync("u@b.com", Url, CancellationToken);

        var tokens = await services.GetRequiredService<IRepository<IPasswordResetToken>>().GetAllAsync(CancellationToken);
        tokens.Should().HaveCount(1);
        await sender.DidNotReceive().SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task RequestReset_WithKnownEmail_SmtpEnabled_EmailsResetLink()
    {
        var sender = Substitute.For<IEmailSender>();
        var store = Substitute.For<IEmailSettingsStore>();
        store.GetAsync(Arg.Any<CancellationToken>()).Returns(EnabledSettings());

        var services = GetServices(b =>
        {
            b.RegisterInstance(sender).As<IEmailSender>();
            b.RegisterInstance(store).As<IEmailSettingsStore>();
        });
        await SeedUser(services, "u@b.com");
        var svc = services.GetRequiredService<IPasswordResetService>();

        string? sentUrl = null;
        await svc.RequestResetAsync("u@b.com", t => sentUrl = $"https://app.test/reset-password?token={t}", CancellationToken);

        sentUrl.Should().NotBeNull();
        await sender.Received(1).SendAsync(
            Arg.Is<EmailMessage>(m => m != null && m.To == "u@b.com" && m.HtmlBody.Contains(sentUrl) && m.TextBody.Contains(sentUrl)),
            CancellationToken);
    }

    [TestMethod]
    public async Task RequestReset_WithUnknownEmail_IssuesNothing()
    {
        var sender = Substitute.For<IEmailSender>();
        var services = GetServices(b => b.RegisterInstance(sender).As<IEmailSender>());
        var svc = services.GetRequiredService<IPasswordResetService>();

        var built = false;
        await svc.RequestResetAsync("nobody@b.com", t => { built = true; return t; }, CancellationToken);

        built.Should().BeFalse();
        (await services.GetRequiredService<IRepository<IPasswordResetToken>>().GetAllAsync(CancellationToken)).Should().BeEmpty();
        await sender.DidNotReceive().SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task CompleteReset_WithValidToken_SetsNewPasswordConsumesTokenAndIssuesSession()
    {
        var services = GetServices();
        var user = await SeedUser(services, "u@b.com");
        var svc = services.GetRequiredService<IPasswordResetService>();

        string rawToken = string.Empty;
        await svc.RequestResetAsync("u@b.com", t => rawToken = t, CancellationToken);

        var result = await svc.CompleteResetAsync(rawToken, NewPassword, CancellationToken);

        // No MFA on this account → the reset issues a session directly.
        result.Should().BeOfType<LoginSucceeded>();
        ((LoginSucceeded)result!).Token.Should().NotBeNullOrEmpty();

        var passwords = services.GetRequiredService<IPasswordService>();
        var stored = await services.GetRequiredService<IRepository<IUser>>().GetAsync(user.Id, CancellationToken);
        var hash = stored.PasswordHash ?? throw new InvalidOperationException("expected a password hash");
        passwords.Verify(stored, hash, NewPassword).Should().BeTrue();
        passwords.Verify(stored, hash, OldPassword).Should().BeFalse();
    }

    [TestMethod]
    public async Task CompleteReset_WithAlreadyUsedToken_ReturnsNull()
    {
        var services = GetServices();
        await SeedUser(services, "u@b.com");
        var svc = services.GetRequiredService<IPasswordResetService>();

        string rawToken = string.Empty;
        await svc.RequestResetAsync("u@b.com", t => rawToken = t, CancellationToken);
        await svc.CompleteResetAsync(rawToken, NewPassword, CancellationToken);

        var second = await svc.CompleteResetAsync(rawToken, "Another0ne!", CancellationToken);
        second.Should().BeNull();
    }

    [TestMethod]
    public async Task CompleteReset_WithUnknownToken_ReturnsNull()
    {
        var services = GetServices();
        var svc = services.GetRequiredService<IPasswordResetService>();

        (await svc.CompleteResetAsync("not-a-real-token", NewPassword, CancellationToken)).Should().BeNull();
    }

    [TestMethod]
    public async Task IssueResetLink_WithKnownUser_ReturnsUsableLink()
    {
        var services = GetServices();
        var user = await SeedUser(services, "u@b.com");
        var svc = services.GetRequiredService<IPasswordResetService>();

        string rawToken = string.Empty;
        var link = await svc.IssueResetLinkAsync(user.Id, t => { rawToken = t; return $"https://app.test/reset-password?token={t}"; }, CancellationToken);

        link.Should().NotBeNull();
        link.Link.Should().Contain("reset-password?token=");
        link.ExpiresAt.Should().BeAfter(DateTimeOffset.UtcNow);

        var completed = await svc.CompleteResetAsync(rawToken, NewPassword, CancellationToken);
        completed.Should().NotBeNull();
    }

    [TestMethod]
    public async Task IssueResetLink_WithUnknownUser_ReturnsNull()
    {
        var services = GetServices();
        var svc = services.GetRequiredService<IPasswordResetService>();

        (await svc.IssueResetLinkAsync(Guid.NewGuid(), Url, CancellationToken)).Should().BeNull();
    }

    private async Task<IUser> SeedUser(IServiceProvider services, string email)
    {
        var passwords = services.GetRequiredService<IPasswordService>();
        var factory = services.GetRequiredService<IUser.CreateNew>();
        var draft = factory(email, null, "x", UserRole.Member);
        var hash = passwords.Hash(draft, OldPassword);
        return await factory(email, null, hash, UserRole.Member).AddAsync(CancellationToken);
    }

    private static string Url(string token) => $"https://app.test/reset-password?token={token}";

    private static EmailSettings EnabledSettings() => new(
        Enabled: true, SmtpHost: "smtp", SmtpPort: 25, Security: SmtpSecurity.None,
        Username: null, Password: null, FromAddress: "a@b.c", FromName: "PT",
        AppBaseUrl: "https://app.test", MinSeverity: NotificationSeverity.Warning);
}
