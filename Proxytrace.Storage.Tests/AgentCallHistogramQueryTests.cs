using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Proxytrace.Domain;
using Proxytrace.Domain.AgentCall;
using Nordstein.Core.Testing;

namespace Proxytrace.Storage.Tests;

[TestClass]
public sealed class AgentCallHistogramQueryTests : BaseTest<Module>
{
    [TestMethod]
    public async Task GetHistogram_EmptyDb_ReturnsEmpty()
    {
        IServiceProvider services = GetServices();
        var repo = services.GetRequiredService<IAgentCallRepository>();

        var result = await repo.GetHistogramAsync(
            new AgentCallFilter(From: DateTimeOffset.UtcNow.AddHours(-1), To: DateTimeOffset.UtcNow),
            buckets: 6,
            CancellationToken);

        result.Should().BeEmpty();
    }

    [TestMethod]
    public async Task GetHistogram_WithSeededCalls_CountsLandInWindow()
    {
        IServiceProvider services = GetServices();
        var repo = services.GetRequiredService<IAgentCallRepository>();
        var gen = services.GetRequiredService<IDomainEntityGenerator<IAgentCall>>();
        await gen.CreateAsync(CancellationToken);
        await gen.CreateAsync(CancellationToken);

        var result = await repo.GetHistogramAsync(
            new AgentCallFilter(
                From: DateTimeOffset.UtcNow.AddMinutes(-5),
                To: DateTimeOffset.UtcNow.AddMinutes(5)),
            buckets: 6,
            CancellationToken);

        result.Should().HaveCount(6);
        result.Sum(b => b.Total).Should().Be(2);
    }

    [TestMethod]
    public async Task GetHistogram_NoFrom_DerivesWindowFromEarliestCall()
    {
        IServiceProvider services = GetServices();
        var repo = services.GetRequiredService<IAgentCallRepository>();
        var gen = services.GetRequiredService<IDomainEntityGenerator<IAgentCall>>();
        await gen.CreateAsync(CancellationToken);

        var result = await repo.GetHistogramAsync(new AgentCallFilter(), buckets: 4, CancellationToken);

        result.Should().HaveCount(4);
        result.Sum(b => b.Total).Should().Be(1);
    }
}
