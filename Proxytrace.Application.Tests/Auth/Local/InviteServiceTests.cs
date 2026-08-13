using Autofac;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Proxytrace.Application.Auth.Local;
using Proxytrace.Domain;
using Proxytrace.Domain.User;
using Proxytrace.Licensing;
using Proxytrace.Licensing.Exceptions;
using Nordstein.Core.Testing;

namespace Proxytrace.Application.Tests.Auth.Local;

[TestClass]
public sealed class InviteServiceTests : BaseTest<Module>
{
    [TestMethod]
    public async Task Create_PersistsInviteWithToken()
    {
        var s = GetServices(PermissiveLicense);
        var inviter = await s.GetRequiredService<IDomainEntityGenerator<IUser>>().CreateAsync(CancellationToken);
        var svc = s.GetRequiredService<IInviteService>();

        var created = await svc.CreateAsync("a@b.com", UserRole.Member, inviter, CancellationToken);

        created.RawToken.Should().NotBeNullOrEmpty();
        // Only the hash is persisted — never the raw token.
        created.Invite.TokenHash.Should().NotBe(created.RawToken);
        created.Invite.Email.Should().Be("a@b.com");
        created.Invite.ConsumedAt.Should().BeNull();
        created.Invite.ExpiresAt.Should().BeAfter(DateTimeOffset.UtcNow.AddDays(6));
    }

    [TestMethod]
    public async Task GetByToken_ReturnsInviteWhenValid()
    {
        var s = GetServices(PermissiveLicense);
        var inviter = await s.GetRequiredService<IDomainEntityGenerator<IUser>>().CreateAsync(CancellationToken);
        var svc = s.GetRequiredService<IInviteService>();
        var created = await svc.CreateAsync("a@b.com", UserRole.Member, inviter, CancellationToken);

        var fetched = await svc.GetByTokenAsync(created.RawToken, CancellationToken);
        fetched.Should().NotBeNull();
        fetched.Email.Should().Be("a@b.com");
    }

    [TestMethod]
    public async Task Consume_CreatesUserAndMarksConsumed()
    {
        var s = GetServices(PermissiveLicense);
        var inviter = await s.GetRequiredService<IDomainEntityGenerator<IUser>>().CreateAsync(CancellationToken);
        var svc = s.GetRequiredService<IInviteService>();
        var created = await svc.CreateAsync("new@b.com", UserRole.Admin, inviter, CancellationToken);

        var newUser = await svc.ConsumeAsync(created.RawToken, "Abcdef1!", CancellationToken);

        newUser.Should().NotBeNull();
        newUser.Email.Should().Be("new@b.com");
        newUser.Role.Should().Be(UserRole.Admin);
        newUser.PasswordHash.Should().NotBeNullOrEmpty();

        (await svc.GetByTokenAsync(created.RawToken, CancellationToken)).Should().BeNull();
    }

    [TestMethod]
    public async Task Create_WhenUserCountAtLimit_ThrowsLicenseLimitExceeded()
    {
        var license = Substitute.For<ILicenseService>();
        license.GetLimit(LicenseLimit.MaxUsers).Returns(1);

        var s = GetServices(b => b.RegisterInstance(license).As<ILicenseService>());
        // One persisted user puts us at the MaxUsers=1 cap; the next invite must be rejected.
        var inviter = await s.GetRequiredService<IDomainEntityGenerator<IUser>>().CreateAsync(CancellationToken);
        var svc = s.GetRequiredService<IInviteService>();

        await FluentActions
            .Invoking(() => svc.CreateAsync("a@b.com", UserRole.Member, inviter, CancellationToken))
            .Should().ThrowAsync<LicenseLimitExceededException>()
            .Where(e => e.Limit == LicenseLimit.MaxUsers);
    }

    [TestMethod]
    public async Task Create_WhenUserCountBelowLimit_Succeeds()
    {
        var license = Substitute.For<ILicenseService>();
        license.GetLimit(LicenseLimit.MaxUsers).Returns(5);

        var s = GetServices(b => b.RegisterInstance(license).As<ILicenseService>());
        var inviter = await s.GetRequiredService<IDomainEntityGenerator<IUser>>().CreateAsync(CancellationToken);
        var svc = s.GetRequiredService<IInviteService>();

        var created = await svc.CreateAsync("a@b.com", UserRole.Member, inviter, CancellationToken);

        created.Invite.Should().NotBeNull();
    }

    // The default (Free) tier caps MaxUsers at 1, so tests that seed a user before inviting must
    // opt out of the limit to exercise invite behaviour itself.
    private static void PermissiveLicense(ContainerBuilder builder)
    {
        var license = Substitute.For<ILicenseService>();
        license.GetLimit(Arg.Any<LicenseLimit>()).Returns(long.MaxValue);
        builder.RegisterInstance(license).As<ILicenseService>();
    }
}
