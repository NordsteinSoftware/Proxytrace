using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Proxytrace.Domain;
using Proxytrace.Domain.AgentCall;
using Proxytrace.Testing;

namespace Proxytrace.Storage.Tests;

/// <summary>
/// Covers the single-agent "last used" lookup. The single-agent GET used to call
/// <c>GetLastCallTimesAsync</c> — an unfiltered whole-table GROUP BY — just to read one agent's
/// timestamp, so a hot per-agent page scaled with total trace volume. These assert the filtered
/// variant agrees with the whole-table one, which is what makes the substitution safe.
/// </summary>
[TestClass]
public sealed class AgentLastCallTimeQueryTests : BaseTest<Module>
{
    [TestMethod]
    public async Task GetLastCallTime_WithNoCalls_ReturnsNull()
    {
        IServiceProvider services = GetServices();
        var repo = services.GetRequiredService<IAgentCallRepository>();

        var result = await repo.GetLastCallTimeAsync(Guid.NewGuid(), CancellationToken);

        result.Should().BeNull();
    }

    [TestMethod]
    public async Task GetLastCallTime_ForAnAgentWithCalls_MatchesTheWholeTableGrouping()
    {
        IServiceProvider services = GetServices();
        var repo = services.GetRequiredService<IAgentCallRepository>();
        var gen = services.GetRequiredService<IDomainEntityGenerator<IAgentCall>>();

        var call = await gen.CreateAsync(CancellationToken);
        await gen.CreateAsync(CancellationToken);

        var agentId = call.Agent.Id;
        var all = await repo.GetLastCallTimesAsync(CancellationToken);
        all.Should().ContainKey(agentId);

        var single = await repo.GetLastCallTimeAsync(agentId, CancellationToken);

        single.Should().Be(all[agentId]);
    }

    [TestMethod]
    public async Task GetLastCallTime_ForAnAgentWithoutCalls_ReturnsNullWhileOthersHaveCalls()
    {
        // The filter must actually scope to the requested agent — an unfiltered MAX would return
        // the other agent's timestamp here.
        IServiceProvider services = GetServices();
        var repo = services.GetRequiredService<IAgentCallRepository>();
        var callGen = services.GetRequiredService<IDomainEntityGenerator<IAgentCall>>();
        var agentGen = services.GetRequiredService<IDomainEntityGenerator<Domain.Agent.IAgent>>();

        await callGen.CreateAsync(CancellationToken);
        var untouched = await agentGen.CreateAsync(CancellationToken);

        var result = await repo.GetLastCallTimeAsync(untouched.Id, CancellationToken);

        result.Should().BeNull();
    }
}
