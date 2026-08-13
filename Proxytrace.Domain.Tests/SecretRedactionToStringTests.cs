using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Proxytrace.Domain.Kiosk;
using Proxytrace.Domain.ModelProvider;
using Proxytrace.Domain.Notification;
using Proxytrace.Domain.Notifications;
using Proxytrace.Domain.User;
using Proxytrace.Domain.UserTotpEnrollment;
using Nordstein.Core.Testing;

namespace Proxytrace.Domain.Tests;

/// <summary>
/// A record's generated ToString() prints every member, so any secret-bearing record leaks its
/// plaintext the first time someone writes <c>logger.LogX("... {Settings}", settings)</c> or
/// interpolates it into an exception message. Every record that carries a replayable secret must
/// therefore override PrintMembers and mask it, the way ModelProvider does. See docs/security.md.
/// </summary>
[TestClass]
public sealed class SecretRedactionToStringTests : DomainTest<Module>
{
    [TestMethod]
    public void EmailSettings_ToString_RedactsSmtpPassword()
    {
        var settings = new EmailSettings(
            Enabled: true,
            SmtpHost: "smtp.example.com",
            SmtpPort: 587,
            Security: SmtpSecurity.StartTls,
            Username: "postmaster@example.com",
            Password: "super-secret-smtp-password",
            FromAddress: "noreply@example.com",
            FromName: "Proxytrace",
            AppBaseUrl: "https://proxytrace.example.com",
            MinSeverity: NotificationSeverity.Warning);

        var text = settings.ToString();

        text.Should().NotContain("super-secret-smtp-password");
        text.Should().Contain("Password = ***");
        // The non-secret members still render, so the type stays useful in a log line.
        text.Should().Contain("smtp.example.com").And.Contain("postmaster@example.com");
    }

    [TestMethod]
    public void KioskEndpointOptions_ToString_RedactsApiKey()
    {
        var options = new KioskEndpointOptions
        {
            BaseUrl = "https://api.openai.com/v1",
            ApiKey = "sk-kiosk-secret-value",
            Model = "gpt-4o",
        };

        var text = options.ToString();

        text.Should().NotContain("sk-kiosk-secret-value");
        text.Should().Contain("ApiKey = ***");
        text.Should().Contain("gpt-4o");
    }

    [TestMethod]
    public void ResolvedKioskEndpoint_ToString_RedactsApiKey()
    {
        var resolved = new KioskEndpointOptions
        {
            BaseUrl = "https://api.openai.com/v1",
            ApiKey = "sk-kiosk-secret-value",
            Model = "gpt-4o",
        }.Resolve();

        var text = resolved.ToString();

        text.Should().NotContain("sk-kiosk-secret-value");
        text.Should().Contain("ApiKey = ***");
        text.Should().Contain("gpt-4o");
    }

    [TestMethod]
    public async Task UserTotpEnrollment_ToString_RedactsSharedSecret()
    {
        IServiceProvider services = GetServices();
        var enrollment = await services
            .GetRequiredService<IDomainEntityGenerator<IUserTotpEnrollment>>()
            .CreateAsync(CancellationToken);

        var text = enrollment.ToString();

        text.Should().NotBeNull();
        text.Should().NotContain(enrollment.Secret);
        text.Should().Contain("Secret = ***");
    }

    [TestMethod]
    public void User_ToString_RedactsPasswordHash()
    {
        IServiceProvider services = GetServices();
        var create = services.GetRequiredService<IUser.CreateNew>();

        var user = create(
            email: "operator@example.com",
            externalSubject: "https://issuer.example.com|subject-1234",
            passwordHash: "AQAAAAIAAYagAAAAEsuper-secret-password-hash",
            role: UserRole.Admin);

        var text = user.ToString();

        text.Should().NotBeNull();
        text.Should().NotContain("AQAAAAIAAYagAAAAEsuper-secret-password-hash");
        text.Should().Contain("PasswordHash = ***");
        // Identifiers are not secrets: they stay readable so a log line still says who this is.
        text.Should().Contain("operator@example.com")
            .And.Contain("https://issuer.example.com|subject-1234");
    }

    [TestMethod]
    public async Task ModelProvider_ToString_RedactsApiKey()
    {
        // Pins the reference implementation the five peers above mirror.
        IServiceProvider services = GetServices();
        var provider = await services
            .GetRequiredService<IDomainEntityGenerator<IModelProvider>>()
            .CreateAsync(CancellationToken);

        var text = provider.ToString();

        text.Should().NotBeNull();
        text.Should().NotContain(provider.ApiKey);
        text.Should().Contain("ApiKey = ***");
    }
}
