using AwesomeAssertions;
using Autofac;
using Microsoft.Extensions.DependencyInjection;
using Proxytrace.Application.Auth;
using Proxytrace.Application.Auth.Internal;
using Proxytrace.Domain;
using Proxytrace.Domain.User;
using Proxytrace.Testing;

namespace Proxytrace.Application.Tests;

[TestClass]
public sealed class UserAdministrationServiceTests : BaseTest<Module>
{
    [TestMethod]
    public async Task ChangeRoleAsync_UnknownUser_ReturnsNull()
    {
        IServiceProvider services = GetServices(Register);
        var service = services.GetRequiredService<IUserAdministrationService>();

        var result = await service.ChangeRoleAsync(Guid.NewGuid(), Guid.NewGuid(), UserRole.Admin, CancellationToken);

        result.Should().BeNull();
    }

    [TestMethod]
    public async Task ChangeRoleAsync_PromoteMember_PersistsAdmin()
    {
        IServiceProvider services = GetServices(Register);
        var service = services.GetRequiredService<IUserAdministrationService>();
        var acting = await CreateUserAsync(services, UserRole.Admin);
        var target = await CreateUserAsync(services, UserRole.Member);

        var updated = await service.ChangeRoleAsync(acting.Id, target.Id, UserRole.Admin, CancellationToken);

        updated.Should().NotBeNull();
        var stored = await services.GetRequiredService<IRepository<IUser>>().GetAsync(target.Id, CancellationToken);
        stored.Role.Should().Be(UserRole.Admin);
    }

    [TestMethod]
    public async Task ChangeRoleAsync_DemoteSelf_Throws()
    {
        IServiceProvider services = GetServices(Register);
        var service = services.GetRequiredService<IUserAdministrationService>();
        // Two admins, so the "last admin" rule cannot fire — isolates the self-demotion rule.
        var acting = await CreateUserAsync(services, UserRole.Admin);
        await CreateUserAsync(services, UserRole.Admin);

        await FluentActions
            .Invoking(() => service.ChangeRoleAsync(acting.Id, acting.Id, UserRole.Member, CancellationToken))
            .Should().ThrowAsync<UserAdministrationException>();
    }

    [TestMethod]
    public async Task ChangeRoleAsync_DemoteLastAdmin_Throws()
    {
        IServiceProvider services = GetServices(Register);
        var service = services.GetRequiredService<IUserAdministrationService>();
        var acting = await CreateUserAsync(services, UserRole.Member);
        var onlyAdmin = await CreateUserAsync(services, UserRole.Admin);

        await FluentActions
            .Invoking(() => service.ChangeRoleAsync(acting.Id, onlyAdmin.Id, UserRole.Member, CancellationToken))
            .Should().ThrowAsync<UserAdministrationException>();
    }

    [TestMethod]
    public async Task ChangeRoleAsync_DemoteAdmin_WhenOtherAdminsExist_Succeeds()
    {
        IServiceProvider services = GetServices(Register);
        var service = services.GetRequiredService<IUserAdministrationService>();
        var acting = await CreateUserAsync(services, UserRole.Member);
        var admin1 = await CreateUserAsync(services, UserRole.Admin);
        await CreateUserAsync(services, UserRole.Admin);

        var updated = await service.ChangeRoleAsync(acting.Id, admin1.Id, UserRole.Member, CancellationToken);

        // ReSharper disable once NullableWarningSuppressionIsUsed
        updated!.Role.Should().Be(UserRole.Member);
    }

    [TestMethod]
    public async Task RemoveAsync_Self_Throws()
    {
        IServiceProvider services = GetServices(Register);
        var service = services.GetRequiredService<IUserAdministrationService>();
        var acting = await CreateUserAsync(services, UserRole.Admin);
        await CreateUserAsync(services, UserRole.Admin);

        await FluentActions
            .Invoking(() => service.RemoveAsync(acting.Id, acting.Id, CancellationToken))
            .Should().ThrowAsync<UserAdministrationException>();
    }

    [TestMethod]
    public async Task RemoveAsync_LastAdmin_Throws()
    {
        IServiceProvider services = GetServices(Register);
        var service = services.GetRequiredService<IUserAdministrationService>();
        var acting = await CreateUserAsync(services, UserRole.Member);
        var onlyAdmin = await CreateUserAsync(services, UserRole.Admin);

        await FluentActions
            .Invoking(() => service.RemoveAsync(acting.Id, onlyAdmin.Id, CancellationToken))
            .Should().ThrowAsync<UserAdministrationException>();
    }

