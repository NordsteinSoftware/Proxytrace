using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Proxytrace.Domain.Invite;
using Proxytrace.Domain.User;
using Nordstein.Core.Testing;

namespace Proxytrace.Domain.Tests;

[TestClass]
public sealed class InviteValidationTests : BaseTest<Module>
{
    [TestMethod]
    public async Task CreateNew_WithValidArgs_CreatesInvite()
    {
        var services = GetServices();
        var userGen = services.GetRequiredService<IDomainEntityGenerator<IUser>>();
        var user = await userGen.CreateAsync(CancellationToken);
        var factory = services.GetRequiredService<IInvite.CreateNew>();

        var invite = factory("invitee@example.com", UserRole.Member, "tok-abcdef", DateTimeOffset.UtcNow.AddDays(7), user);

        invite.Email.Should().Be("invitee@example.com");
        invite.Role.Should().Be(UserRole.Member);
        invite.TokenHash.Should().Be("tok-abcdef");
        invite.ConsumedAt.Should().BeNull();
        invite.InvitedBy.Id.Should().Be(user.Id);
    }

    [TestMethod]
    public async Task CreateNew_WithEmptyEmail_Throws()
    {
        var services = GetServices();
        var user = await services.GetRequiredService<IDomainEntityGenerator<IUser>>().CreateAsync(CancellationToken);
        var factory = services.GetRequiredService<IInvite.CreateNew>();
        var act = () => factory("", UserRole.Member, "tok", DateTimeOffset.UtcNow.AddDays(7), user);
        act.Should().Throw<Exception>();
    }

    [TestMethod]
    public async Task CreateNew_WithEmptyToken_Throws()
    {
        var services = GetServices();
        var user = await services.GetRequiredService<IDomainEntityGenerator<IUser>>().CreateAsync(CancellationToken);
        var factory = services.GetRequiredService<IInvite.CreateNew>();
        var act = () => factory("a@b.com", UserRole.Member, "", DateTimeOffset.UtcNow.AddDays(7), user);
        act.Should().Throw<Exception>();
    }

    [TestMethod]
    public async Task MarkConsumed_SetsConsumedAt()
    {
        var services = GetServices();
        var user = await services.GetRequiredService<IDomainEntityGenerator<IUser>>().CreateAsync(CancellationToken);
        var factory = services.GetRequiredService<IInvite.CreateNew>();
        var invite = await factory("a@b.com", UserRole.Member, "tok", DateTimeOffset.UtcNow.AddDays(7), user).AddAsync(CancellationToken);

        var consumed = await invite.MarkConsumedAsync(CancellationToken);

        consumed.ConsumedAt.Should().NotBeNull();
    }
}
