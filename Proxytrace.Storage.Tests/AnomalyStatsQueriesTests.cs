using System.Net;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Proxytrace.Domain.Statistics;
using Proxytrace.Domain;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.AgentCall;
using Proxytrace.Domain.Completion;
using Proxytrace.Domain.Message;
using Proxytrace.Domain.Usage;
using Nordstein.Core.Testing;

namespace Proxytrace.Storage.Tests;

/// <summary>
/// Behavioral coverage for <c>GetAnomalyCountsByAgentAsync</c> (the anomaly dashboard timeline):
/// bucketing, per-agent grouping, the static/custom split and the filter scoping. Runs on the
/// in-memory provider; the server-side translation of the aggregate shape is guarded separately by
/// <see cref="StatsQueryTranslationTests"/>.
/// </summary>
[TestClass]
public sealed class AnomalyStatsQueriesTests : BaseTest<Module>
{
    [TestMethod]
    public async Task GetAnomalyCountsByAgent_EmptyDb_ReturnsEmpty()
    {
        IServiceProvider services = GetServices();
        var reader = services.GetRequiredService<IAgentCallStatsReader>();

        var rows = await reader.GetAnomalyCountsByAgentAsync(new StatisticsFilter(), StatisticsBucket.Daily, CancellationToken);

        rows.Should().BeEmpty();
    }

    [TestMethod]
    public async Task GetAnomalyCountsByAgent_OnlyUnflaggedCalls_ReturnsEmpty()
    {
        IServiceProvider services = GetServices();
        var reader = services.GetRequiredService<IAgentCallStatsReader>();
        var agent = await NewAgentAsync(services);
        await AddCallAsync(services, agent, OutlierFlags.None);

        var rows = await reader.GetAnomalyCountsByAgentAsync(new StatisticsFilter(), StatisticsBucket.Daily, CancellationToken);

        rows.Should().BeEmpty();
    }

    [TestMethod]
    public async Task GetAnomalyCountsByAgent_SplitsStaticAndCustomCounts()
    {
        IServiceProvider services = GetServices();
        var reader = services.GetRequiredService<IAgentCallStatsReader>();
        var agent = await NewAgentAsync(services);
        // One static-only, one custom-only, one carrying both kinds (counts in both), one unflagged.
        await AddCallAsync(services, agent, OutlierFlags.HighTokens);
        await AddCallAsync(services, agent, OutlierFlags.CustomAnomaly);
        await AddCallAsync(services, agent, OutlierFlags.HighLatency | OutlierFlags.CustomAnomaly);
        await AddCallAsync(services, agent, OutlierFlags.None);

        var rows = await reader.GetAnomalyCountsByAgentAsync(new StatisticsFilter(), StatisticsBucket.Daily, CancellationToken);

        var row = rows.Should().ContainSingle().Subject;
        row.AgentId.Should().Be(agent.Id);
        row.StaticCount.Should().Be(2);
        row.CustomCount.Should().Be(2);
    }

    [TestMethod]
    public async Task GetAnomalyCountsByAgent_MultipleAgents_GroupsPerAgent()
    {
        IServiceProvider services = GetServices();
        var reader = services.GetRequiredService<IAgentCallStatsReader>();
        var agentA = await NewAgentAsync(services);
        var agentB = await NewAgentAsync(services);
        await AddCallAsync(services, agentA, OutlierFlags.HighTokens);
        await AddCallAsync(services, agentA, OutlierFlags.ManyToolCalls);
        await AddCallAsync(services, agentB, OutlierFlags.CustomAnomaly);

        var rows = await reader.GetAnomalyCountsByAgentAsync(new StatisticsFilter(), StatisticsBucket.Daily, CancellationToken);

        rows.Should().HaveCount(2);
        rows.Single(r => r.AgentId == agentA.Id).StaticCount.Should().Be(2);
        rows.Single(r => r.AgentId == agentB.Id).CustomCount.Should().Be(1);
    }

