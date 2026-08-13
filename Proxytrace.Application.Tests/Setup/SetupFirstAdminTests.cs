using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Proxytrace.Application.Setup;
using Proxytrace.Domain;
using Proxytrace.Domain.User;
using Nordstein.Core.Testing;

namespace Proxytrace.Application.Tests.Setup;

[TestClass]
public sealed class SetupFirstAdminTests : BaseTest<Module>
{
    [TestMethod]
    public async Task CreateFirstAdmin_FromEmptyDb_CreatesAdmin()
    {
        var s = GetServices();
        var svc = s.GetRequiredService<ISetupService>();
        var users = s.GetRequiredService<IUserRepository>();

        var result = await svc.CreateFirstAdminAsync("admin@proxytrace.local", "Abcdef1!", CancellationToken);

        result.UserId.Should().NotBe(Guid.Empty);
        result.Token.Should().NotBeNullOrEmpty();
        var user = await users.FindByEmailAsync("admin@proxytrace.local", CancellationToken);
        user.Should().NotBeNull();
        user.Role.Should().Be(UserRole.Admin);
    }

#if DEBUG
    [TestMethod]
    public async Task AnyUsersExist_WithOnlyTheDebugBackDoorAdmin_IsFalse()
    {
        var s = GetServices();
        var createUser = s.GetRequiredService<IUser.CreateNew>();
        await createUser(DebugBackDoorAccount.Email, externalSubject: null, passwordHash: "hash", role: UserRole.Admin)
            .AddAsync(CancellationToken);

        var svc = s.GetRequiredService<ISetupService>();

        // The DEBUG-only seeded back-door account must not look like a completed first-run setup,
        // or a fresh dev database skips onboarding entirely. See docs/debug_api.md.
        (await svc.AnyUsersExistAsync(CancellationToken)).Should().BeFalse();
    }

    [TestMethod]
    public async Task AnyUsersExist_WithDebugBackDoorAdminAndARealUser_IsTrue()
    {
        var s = GetServices();
        var createUser = s.GetRequiredService<IUser.CreateNew>();
        await createUser(DebugBackDoorAccount.Email, externalSubject: null, passwordHash: "hash", role: UserRole.Admin)
            .AddAsync(CancellationToken);
        await s.GetRequiredService<IDomainEntityGenerator<IUser>>().CreateAsync(CancellationToken);

        var svc = s.GetRequiredService<ISetupService>();

        (await svc.AnyUsersExistAsync(CancellationToken)).Should().BeTrue();
    }
#endif

    [TestMethod]
    public async Task AnyUsersExist_WithOneRealUser_IsTrue()
    {
        var s = GetServices();
        await s.GetRequiredService<IDomainEntityGenerator<IUser>>().CreateAsync(CancellationToken);

        var svc = s.GetRequiredService<ISetupService>();

        (await svc.AnyUsersExistAsync(CancellationToken)).Should().BeTrue();
    }

    [TestMethod]
    public async Task CreateFirstAdmin_WhenUsersExist_Throws()
    {
        var s = GetServices();
        await s.GetRequiredService<IDomainEntityGenerator<IUser>>().CreateAsync(CancellationToken);

        var svc = s.GetRequiredService<ISetupService>();
        await FluentActions
            .Invoking(() => svc.CreateFirstAdminAsync("a@b.com", "Abcdef1!", CancellationToken))
            .Should().ThrowAsync<InvalidOperationException>();
    }
}
