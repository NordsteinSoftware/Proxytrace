using System.Security.Claims;
using AwesomeAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Proxytrace.Api.Auth;
using Proxytrace.Application.Auth;
using Proxytrace.Domain;
using Proxytrace.Domain.User;
using Nordstein.Core.Testing;

namespace Proxytrace.Api.Tests.Auth;

[TestClass]
public sealed class AuthUserResolverTests : BaseTest<Module>
{
    private static TokenValidatedContext CreateContext()
    {
        var httpContext = new DefaultHttpContext();
        var scheme = new AuthenticationScheme(
            JwtBearerDefaults.AuthenticationScheme,
            JwtBearerDefaults.AuthenticationScheme,
            typeof(JwtBearerHandler));
        return new TokenValidatedContext(httpContext, scheme, new JwtBearerOptions());
    }

    [TestMethod]
    public async Task LocalUserResolver_KnownUser_ReturnsUser()
    {
        var services = GetServices();
        var generator = services.GetRequiredService<IDomainEntityGenerator<IUser>>();
        var user = await generator.CreateAsync(CancellationToken);
        var repo = services.GetRequiredService<IRepository<IUser>>();

        var resolver = new LocalUserResolver(repo);
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new("sub", user.Id.ToString())
        ]));

        var result = await resolver.Resolve(CreateContext(), principal);

        result.Should().NotBeNull();
        result.Id.Should().Be(user.Id);
    }

    [TestMethod]
    public async Task LocalUserResolver_InvalidSub_FailsContext()
    {
        var repo = Substitute.For<IRepository<IUser>>();
        var resolver = new LocalUserResolver(repo);
        var ctx = CreateContext();
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new("sub", "not-a-guid")
        ]));

        var result = await resolver.Resolve(ctx, principal);

        result.Should().BeNull();
        ctx.Result?.Failure.Should().NotBeNull();
        await repo.DidNotReceive().FindAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task LocalUserResolver_UnknownUser_FailsContext()
    {
        var repo = Substitute.For<IRepository<IUser>>();
        repo.FindAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((IUser?)null);

        var resolver = new LocalUserResolver(repo);
        var ctx = CreateContext();
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new("sub", Guid.NewGuid().ToString())
        ]));

        var result = await resolver.Resolve(ctx, principal);

        result.Should().BeNull();
        ctx.Result?.Failure.Should().NotBeNull();
    }

    [TestMethod]
    public async Task JitUserResolver_ProvisionsExternalIdentity()
    {
        var provisioner = Substitute.For<IJitUserProvisioner>();
        var user = Substitute.For<IUser>();
        provisioner.EnsureProvisionedAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(user);

        var options = new AuthOptions
        {
            Oidc = new AuthOptions.OidcOptions
            {
                Authority = "https://issuer.example.com/",
                EmailClaimType = "email",
            },
        };

        var resolver = new JitUserResolver(provisioner, options);
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new("iss", "https://issuer.example.com/"),
            new(ClaimTypes.NameIdentifier, "subject-123"),
            new("email", "user@example.com")
        ]));

        var result = await resolver.Resolve(CreateContext(), principal);

        result.Should().BeSameAs(user);
        await provisioner.Received(1).EnsureProvisionedAsync(
            "https://issuer.example.com|subject-123",
            "user@example.com",
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task JitUserResolver_MissingSubject_FailsContext()
    {
        var provisioner = Substitute.For<IJitUserProvisioner>();
        var resolver = new JitUserResolver(provisioner, new AuthOptions());
        var ctx = CreateContext();
        var principal = new ClaimsPrincipal(new ClaimsIdentity());

        var result = await resolver.Resolve(ctx, principal);

        result.Should().BeNull();
        ctx.Result?.Failure.Should().NotBeNull();
        await provisioner.DidNotReceive().EnsureProvisionedAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
