using Autofac;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Proxytrace.Application.Auth.Local;
using Proxytrace.Domain.User;
using Nordstein.Core.Testing;

namespace Proxytrace.Application.Tests.Auth.Local;

[TestClass]
public sealed class LoginServiceTests : BaseTest<Module>
{
    [TestMethod]
    public async Task Login_WithCorrectPassword_ReturnsToken()
    {
        var s = GetServices();
        var pwd = s.GetRequiredService<IPasswordService>();
        var factory = s.GetRequiredService<IUser.CreateNew>();
        var draft = factory("u@b.com", null, "x", UserRole.Member);
        var hash = pwd.Hash(draft, "Abcdef1!");
        var withHash = factory("u@b.com", null, hash, UserRole.Member);
        await withHash.AddAsync(CancellationToken);

        var svc = s.GetRequiredService<ILoginService>();
        var result = await svc.LoginAsync("u@b.com", "Abcdef1!", CancellationToken);

        // No MFA enrollment → a session is issued outright.
        result.Should().BeOfType<LoginSucceeded>();
        ((LoginSucceeded)result!).Token.Should().NotBeNullOrEmpty();
    }

    [TestMethod]
    public async Task Login_WithWrongPassword_ReturnsNull()
    {
        var s = GetServices();
        var pwd = s.GetRequiredService<IPasswordService>();
        var factory = s.GetRequiredService<IUser.CreateNew>();
        var draft = factory("w@b.com", null, "x", UserRole.Member);
        var hash = pwd.Hash(draft, "Abcdef1!");
        var withHash = factory("w@b.com", null, hash, UserRole.Member);
        await withHash.AddAsync(CancellationToken);

        var svc = s.GetRequiredService<ILoginService>();
        (await svc.LoginAsync("w@b.com", "Wrong!1A", CancellationToken)).Should().BeNull();
    }

    [TestMethod]
    public async Task Login_WithUnknownEmail_ReturnsNull()
    {
        var svc = GetServices().GetRequiredService<ILoginService>();
        (await svc.LoginAsync("unknown@b.com", "Abcdef1!", CancellationToken)).Should().BeNull();
    }

    [TestMethod]
    public async Task Login_WithUnknownEmail_StillSpendsTheVerificationCost()
    {
        // Bailing out before hashing made an unknown email answer far faster than a known one,
        // disclosing which addresses have accounts. The not-found path must verify against a dummy
        // hash so the work — and so the response time — matches the found path.
        var passwords = Substitute.For<IPasswordService>();
        var s = GetServices(builder => builder.RegisterInstance(passwords).As<IPasswordService>());

        var svc = s.GetRequiredService<ILoginService>();
        var result = await svc.LoginAsync("nobody@b.com", "Abcdef1!", CancellationToken);

        result.Should().BeNull();
        passwords.Received(1).VerifyDummy("Abcdef1!");
    }

    [TestMethod]
    public async Task Login_WithKnownEmailButNoPasswordSet_StillSpendsTheVerificationCost()
    {
        // Same oracle via a different door: an SSO-only account has no password hash, so the
        // early return would otherwise distinguish it from an account that simply does not exist.
        var passwords = Substitute.For<IPasswordService>();
        var s = GetServices(builder => builder.RegisterInstance(passwords).As<IPasswordService>());

        var factory = s.GetRequiredService<IUser.CreateNew>();
        await factory("sso@b.com", "external-subject", null, UserRole.Member).AddAsync(CancellationToken);

        var svc = s.GetRequiredService<ILoginService>();
        var result = await svc.LoginAsync("sso@b.com", "Abcdef1!", CancellationToken);

        result.Should().BeNull();
        passwords.Received(1).VerifyDummy("Abcdef1!");
    }
}
