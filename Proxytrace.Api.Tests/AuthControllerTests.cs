using Proxytrace.Domain.AuditLog;
using Proxytrace.Domain.Notifications;
using AwesomeAssertions;
using Autofac;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Proxytrace.Api.Auth;
using Proxytrace.Api.Controllers;
using Proxytrace.Api.Dto.Auth;
using Proxytrace.Application.Auth;
using Proxytrace.Application.Auth.Local;
using Proxytrace.Application.Setup;
using Proxytrace.Domain;
using Proxytrace.Domain.Invite;
using Proxytrace.Domain.Notification;
using Proxytrace.Domain.User;
using Proxytrace.Licensing;
using Nordstein.Core.Testing;

namespace Proxytrace.Api.Tests;

[TestClass]
public sealed class AuthControllerTests : BaseTest<Module>
{
    [TestMethod]
    public async Task GetMode_NoUsers_ReportsSetupRequired()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);

        var mode = await controller.GetMode(CancellationToken);

        mode.Mode.Should().Be("local");
        mode.SetupRequired.Should().BeTrue();
    }

    [TestMethod]
    public async Task GetMode_WithUser_NoSetupRequired()
    {
        IServiceProvider services = GetServices();
        await services.GetRequiredService<IDomainEntityGenerator<IUser>>().CreateAsync(CancellationToken);
        var controller = ResolveController(services);

        var mode = await controller.GetMode(CancellationToken);

        mode.SetupRequired.Should().BeFalse();
    }

    [TestMethod]
    public async Task Login_BadCreds_ReturnsUnauthorized()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);

        var result = await controller.Login(new LoginRequest("nobody@example.com", "wrongpassword"), CancellationToken);

        result.Result.Should().BeOfType<UnauthorizedResult>();
    }

    [TestMethod]
    public async Task Setup_WeakPassword_ReturnsBadRequest()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);

        var result = await controller.Setup(new SetupAdminRequest("admin@example.com", "a"), CancellationToken);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [TestMethod]
    public async Task Setup_AfterUsersExist_ReturnsConflict()
    {
        IServiceProvider services = GetServices();
        await services.GetRequiredService<IDomainEntityGenerator<IUser>>().CreateAsync(CancellationToken);
        var controller = ResolveController(services);

        var result = await controller.Setup(new SetupAdminRequest("admin@example.com", "StrongPass1!Foo"), CancellationToken);

        result.Result.Should().BeOfType<ConflictObjectResult>();
    }

    [TestMethod]
    public async Task ClaimLegacy_WeakPassword_ReturnsBadRequest()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);

        var result = await controller.ClaimLegacy(new ClaimLegacyRequest("x@y.com", "1"), CancellationToken);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [TestMethod]
    public async Task Login_WhenMfaEnabled_ReturnsChallenge_ThenMfaVerifyIssuesSession()
    {
        IServiceProvider services = GetServices();

        // Seed a local user and enable MFA for them.
        var passwords = services.GetRequiredService<IPasswordService>();
        var create = services.GetRequiredService<IUser.CreateNew>();
        const string email = "mfa@example.test";
        const string password = "StrongPass1!Foo";
        var hash = passwords.Hash(create(email, null, "x", UserRole.Member), password);
        var user = await create(email, null, hash, UserRole.Member).AddAsync(CancellationToken);

        var mfa = services.GetRequiredService<IMfaService>();
        var setup = await mfa.SetupAsync(user, CancellationToken)
            ?? throw new InvalidOperationException("setup returned null");
        var backupCodes = await mfa.ActivateAsync(
                user,
                new OtpNet.Totp(OtpNet.Base32Encoding.ToBytes(setup.Secret)).ComputeTotp(),
                CancellationToken)
            ?? throw new InvalidOperationException("activate returned null");

        var controller = ResolveController(services);

        // Step 1: password login returns an MFA challenge, not a session.
        var login = await controller.Login(new LoginRequest(email, password), CancellationToken);
        var challenge = login.Value ?? throw new InvalidOperationException("no login body");
        challenge.MfaRequired.Should().BeTrue();
        challenge.Token.Should().BeNull();
        var challengeToken = challenge.MfaChallengeToken;
        challengeToken.Should().NotBeNullOrEmpty();

        // Step 2: completing the challenge (here with a backup code) issues a session.
        var verified = await controller.MfaVerify(
            new MfaVerifyRequest(challengeToken ?? string.Empty, backupCodes[0]),
            CancellationToken);
        var session = verified.Value ?? throw new InvalidOperationException("no verify body");
        session.MfaRequired.Should().BeFalse();
        session.Token.Should().NotBeNullOrEmpty();
    }

    [TestMethod]
    public async Task Preview_UnknownToken_ReturnsGone()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);

        var result = await controller.Preview("nonsense", CancellationToken);

        var status = (StatusCodeResult)(result.Result ?? throw new InvalidOperationException());
        status.StatusCode.Should().Be(410);
    }

    [TestMethod]
    public async Task Signup_WeakPassword_ReturnsBadRequest()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);

        var result = await controller.Signup(new SignupRequest("anything", "1"), CancellationToken);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [TestMethod]
    public async Task Signup_BadToken_ReturnsGone()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);

        var result = await controller.Signup(new SignupRequest("does-not-exist", "StrongPass1!Foo"), CancellationToken);

        var status = (ObjectResult)(result.Result ?? throw new InvalidOperationException());
        status.StatusCode.Should().Be(410);
    }

    [TestMethod]
    public async Task Setup_ValidRequest_SetsHttpOnlySessionCookie()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);

        var result = await controller.Setup(new SetupAdminRequest("admin@example.com", "StrongPass1!Foo"), CancellationToken);

        result.Value.Should().NotBeNull();
        var setCookie = controller.Response.Headers.SetCookie.ToString();
        setCookie.Should().Contain("proxytrace_session=");
        setCookie.ToLowerInvariant().Should().Contain("httponly").And.Contain("samesite=strict");
    }

    [TestMethod]
    public async Task Login_ValidCreds_SetsSessionCookieWithToken()
    {
        IServiceProvider services = GetServices();
        var setupController = ResolveController(services);
        await setupController.Setup(new SetupAdminRequest("admin@example.com", "StrongPass1!Foo"), CancellationToken);

        var controller = ResolveController(services);
        var result = await controller.Login(new LoginRequest("admin@example.com", "StrongPass1!Foo"), CancellationToken);

        result.Value.Should().NotBeNull();
        var setCookie = controller.Response.Headers.SetCookie.ToString();
        setCookie.Should().Contain($"proxytrace_session={result.Value.Token}");
    }

    [TestMethod]
    public async Task Login_BadCreds_SetsNoCookie()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);

        await controller.Login(new LoginRequest("nobody@example.com", "wrongpassword"), CancellationToken);

        controller.Response.Headers.SetCookie.ToString().Should().BeEmpty();
    }

    [TestMethod]
    public void Logout_ClearsSessionCookie()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);

        var result = controller.Logout();

        result.Should().BeOfType<NoContentResult>();
        var setCookie = controller.Response.Headers.SetCookie.ToString();
        setCookie.Should().Contain("proxytrace_session=;");
    }

    [TestMethod]
    public async Task Me_WithoutAuthenticatedUser_ReturnsUnauthorized()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);

        var result = await controller.Me(CancellationToken);

        result.Result.Should().BeOfType<UnauthorizedResult>();
    }

    [TestMethod]
    public async Task Me_WithAuthenticatedUser_IncludesLanguage()
    {
        var accessor = Substitute.For<ICurrentUserAccessor>();
        IServiceProvider services = GetServices(builder => builder.RegisterInstance(accessor).As<ICurrentUserAccessor>());
        var create = services.GetRequiredService<IUser.CreateNew>();
        var user = create($"{Guid.NewGuid():N}@example.test", externalSubject: null, passwordHash: "hash", UserRole.Member, "de");
        await services.GetRequiredService<IRepository<IUser>>().AddAsync(user, CancellationToken);
        accessor.GetCurrentUserAsync(Arg.Any<CancellationToken>()).Returns(user);

        var result = await ResolveController(services).Me(CancellationToken);

        result.Value.Should().NotBeNull();
        result.Value.Language.Should().Be("de");
    }

    [TestMethod]
    public async Task Me_IncludesEmailPreferences_AndOperatorEnabledFlag()
    {
        var accessor = Substitute.For<ICurrentUserAccessor>();
        IServiceProvider services = GetServices(builder => builder.RegisterInstance(accessor).As<ICurrentUserAccessor>());
        var create = services.GetRequiredService<IUser.CreateNew>();
        var user = create($"{Guid.NewGuid():N}@example.test", externalSubject: null, passwordHash: "hash",
            UserRole.Member, "en", emailNotificationsEnabled: false, emailNotificationMinSeverity: NotificationSeverity.Critical);
        await services.GetRequiredService<IRepository<IUser>>().AddAsync(user, CancellationToken);
        accessor.GetCurrentUserAsync(Arg.Any<CancellationToken>()).Returns(user);

        var store = Substitute.For<IEmailSettingsStore>();
        store.GetAsync(Arg.Any<CancellationToken>()).Returns(EnabledSettings());

        var result = await ResolveController(services, store).Me(CancellationToken);

        result.Value.Should().NotBeNull();
        result.Value.EmailNotificationsEnabled.Should().BeFalse();
        result.Value.EmailNotificationMinSeverity.Should().Be(NotificationSeverity.Critical);
        result.Value.EmailEnabled.Should().BeTrue();
    }

    [TestMethod]
    public async Task CreateInvite_FallsBackToAllowedOriginForLink()
    {
        var accessor = Substitute.For<ICurrentUserAccessor>();
        // The default (Free) license caps MaxUsers at 1; with the admin seeded below the invite
        // would trip the limit before reaching the link-building logic under test. Lift the cap so
        // the invite path is reachable — the limit itself is covered by InviteServiceTests.
        var license = Substitute.For<ILicenseService>();
        license.GetLimit(Arg.Any<LicenseLimit>()).Returns(long.MaxValue);
        IServiceProvider services = GetServices(builder =>
        {
            builder.RegisterInstance(accessor).As<ICurrentUserAccessor>();
            builder.RegisterInstance(license).As<ILicenseService>();
        });
        var admin = await services.GetRequiredService<IDomainEntityGenerator<IUser>>().CreateAsync(CancellationToken);
        accessor.GetCurrentUserAsync(Arg.Any<CancellationToken>()).Returns(admin);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Frontend:AllowedOrigin"] = "https://app.example.test",
            })
            .Build();
        var controller = ResolveController(services, config: config);

        var result = await controller.Create(new CreateInviteRequest("invitee@example.com", UserRole.Member), CancellationToken);

        // No Frontend:BaseUrl is set, so the link must use the configured frontend origin — not the
        // API server's own host (which would point the browser at the wrong port).
        result.Value.Should().NotBeNull();
        result.Value.Url.Should().StartWith("https://app.example.test/signup?token=");
    }

    [TestMethod]
    public async Task ForgotPassword_BuildsResetLinkFromAllowedOrigin()
    {
        Func<string, string>? capturedBuilder = null;
        var resetService = Substitute.For<IPasswordResetService>();
        resetService
            .RequestResetAsync(Arg.Any<string>(), Arg.Do<Func<string, string>>(b => capturedBuilder = b), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        IServiceProvider services = GetServices(builder => builder.RegisterInstance(resetService).As<IPasswordResetService>());
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Frontend:AllowedOrigin"] = "https://app.example.test",
            })
            .Build();
        var controller = ResolveController(services, config: config);

        await controller.ForgotPassword(new ForgotPasswordRequest("user@example.com"), CancellationToken);

        // No Frontend:BaseUrl is set, so the reset link the controller hands the service must build
        // from the configured frontend origin — not the API server's own host (the wrong-port bug).
        capturedBuilder.Should().NotBeNull();
        var url = capturedBuilder?.Invoke("tok-123");
        url.Should().Be("https://app.example.test/reset-password?token=tok-123");
    }

    [TestMethod]
    public async Task ForgotPassword_PrefersExplicitBaseUrlOverAllowedOrigin()
    {
        Func<string, string>? capturedBuilder = null;
        var resetService = Substitute.For<IPasswordResetService>();
        resetService
            .RequestResetAsync(Arg.Any<string>(), Arg.Do<Func<string, string>>(b => capturedBuilder = b), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        IServiceProvider services = GetServices(builder => builder.RegisterInstance(resetService).As<IPasswordResetService>());
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Frontend:BaseUrl"] = "https://explicit.example.test",
                ["Frontend:AllowedOrigin"] = "https://app.example.test",
            })
            .Build();
        var controller = ResolveController(services, config: config);

        await controller.ForgotPassword(new ForgotPasswordRequest("user@example.com"), CancellationToken);

        // Frontend:BaseUrl is the explicit override and must win over AllowedOrigin when both are set.
        capturedBuilder.Should().NotBeNull();
        var url = capturedBuilder?.Invoke("tok");
        url.Should().Be("https://explicit.example.test/reset-password?token=tok");
    }

    [TestMethod]
    public async Task ForgotPassword_WithNoFrontendConfig_FallsBackToRequestHost()
    {
        Func<string, string>? capturedBuilder = null;
        var resetService = Substitute.For<IPasswordResetService>();
        resetService
            .RequestResetAsync(Arg.Any<string>(), Arg.Do<Func<string, string>>(b => capturedBuilder = b), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        IServiceProvider services = GetServices(builder => builder.RegisterInstance(resetService).As<IPasswordResetService>());
        var controller = ResolveController(services);
        controller.ControllerContext.HttpContext.Request.Scheme = "https";
        controller.ControllerContext.HttpContext.Request.Host = new HostString("backend.local:5000");

        await controller.ForgotPassword(new ForgotPasswordRequest("user@example.com"), CancellationToken);

        // With neither Frontend key set, the documented last resort is the request host. Pinning this
        // keeps the three-tier fallback honest: dropping or reordering a tier would change this URL.
        capturedBuilder.Should().NotBeNull();
        var url = capturedBuilder?.Invoke("tok");
        url.Should().Be("https://backend.local:5000/reset-password?token=tok");
    }

    private static AuthController ResolveController(
        IServiceProvider services,
        IEmailSettingsStore? emailSettings = null,
        IConfiguration? config = null) => new(
        new AuthOptions(),
        services.GetRequiredService<ISetupService>(),
        services.GetRequiredService<ILoginService>(),
        services.GetRequiredService<ILegacyClaimService>(),
        services.GetRequiredService<IInviteService>(),
        services.GetRequiredService<IInviteRepository>(),
        services.GetRequiredService<IPasswordResetService>(),
        services.GetRequiredService<IMfaService>(),
        services.GetRequiredService<IPasswordPolicy>(),
        services.GetRequiredService<ICurrentUserAccessor>(),
        services.GetRequiredService<IStreamTicketService>(),
        config ?? new ConfigurationBuilder().Build(),
        Microsoft.Extensions.Logging.Abstractions.NullLogger<Proxytrace.Domain.AuditLog.Audit>.Instance,
        emailSettings ?? Substitute.For<IEmailSettingsStore>(),
        services.GetRequiredService<ISessionCookie>())
    {
        ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
    };

    private static EmailSettings EnabledSettings() => new(
        Enabled: true, SmtpHost: "smtp", SmtpPort: 25, Security: SmtpSecurity.None,
        Username: null, Password: null, FromAddress: "a@b.c", FromName: "PT",
        AppBaseUrl: null, MinSeverity: NotificationSeverity.Warning);
}