    [TestMethod]
    public async Task GetAnomalyCountsByAgent_CallsOnDifferentDays_LandInSeparateAlignedBuckets()
    {
        IServiceProvider services = GetServices();
        var reader = services.GetRequiredService<IAgentCallStatsReader>();
        var agent = await NewAgentAsync(services);
        var dayOne = new DateTimeOffset(2026, 6, 1, 13, 30, 0, TimeSpan.Zero);
        var dayTwo = new DateTimeOffset(2026, 6, 2, 4, 15, 0, TimeSpan.Zero);
        await AddCallAsync(services, agent, OutlierFlags.HighTokens, createdAt: dayOne);
        await AddCallAsync(services, agent, OutlierFlags.CustomAnomaly, createdAt: dayTwo);

        var rows = await reader.GetAnomalyCountsByAgentAsync(new StatisticsFilter(), StatisticsBucket.Daily, CancellationToken);

        rows.Should().HaveCount(2);
        rows[0].BucketStart.Should().Be(new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero));
        rows[1].BucketStart.Should().Be(new DateTimeOffset(2026, 6, 2, 0, 0, 0, TimeSpan.Zero));
        rows[0].StaticCount.Should().Be(1);
        rows[1].CustomCount.Should().Be(1);
    }

    [TestMethod]
    public async Task GetAnomalyCountsByAgent_FilteredByAgent_OmitsOtherAgents()
    {
        IServiceProvider services = GetServices();
        var reader = services.GetRequiredService<IAgentCallStatsReader>();
        var agentA = await NewAgentAsync(services);
        var agentB = await NewAgentAsync(services);
        await AddCallAsync(services, agentA, OutlierFlags.HighTokens);
        await AddCallAsync(services, agentB, OutlierFlags.HighLatency);

        var rows = await reader.GetAnomalyCountsByAgentAsync(
            new StatisticsFilter(AgentId: agentA.Id), StatisticsBucket.Daily, CancellationToken);

        rows.Should().ContainSingle().Which.AgentId.Should().Be(agentA.Id);
    }

    [TestMethod]
    public async Task GetAnomalyCountsByAgent_FilteredByProject_ScopesToThatProject()
    {
        IServiceProvider services = GetServices();
        var reader = services.GetRequiredService<IAgentCallStatsReader>();
        var agent = await NewAgentAsync(services);
        await AddCallAsync(services, agent, OutlierFlags.HighTokens);

        var own = await reader.GetAnomalyCountsByAgentAsync(
            new StatisticsFilter(ProjectId: agent.Project.Id), StatisticsBucket.Daily, CancellationToken);
        var foreign = await reader.GetAnomalyCountsByAgentAsync(
            new StatisticsFilter(ProjectId: Guid.NewGuid()), StatisticsBucket.Daily, CancellationToken);

        own.Should().ContainSingle().Which.AgentId.Should().Be(agent.Id);
        foreign.Should().BeEmpty();
    }

    [TestMethod]
    public async Task GetAnomalyCountsByAgent_WindowOutsideCalls_ReturnsEmpty()
    {
        IServiceProvider services = GetServices();
        var reader = services.GetRequiredService<IAgentCallStatsReader>();
        var agent = await NewAgentAsync(services);
        await AddCallAsync(services, agent, OutlierFlags.HighTokens);

        var rows = await reader.GetAnomalyCountsByAgentAsync(
            new StatisticsFilter(From: DateTimeOffset.UtcNow.AddDays(-30), To: DateTimeOffset.UtcNow.AddDays(-29)),
            StatisticsBucket.Daily,
            CancellationToken);

        rows.Should().BeEmpty();
    }

    private async Task<IAgent> NewAgentAsync(IServiceProvider services)
        => await services.GetRequiredService<IDomainEntityGenerator<IAgent>>().CreateAsync(CancellationToken);

    private async Task AddCallAsync(
        IServiceProvider services,
        IAgent agent,
        OutlierFlags flags,
        DateTimeOffset? createdAt = null)
    {
        var createCompletion = services.GetRequiredService<ICompletion.Create>();
        var repo = services.GetRequiredService<IAgentCallRepository>();

        var conversation = Conversation.Create()
            .With(new UserMessage([Content.FromText("hi")]));
        ICompletion completion = createCompletion(
            new AssistantMessage([Content.FromText("ok")], []),
            new TokenUsage(100, 10, 0),
            TimeSpan.FromMilliseconds(100));

        // A stamped CreatedAt needs the reconstitution factory (CreateNew always stamps "now"), the
        // same seeding technique as the perf seeder.
        IAgentCall call = createdAt is { } timestamp
            ? services.GetRequiredService<IAgentCall.CreateExisting>()(
                agent: agent,
                version: agent.CurrentVersion,
                endpoint: agent.Endpoint,
                request: conversation,
                response: completion,
                httpStatus: HttpStatusCode.OK,
                finishReason: "stop",
                errorMessage: null,
                modelParameters: agent.ModelParameters,
                existing: new SeededEntityData(Guid.NewGuid(), timestamp, timestamp),
                outlierFlags: flags)
            : services.GetRequiredService<IAgentCall.CreateNew>()(
                agent: agent,
                version: agent.CurrentVersion,
                endpoint: agent.Endpoint,
                request: conversation,
                response: completion,
                httpStatus: HttpStatusCode.OK,
                finishReason: "stop",
                errorMessage: null,
                modelParameters: agent.ModelParameters,
                outlierFlags: flags);

        await repo.AddAsync(call, CancellationToken);
    }

    private sealed record SeededEntityData(Guid Id, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt) : IDomainEntityData;
}
