using System.Net;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Nordstein.Core.AI.Completions;
using Nordstein.Core.AI.Messages;
using Nordstein.Core.Testing;
using Proxytrace.Domain;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.AgentCall;
using Proxytrace.Domain.Statistics;

namespace Proxytrace.Storage.Tests;

/// <summary>
/// The scope filter (<c>AgentCall.ScopeId</c>) across every read path that takes it: the traces list,
/// histogram and KPI summary (<see cref="AgentCallFilter"/>) and the statistics LINQ chokepoint
/// (<see cref="StatisticsFilter"/>). They must all select exactly the scoped rows.
/// </summary>
[TestClass]
public sealed class ScopeFilterQueryTests : BaseTest<Module>
{
    [TestMethod]
    public async Task TraceReads_WithScopeFilter_SelectOnlyThatScopesCalls()
    {
        IServiceProvider services = GetServices();
        var agent = await services.GetRequiredService<IDomainEntityGenerator<IAgent>>().CreateAsync(CancellationToken);
        var repo = services.GetRequiredService<IAgentCallRepository>();
        var support = Guid.NewGuid();
        var billing = Guid.NewGuid();
        await SeedCallAsync(services, agent, support);
        await SeedCallAsync(services, agent, support);
        await SeedCallAsync(services, agent, billing);
        await SeedCallAsync(services, agent, scopeId: null);
        var filter = new AgentCallFilter(ScopeId: support);

        var (items, total) = await repo.GetFilteredListAsync(filter, 1, 50, CancellationToken);
        var summary = await repo.GetSummaryAsync(filter, CancellationToken);
        var histogram = await repo.GetHistogramAsync(filter, 10, CancellationToken);

        total.Should().Be(2);
        items.Should().OnlyContain(i => i.ScopeId == support, "the list row carries the scope for the UI badge");
        summary.Count.Should().Be(2);
        histogram.Sum(b => b.Total).Should().Be(2);
    }

    [TestMethod]
    public async Task Statistics_WithScopeFilter_AggregateOnlyThatScopesCalls()
    {
        IServiceProvider services = GetServices();
        var agent = await services.GetRequiredService<IDomainEntityGenerator<IAgent>>().CreateAsync(CancellationToken);
        var support = Guid.NewGuid();
        await SeedCallAsync(services, agent, support);
        await SeedCallAsync(services, agent, Guid.NewGuid());
        await SeedCallAsync(services, agent, scopeId: null);
        var stats = services.GetRequiredService<IAgentCallStatsReader>();

        var scoped = await stats.GetSummaryAsync(new StatisticsFilter(ScopeId: support), CancellationToken);
        var all = await stats.GetSummaryAsync(new StatisticsFilter(), CancellationToken);

        scoped.TotalCalls.Should().Be(1);
        all.TotalCalls.Should().Be(3);
    }

    private async Task SeedCallAsync(IServiceProvider services, IAgent agent, Guid? scopeId)
    {
        var request = await services.GetRequiredService<IDomainObjectGenerator<Conversation>>().CreateAsync(CancellationToken);
        ICompletion response = services.GetRequiredService<ICompletion.Create>()(
            new AssistantMessage([Content.FromText("ok")], []), new TokenUsage(10, 5), TimeSpan.FromMilliseconds(50));

        IAgentCall call = services.GetRequiredService<IAgentCall.CreateNew>()(
            agent,
            agent.CurrentVersion,
            agent.Endpoint,
            request,
            response,
            httpStatus: HttpStatusCode.OK,
            modelParameters: agent.ModelParameters,
            scopeId: scopeId);

        await services.GetRequiredService<IAgentCallRepository>().AddAsync(call, CancellationToken);
    }
}