    [TestMethod]
    public async Task RemoveAsync_Member_Succeeds()
    {
        IServiceProvider services = GetServices(Register);
        var service = services.GetRequiredService<IUserAdministrationService>();
        var acting = await CreateUserAsync(services, UserRole.Admin);
        var target = await CreateUserAsync(services, UserRole.Member);

        var removed = await service.RemoveAsync(acting.Id, target.Id, CancellationToken);

        removed.Should().BeTrue();
        var exists = await services.GetRequiredService<IRepository<IUser>>().ContainsAsync(target.Id, CancellationToken);
        exists.Should().BeFalse();
    }

    [TestMethod]
    public async Task RemoveAsync_Unknown_ReturnsFalse()
    {
        IServiceProvider services = GetServices(Register);
        var service = services.GetRequiredService<IUserAdministrationService>();
        var acting = await CreateUserAsync(services, UserRole.Admin);

        var removed = await service.RemoveAsync(acting.Id, Guid.NewGuid(), CancellationToken);

        removed.Should().BeFalse();
    }

    [TestMethod]
    public async Task RemoveAsync_WhenTheUserOwnsApiKeys_IsRefusedAndKeepsTheUser()
    {
        // Deleting a user used to cascade-delete every API key they owned. Only the key hash is
        // stored, so those keys are unrecoverable — routine offboarding silently revoked live
        // integrations, which surfaced later as unexplained 401s. The delete is now refused with a
        // message naming the keys so an operator deals with them deliberately.
        IServiceProvider services = GetServices(Register);
        var service = services.GetRequiredService<IUserAdministrationService>();
        var acting = await CreateUserAsync(services, UserRole.Admin);
        var target = await CreateUserAsync(services, UserRole.Member);
        await CreateApiKeyAsync(services, target, "ci-pipeline");

        var act = () => service.RemoveAsync(acting.Id, target.Id, CancellationToken);

        (await act.Should().ThrowAsync<UserAdministrationException>())
            .Which.Message.Should().Contain("ci-pipeline");

        (await services.GetRequiredService<IRepository<IUser>>().ContainsAsync(target.Id, CancellationToken))
            .Should().BeTrue("the user must survive a refused deletion");
    }

    [TestMethod]
    public async Task RemoveAsync_WhenTheUserOwnsNoApiKeys_StillDeletes()
    {
        IServiceProvider services = GetServices(Register);
        var service = services.GetRequiredService<IUserAdministrationService>();
        var acting = await CreateUserAsync(services, UserRole.Admin);
        var target = await CreateUserAsync(services, UserRole.Member);

        var removed = await service.RemoveAsync(acting.Id, target.Id, CancellationToken);

        removed.Should().BeTrue();
    }

    private static void Register(ContainerBuilder builder) =>
        builder.RegisterType<UserAdministrationService>().As<IUserAdministrationService>();

    private async Task CreateApiKeyAsync(IServiceProvider services, IUser owner, string name)
    {
        var project = await services.GetRequiredService<IDomainEntityGenerator<Domain.Project.IProject>>()
            .GetOrCreateAsync(CancellationToken);
        var provider = await services.GetRequiredService<IDomainEntityGenerator<Domain.ModelProvider.IModelProvider>>()
            .GetOrCreateAsync(CancellationToken);
        var create = services.GetRequiredService<Domain.ApiKey.IApiKey.CreateNew>();
        var key = create(name, $"hash-{Guid.NewGuid():N}", "prefix", project, provider,
            Domain.ApiKey.ApiKeyScopes.Ingestion, owner);
        await services.GetRequiredService<Domain.ApiKey.IApiKeyRepository>().AddAsync(key, CancellationToken);
    }

    private async Task<IUser> CreateUserAsync(IServiceProvider services, UserRole role)
    {
        var create = services.GetRequiredService<IUser.CreateNew>();
        var user = create($"{Guid.NewGuid():N}@example.test", externalSubject: null, passwordHash: "hash", role);
        return await services.GetRequiredService<IRepository<IUser>>().AddAsync(user, CancellationToken);
    }
}
