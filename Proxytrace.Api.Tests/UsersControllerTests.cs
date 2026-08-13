using Proxytrace.Domain.AuditLog;
using AwesomeAssertions;
using Autofac;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Proxytrace.Api.Controllers;
using Proxytrace.Api.Dto.Users;
using Proxytrace.Application.Auth;
using Proxytrace.Application.Auth.Local;
using Proxytrace.Domain;
using Proxytrace.Domain.ModelEndpoint;
using Proxytrace.Domain.Notification;
using Proxytrace.Domain.Project;
using Proxytrace.Domain.User;
using Nordstein.Core.Testing;
// ReSharper disable NullableWarningSuppressionIsUsed

namespace Proxytrace.Api.Tests;

[TestClass]
public sealed class UsersControllerTests : BaseTest<Module>
{
    [TestMethod]
    public async Task GetAll_ReturnsSeededUsers()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);
        var u = await services.GetRequiredService<IDomainEntityGenerator<IUser>>().CreateAsync(CancellationToken);

        var result = await controller.GetAll(cancellationToken: CancellationToken);

        result.Items.Should().ContainSingle(x => x.Id == u.Id);
    }

    [TestMethod]
    public async Task Me_NoCurrentUser_ReturnsUnauthorized()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);

        var result = await controller.Me(CancellationToken);

        result.Result.Should().BeOfType<UnauthorizedResult>();
    }

    [TestMethod]
    public async Task Me_HasCurrentUser_ReturnsDto()
    {
        ICurrentUserAccessor accessor = null!;
        IServiceProvider services = GetServices(builder => accessor = RegisterAccessor(builder));
        var user = await services.GetRequiredService<IDomainEntityGenerator<IUser>>().CreateAsync(CancellationToken);
        accessor.GetCurrentUserAsync(Arg.Any<CancellationToken>()).Returns(user);

        var controller = ResolveController(services);
        var result = await controller.Me(CancellationToken);

        result.Value.Should().NotBeNull();
        result.Value.Id.Should().Be(user.Id);
    }

    [TestMethod]
    public async Task UpdateMyLanguage_Valid_PersistsAndReturnsNoContent()
    {
        ICurrentUserAccessor accessor = null!;
        IServiceProvider services = GetServices(builder => accessor = RegisterAccessor(builder));
        var user = await CreateUserAsync(services, UserRole.Member);
        accessor.GetCurrentUserAsync(Arg.Any<CancellationToken>()).Returns(user);
        var controller = ResolveController(services);

        var result = await controller.UpdateMyLanguage(new UpdateMyLanguageRequest("de"), CancellationToken);

        result.Should().BeOfType<NoContentResult>();
        var reloaded = await services.GetRequiredService<IRepository<IUser>>().GetAsync(user.Id, CancellationToken);
        reloaded.Language.Should().Be("de");
    }

    [TestMethod]
    public async Task UpdateMyEmailNotifications_Valid_PersistsAndReturnsNoContent()
    {
        var accessor = Substitute.For<ICurrentUserAccessor>();
        IServiceProvider services = GetServices(builder =>
            builder.RegisterInstance(accessor).As<ICurrentUserAccessor>());
        var user = await CreateUserAsync(services, UserRole.Member);
        accessor.GetCurrentUserAsync(Arg.Any<CancellationToken>()).Returns(user);
        var controller = ResolveController(services);

        var result = await controller.UpdateMyEmailNotifications(
            new UpdateMyEmailNotificationsRequest(false, NotificationSeverity.Critical),
            CancellationToken);

        result.Should().BeOfType<NoContentResult>();
        var reloaded = await services.GetRequiredService<IRepository<IUser>>().GetAsync(user.Id, CancellationToken);
        reloaded.EmailNotificationsEnabled.Should().BeFalse();
        reloaded.EmailNotificationMinSeverity.Should().Be(NotificationSeverity.Critical);
    }

    [TestMethod]
    public async Task UpdateMyEmailNotifications_NoCurrentUser_ReturnsUnauthorized()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);

        var result = await controller.UpdateMyEmailNotifications(
            new UpdateMyEmailNotificationsRequest(true, NotificationSeverity.Warning),
            CancellationToken);

        result.Should().BeOfType<UnauthorizedResult>();
    }

    [TestMethod]
    public async Task UpdateMyLanguage_Unsupported_ReturnsBadRequest()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);

        var result = await controller.UpdateMyLanguage(new UpdateMyLanguageRequest("xx"), CancellationToken);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [TestMethod]
    public async Task UpdateMyLanguage_NoCurrentUser_ReturnsUnauthorized()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);

        var result = await controller.UpdateMyLanguage(new UpdateMyLanguageRequest("de"), CancellationToken);

        result.Should().BeOfType<UnauthorizedResult>();
    }

    [TestMethod]
    public async Task Get_Unknown_ReturnsNotFound()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);

        var result = await controller.Get(Guid.NewGuid(), CancellationToken);

        result.Result.Should().BeOfType<NotFoundResult>();
    }

    [TestMethod]
    public async Task UpdateRole_AsAdmin_PromotesTarget_PersistsNewRole()
    {
        ICurrentUserAccessor accessor = null!;
        IServiceProvider services = GetServices(builder => accessor = RegisterAccessor(builder));
        var acting = await CreateUserAsync(services, UserRole.Admin);
        accessor.GetCurrentUserAsync(Arg.Any<CancellationToken>()).Returns(acting);
        var target = await CreateUserAsync(services, UserRole.Member);
        var controller = ResolveController(services);

        var result = await controller.UpdateRole(target.Id, new UpdateUserRoleRequest(UserRole.Admin), CancellationToken);

        result.Value.Should().NotBeNull();
        result.Value.Role.Should().Be(UserRole.Admin);
    }

    [TestMethod]
    public async Task UpdateRole_NoCurrentUser_ReturnsUnauthorized()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);

        var result = await controller.UpdateRole(Guid.NewGuid(), new UpdateUserRoleRequest(UserRole.Admin), CancellationToken);

        result.Result.Should().BeOfType<UnauthorizedResult>();
    }

    [TestMethod]
    public async Task UpdateRole_Unknown_ReturnsNotFound()
    {
        ICurrentUserAccessor accessor = null!;
        IServiceProvider services = GetServices(builder => accessor = RegisterAccessor(builder));
        var acting = await CreateUserAsync(services, UserRole.Admin);
        accessor.GetCurrentUserAsync(Arg.Any<CancellationToken>()).Returns(acting);
        var controller = ResolveController(services);

        var result = await controller.UpdateRole(Guid.NewGuid(), new UpdateUserRoleRequest(UserRole.Member), CancellationToken);

        result.Result.Should().BeOfType<NotFoundResult>();
    }

    [TestMethod]
    public async Task Delete_AsAdmin_RemovesTarget_ReturnsNoContent()
    {
        ICurrentUserAccessor accessor = null!;
        IServiceProvider services = GetServices(builder => accessor = RegisterAccessor(builder));
        var acting = await CreateUserAsync(services, UserRole.Admin);
        accessor.GetCurrentUserAsync(Arg.Any<CancellationToken>()).Returns(acting);
        var target = await CreateUserAsync(services, UserRole.Member);
        var controller = ResolveController(services);

        var result = await controller.Delete(target.Id, CancellationToken);

        result.Should().BeOfType<NoContentResult>();
    }

    [TestMethod]
    public async Task Delete_NoCurrentUser_ReturnsUnauthorized()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);

        var result = await controller.Delete(Guid.NewGuid(), CancellationToken);

        result.Should().BeOfType<UnauthorizedResult>();
    }

    [TestMethod]
    public async Task Delete_Unknown_ReturnsNotFound()
    {
        ICurrentUserAccessor accessor = null!;
        IServiceProvider services = GetServices(builder => accessor = RegisterAccessor(builder));
        var acting = await CreateUserAsync(services, UserRole.Admin);
        accessor.GetCurrentUserAsync(Arg.Any<CancellationToken>()).Returns(acting);
        var controller = ResolveController(services);

        var result = await controller.Delete(Guid.NewGuid(), CancellationToken);

        result.Should().BeOfType<NotFoundResult>();
    }

    [TestMethod]
    public async Task GetProjects_ReturnsProjectsTheUserBelongsTo()
    {
        IServiceProvider services = GetServices();
        var user = await CreateUserAsync(services, UserRole.Member);
        var endpoint = await services.GetRequiredService<IDomainEntityGenerator<IModelEndpoint>>().GetOrCreateAsync(CancellationToken);
        var createProject = services.GetRequiredService<IProject.CreateNew>();
        var project = createProject("Assigned project", endpoint, [user]);
        await services.GetRequiredService<IRepository<IProject>>().AddAsync(project, CancellationToken);
        var controller = ResolveController(services);

        var result = await controller.GetProjects(user.Id, CancellationToken);

        result.Value.Should().ContainSingle().Which.Id.Should().Be(project.Id);
    }

    [TestMethod]
    public async Task GetProjects_UnknownUser_ReturnsNotFound()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);

        var result = await controller.GetProjects(Guid.NewGuid(), CancellationToken);

        result.Result.Should().BeOfType<NotFoundResult>();
    }

    [TestMethod]
    public async Task CreateResetLink_BuildsAdminResetLinkFromAllowedOrigin()
    {
        Func<string, string>? capturedBuilder = null;
        var resetService = Substitute.For<IPasswordResetService>();
        resetService
            .IssueResetLinkAsync(Arg.Any<Guid>(), Arg.Do<Func<string, string>>(b => capturedBuilder = b), Arg.Any<CancellationToken>())
            .Returns(new PasswordResetLink("ignored", DateTimeOffset.UtcNow.AddHours(1)));

        IServiceProvider services = GetServices(builder => builder.RegisterInstance(resetService).As<IPasswordResetService>());
        var target = await CreateUserAsync(services, UserRole.Member);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Frontend:AllowedOrigin"] = "https://app.example.test",
            })
            .Build();
        var controller = ResolveController(services, config);

        await controller.CreateResetLink(target.Id, CancellationToken);

        // The admin-panel reset link shares the same fallback as the self-service one: with no
        // Frontend:BaseUrl it must build from the configured frontend origin, not the API host.
        capturedBuilder.Should().NotBeNull();
        var url = capturedBuilder?.Invoke("tok-123");
        url.Should().Be("https://app.example.test/reset-password?token=tok-123");
    }

    private static ICurrentUserAccessor RegisterAccessor(ContainerBuilder builder)
    {
        var accessor = Substitute.For<ICurrentUserAccessor>();
        builder.RegisterInstance(accessor).As<ICurrentUserAccessor>();
        return accessor;
    }

    private async Task<IUser> CreateUserAsync(IServiceProvider services, UserRole role)
    {
        var create = services.GetRequiredService<IUser.CreateNew>();
        var user = create($"{Guid.NewGuid():N}@example.test", externalSubject: null, passwordHash: "hash", role);
        return await services.GetRequiredService<IRepository<IUser>>().AddAsync(user, CancellationToken);
    }

    private static UsersController ResolveController(IServiceProvider services, IConfiguration? config = null) => new(
        services.GetRequiredService<IRepository<IUser>>(),
        services.GetRequiredService<IProjectRepository>(),
        services.GetRequiredService<IUserAdministrationService>(),
        services.GetRequiredService<ICurrentUserAccessor>(),
        services.GetRequiredService<IPasswordResetService>(),
        services.GetRequiredService<Proxytrace.Application.Auth.Local.IMfaService>(),
        services.GetRequiredService<Proxytrace.Domain.UserTotpEnrollment.IUserTotpEnrollmentRepository>(),
        config ?? new ConfigurationBuilder().Build(),
        Microsoft.Extensions.Logging.Abstractions.NullLogger<Proxytrace.Domain.AuditLog.Audit>.Instance);
}
