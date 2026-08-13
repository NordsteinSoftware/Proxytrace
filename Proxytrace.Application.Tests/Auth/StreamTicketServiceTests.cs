using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Proxytrace.Application.Auth;
using Proxytrace.Domain;
using Proxytrace.Domain.User;
using Nordstein.Core.Testing;

namespace Proxytrace.Application.Tests.Auth;

[TestClass]
public sealed class StreamTicketServiceTests : BaseTest<Module>
{
    [TestMethod]
    public async Task Issue_ThenConsume_ReturnsUserId()
    {
        IServiceProvider services = GetServices();
        var user = await services.GetRequiredService<IDomainEntityGenerator<IUser>>().CreateAsync(CancellationToken);
        var tickets = services.GetRequiredService<IStreamTicketService>();

        var ticket = tickets.Issue(user);

        ticket.Token.Should().NotBeNullOrWhiteSpace();
        ticket.ExpiresAt.Should().BeAfter(DateTimeOffset.UtcNow);
        tickets.Consume(ticket.Token).Should().Be(user.Id);
    }

    [TestMethod]
    public async Task Consume_Twice_ReturnsNullSecondTime()
    {
        IServiceProvider services = GetServices();
        var user = await services.GetRequiredService<IDomainEntityGenerator<IUser>>().CreateAsync(CancellationToken);
        var tickets = services.GetRequiredService<IStreamTicketService>();

        var ticket = tickets.Issue(user);

        tickets.Consume(ticket.Token).Should().Be(user.Id);
        tickets.Consume(ticket.Token).Should().BeNull();
    }

    [TestMethod]
    public void Consume_UnknownToken_ReturnsNull()
    {
        IServiceProvider services = GetServices();
        var tickets = services.GetRequiredService<IStreamTicketService>();

        tickets.Consume("not-a-real-ticket").Should().BeNull();
    }

    [TestMethod]
    public async Task Issue_TwiceForSameUser_ProducesDistinctTokens()
    {
        IServiceProvider services = GetServices();
        var user = await services.GetRequiredService<IDomainEntityGenerator<IUser>>().CreateAsync(CancellationToken);
        var tickets = services.GetRequiredService<IStreamTicketService>();

        var first = tickets.Issue(user);
        var second = tickets.Issue(user);

        first.Token.Should().NotBe(second.Token);
        tickets.Consume(first.Token).Should().Be(user.Id);
        tickets.Consume(second.Token).Should().Be(user.Id);
    }
}
