using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Proxytrace.Application.Auth;
using Proxytrace.Domain.User;
using Nordstein.Core.Testing;

namespace Proxytrace.Application.Tests.Auth;

[TestClass]
public sealed class MfaChallengeServiceTests : BaseTest<Module>
{
    private async Task<IUser> SeedUser(IServiceProvider services)
    {
        var factory = services.GetRequiredService<IUser.CreateNew>();
        return await factory("c@b.com", null, "hash", UserRole.Member).AddAsync(CancellationToken);
    }

    [TestMethod]
    public async Task IssueThenPeek_ReturnsUserId()
    {
        var services = GetServices();
        var svc = services.GetRequiredService<IMfaChallengeService>();
        var user = await SeedUser(services);

        var challenge = svc.Issue(user);

        challenge.Token.Should().NotBeNullOrEmpty();
        svc.Peek(challenge.Token).Should().Be(user.Id);
    }

    [TestMethod]
    public async Task Consume_InvalidatesTicket()
    {
        var services = GetServices();
        var svc = services.GetRequiredService<IMfaChallengeService>();
        var user = await SeedUser(services);
        var challenge = svc.Issue(user);

        svc.Consume(challenge.Token);

        svc.Peek(challenge.Token).Should().BeNull();
    }

    [TestMethod]
    public void Peek_UnknownToken_ReturnsNull()
    {
        var svc = GetServices().GetRequiredService<IMfaChallengeService>();
        svc.Peek("nope").Should().BeNull();
    }

    [TestMethod]
    public async Task RegisterFailure_AfterAttemptCap_InvalidatesTicket()
    {
        var services = GetServices();
        var svc = services.GetRequiredService<IMfaChallengeService>();
        var user = await SeedUser(services);
        var challenge = svc.Issue(user);

        // The cap is 5; the fifth failure exhausts and removes the ticket.
        for (var i = 0; i < 4; i++)
        {
            svc.RegisterFailure(challenge.Token).Should().BeTrue();
        }
        svc.RegisterFailure(challenge.Token).Should().BeFalse();
        svc.Peek(challenge.Token).Should().BeNull();
    }
}
