using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Proxytrace.Application.Auth.Local;
using Proxytrace.Domain.User;
using Nordstein.Core.Testing;

namespace Proxytrace.Application.Tests.Auth.Local;

[TestClass]
public sealed class LegacyClaimServiceTests : BaseTest<Module>
{
    private const string LegacyEmail = "alice@local";
    private const string LegacySubject = "legacy:|11111111-1111-1111-1111-111111111111";
    private const string NewPassword = "Abcdef1!";

    [TestMethod]
    public async Task IsClaimAvailable_NoUsers_False()
    {
        var svc = GetServices().GetRequiredService<ILegacyClaimService>();
        (await svc.IsClaimAvailableAsync(CancellationToken)).Should().BeFalse();
    }

    [TestMethod]
    public async Task IsClaimAvailable_SingleLegacyUserWithoutPassword_True()
    {
        var s = GetServices();
        await AddUserAsync(s, LegacyEmail, LegacySubject, passwordHash: null, UserRole.Admin);

        var svc = s.GetRequiredService<ILegacyClaimService>();
        (await svc.IsClaimAvailableAsync(CancellationToken)).Should().BeTrue();
    }

    [TestMethod]
    public async Task IsClaimAvailable_SingleOidcUser_False()
    {
        var s = GetServices();
        await AddUserAsync(s, "bob@example.com", "https://issuer|sub-1", passwordHash: null, UserRole.Admin);

        var svc = s.GetRequiredService<ILegacyClaimService>();
        (await svc.IsClaimAvailableAsync(CancellationToken)).Should().BeFalse();
    }

    [TestMethod]
    public async Task IsClaimAvailable_SingleLocalUserWithPassword_False()
    {
        var s = GetServices();
        await AddLocalUserAsync(s, "local@b.com", NewPassword, UserRole.Admin);

        var svc = s.GetRequiredService<ILegacyClaimService>();
        (await svc.IsClaimAvailableAsync(CancellationToken)).Should().BeFalse();
    }

    [TestMethod]
    public async Task IsClaimAvailable_TwoUsers_False()
    {
        var s = GetServices();
        await AddUserAsync(s, LegacyEmail, LegacySubject, passwordHash: null, UserRole.Admin);
        await AddUserAsync(s, "carol@local", "legacy:|22222222-2222-2222-2222-222222222222", passwordHash: null, UserRole.Admin);

        var svc = s.GetRequiredService<ILegacyClaimService>();
        (await svc.IsClaimAvailableAsync(CancellationToken)).Should().BeFalse();
    }

    [TestMethod]
    public async Task Claim_SetsPasswordHashAndIssuesToken()
    {
        var s = GetServices();
        await AddUserAsync(s, LegacyEmail, LegacySubject, passwordHash: null, UserRole.Admin);

        var svc = s.GetRequiredService<ILegacyClaimService>();
        var result = await svc.ClaimAsync(LegacyEmail, NewPassword, CancellationToken);

        result.Should().NotBeNull();
        result.Token.Should().NotBeNullOrEmpty();

        var repo = s.GetRequiredService<IUserRepository>();
        var fresh = await repo.FindByEmailAsync(LegacyEmail, CancellationToken);
        fresh.Should().NotBeNull();
        fresh.PasswordHash.Should().NotBeNullOrEmpty();

        var login = s.GetRequiredService<ILoginService>();
        (await login.LoginAsync(LegacyEmail, NewPassword, CancellationToken)).Should().NotBeNull();
    }

    [TestMethod]
    public async Task Claim_WrongEmail_ReturnsNull()
    {
        var s = GetServices();
        await AddUserAsync(s, LegacyEmail, LegacySubject, passwordHash: null, UserRole.Admin);

        var svc = s.GetRequiredService<ILegacyClaimService>();
        (await svc.ClaimAsync("wrong@local", NewPassword, CancellationToken)).Should().BeNull();
    }

    [TestMethod]
    public async Task Claim_WhenIneligible_ReturnsNull()
    {
        var svc = GetServices().GetRequiredService<ILegacyClaimService>();
        (await svc.ClaimAsync(LegacyEmail, NewPassword, CancellationToken)).Should().BeNull();
    }

    [TestMethod]
    public async Task Claim_SecondInvocation_ReturnsNull()
    {
        var s = GetServices();
        await AddUserAsync(s, LegacyEmail, LegacySubject, passwordHash: null, UserRole.Admin);

        var svc = s.GetRequiredService<ILegacyClaimService>();
        (await svc.ClaimAsync(LegacyEmail, NewPassword, CancellationToken)).Should().NotBeNull();
        (await svc.ClaimAsync(LegacyEmail, NewPassword, CancellationToken)).Should().BeNull();
    }

    private async Task AddUserAsync(
        IServiceProvider s,
        string email,
        string? externalSubject,
        string? passwordHash,
        UserRole role)
    {
        var factory = s.GetRequiredService<IUser.CreateNew>();
        var user = factory(email, externalSubject, passwordHash, role);
        await user.AddAsync(CancellationToken);
    }

    private async Task AddLocalUserAsync(IServiceProvider s, string email, string password, UserRole role)
    {
        var pwd = s.GetRequiredService<IPasswordService>();
        var factory = s.GetRequiredService<IUser.CreateNew>();
        var draft = factory(email, null, "x", role);
        var hash = pwd.Hash(draft, password);
        var withHash = factory(email, null, hash, role);
        await withHash.AddAsync(CancellationToken);
    }
}
