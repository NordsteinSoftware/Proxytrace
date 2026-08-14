using System.Net;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Proxytrace.Domain.Outliers;
using Proxytrace.Domain;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.AgentCall;
using Nordstein.Core.AI.Completions;
using Nordstein.Core.AI.Messages;
using Nordstein.Core.Testing;

namespace Proxytrace.Storage.Tests;

[TestClass]
public sealed class OutlierBaselineQueriesTests : BaseTest<Module>
{
    [TestMethod]
    public async Task GetBaseline_NoCalls_ReturnsEmpty()
    {
        IServiceProvider services = GetServices();
        var agent = await NewAgentAsync(services);
        var reader = services.GetRequiredService<IOutlierBaselineReader>();

        var baseline = await reader.GetBaselineAsync(agent.Id, 200, CancellationToken);

        baseline.TotalTokens.SampleCount.Should().Be(0);
    }

    [TestMethod]
    public async Task GetBaseline_ComputesPerCallTokenMeanAndStdDev()
    {
        IServiceProvider services = GetServices();
        var agent = await NewAgentAsync(services);
        // Total tokens 100, 100, 130 → mean 110, sample (n−1) stddev = sqrt(300).
        await AddCallAsync(services, agent, input: 60, output: 40);
        await AddCallAsync(services, agent, input: 60, output: 40);
        await AddCallAsync(services, agent, input: 80, output: 50);
        var reader = services.GetRequiredService<IOutlierBaselineReader>();

        var baseline = await reader.GetBaselineAsync(agent.Id, 200, CancellationToken);

        baseline.TotalTokens.SampleCount.Should().Be(3);
        baseline.TotalTokens.Mean.Should().BeApproximately(110d, 0.001);
        baseline.TotalTokens.StdDev.Should().BeApproximately(Math.Sqrt(300d), 0.01);
    }

    [TestMethod]
    public async Task GetBaseline_ExcludesNonSuccessfulCalls()
    {
        IServiceProvider services = GetServices();
        var agent = await NewAgentAsync(services);
        await AddCallAsync(services, agent, input: 60, output: 40);
        await AddCallAsync(services, agent, input: 0, output: 0, httpStatus: HttpStatusCode.InternalServerError);
        var reader = services.GetRequiredService<IOutlierBaselineReader>();

        var baseline = await reader.GetBaselineAsync(agent.Id, 200, CancellationToken);

        baseline.TotalTokens.SampleCount.Should().Be(1);
    }

    [TestMethod]
    public async Task GetBaseline_SamplesCacheHitForLaterTurnsOnly()
    {
        IServiceProvider services = GetServices();
        var agent = await NewAgentAsync(services);
        var conversation = Guid.NewGuid();
        // Three turns of the same conversation, each with cached 80 of 100 input. The earliest turn is
        // dropped (turn 1 can't be a cache hit), leaving two later-turn samples, both 0.8 — so the
        // assertion holds regardless of which call the in-memory provider treats as earliest.
        for (int i = 0; i < 3; i++)
        {
            await AddCallAsync(services, agent, input: 100, output: 10, cached: 80, conversationId: conversation);
        }
        var reader = services.GetRequiredService<IOutlierBaselineReader>();

        var baseline = await reader.GetBaselineAsync(agent.Id, 200, CancellationToken);

        baseline.CacheHitRate.SampleCount.Should().Be(2);
        baseline.CacheHitRate.Mean.Should().BeApproximately(0.8d, 0.001);
    }

    [TestMethod]
    public async Task GetFilteredList_OutlierOnly_ReturnsOnlyFlaggedCalls()
    {
        IServiceProvider services = GetServices();
        var agent = await NewAgentAsync(services);
        await AddCallAsync(services, agent, input: 60, output: 40, flags: OutlierFlags.HighTokens);
        await AddCallAsync(services, agent, input: 60, output: 40, flags: OutlierFlags.None);
        var repo = services.GetRequiredService<IAgentCallRepository>();

        var (items, total) = await repo.GetFilteredListAsync(
            new AgentCallFilter(AgentId: agent.Id, OutlierOnly: true), 1, 50, CancellationToken);

        total.Should().Be(1);
        items.Should().ContainSingle();
        items[0].OutlierFlags.Should().Be(OutlierFlags.HighTokens);
    }

    [TestMethod]
    public async Task GetFilteredList_WithoutOutlierFilter_ReturnsAll_AndPreservesFlags()
    {
        IServiceProvider services = GetServices();
        var agent = await NewAgentAsync(services);
        await AddCallAsync(services, agent, input: 60, output: 40, flags: OutlierFlags.HighLatency | OutlierFlags.ManyToolCalls);
        await AddCallAsync(services, agent, input: 60, output: 40, flags: OutlierFlags.None);
        var repo = services.GetRequiredService<IAgentCallRepository>();

        var (items, total) = await repo.GetFilteredListAsync(
            new AgentCallFilter(AgentId: agent.Id), 1, 50, CancellationToken);

        total.Should().Be(2);
        items.Should().Contain(i => i.OutlierFlags == (OutlierFlags.HighLatency | OutlierFlags.ManyToolCalls));
    }

    private async Task<IAgent> NewAgentAsync(IServiceProvider services)
        => await services.GetRequiredService<IDomainEntityGenerator<IAgent>>().CreateAsync(CancellationToken);

    private async Task AddCallAsync(
        IServiceProvider services,
        IAgent agent,
        ulong input,
        ulong output,
        ulong cached = 0,
        double latencyMs = 100,
        HttpStatusCode httpStatus = HttpStatusCode.OK,
        Guid? conversationId = null,
        OutlierFlags flags = OutlierFlags.None)
    {
        var createCall = services.GetRequiredService<IAgentCall.CreateNew>();
        var createCompletion = services.GetRequiredService<ICompletion.Create>();
        var repo = services.GetRequiredService<IAgentCallRepository>();

        var conversation = Conversation.Create()
            .With(new UserMessage([Content.FromText("hi")]));

        bool success = (int)httpStatus is >= 200 and < 300;
        ICompletion? completion = success
            ? createCompletion(
                new AssistantMessage([Content.FromText("ok")], []),
                new TokenUsage(input, output, cached),
                TimeSpan.FromMilliseconds(latencyMs))
            : null;

        var call = createCall(
            agent: agent,
            version: agent.CurrentVersion,
            endpoint: agent.Endpoint,
            request: conversation,
            response: completion,
            httpStatus: httpStatus,
            finishReason: "stop",
            errorMessage: null,
            modelParameters: agent.ModelParameters,
            conversationId: conversationId,
            outlierFlags: flags);

        await repo.AddAsync(call, CancellationToken);
    }
}
