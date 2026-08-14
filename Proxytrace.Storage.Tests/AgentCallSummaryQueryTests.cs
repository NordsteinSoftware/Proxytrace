using System.Net;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Proxytrace.Domain;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.AgentCall;
using Nordstein.Core.AI.Completions;
using Nordstein.Core.AI.Messages;
using Proxytrace.Domain.Model;
using Proxytrace.Domain.ModelEndpoint;
using Proxytrace.Domain.ModelProvider;
using Nordstein.Core.Testing;

namespace Proxytrace.Storage.Tests;

/// <summary>
/// <see cref="IAgentCallRepository.GetSummaryAsync"/> — the unpaged aggregate behind the traces KPI
/// band. The load-bearing test here is
/// <see cref="GetSummary_MatchesGetFilteredListTotal_ForEveryFilterDimension"/>: the summary and the
/// list must never disagree about what a filter selects.
/// </summary>
[TestClass]
public sealed class AgentCallSummaryQueryTests : BaseTest<Module>
{
    [TestMethod]
    public async Task GetSummary_EmptyDb_ReturnsEmpty()
    {
        IServiceProvider services = GetServices();
        var repo = services.GetRequiredService<IAgentCallRepository>();

        var result = await repo.GetSummaryAsync(new AgentCallFilter(), CancellationToken);

        result.Should().Be(AgentCallSummary.Empty);
    }

    [TestMethod]
    public async Task GetSummary_SumsTokensOverEveryMatchingRow_NotJustAPage()
    {
        IServiceProvider services = GetServices();
        var repo = services.GetRequiredService<IAgentCallRepository>();
        var (agent, endpoint) = await SeedAgentAsync(services);

        // Deliberately more rows than any page size the UI ever requested — the point of this
        // aggregate is that it is not page-scoped.
        for (int i = 0; i < 60; i++)
        {
            await SeedCallAsync(services, agent, endpoint, new TokenUsage(10, 5, 2), latencyMs: 100);
        }

        var result = await repo.GetSummaryAsync(new AgentCallFilter(), CancellationToken);

        result.Count.Should().Be(60);
        result.InputTokens.Should().Be(600);
        result.OutputTokens.Should().Be(300);
        result.CachedInputTokens.Should().Be(120);
    }

    [TestMethod]
    public async Task GetSummary_MatchesGetFilteredListTotal_ForEveryFilterDimension()
    {
        IServiceProvider services = GetServices();
        var repo = services.GetRequiredService<IAgentCallRepository>();
        var (agent, endpoint) = await SeedAgentAsync(services);
        var (otherAgent, _) = await SeedAgentAsync(services);

        var old = DateTimeOffset.UtcNow.AddDays(-10);
        await SeedCallAsync(services, agent, endpoint, new TokenUsage(10, 5), latencyMs: 50);
        await SeedCallAsync(services, agent, endpoint, new TokenUsage(20, 10), latencyMs: 500);
        await SeedCallAsync(services, agent, endpoint, usage: null, latencyMs: null,
            httpStatus: HttpStatusCode.InternalServerError);
        await SeedCallAsync(services, agent, endpoint, new TokenUsage(1, 1), latencyMs: 10, createdAt: old);
        await SeedCallAsync(services, otherAgent, endpoint, new TokenUsage(7, 7), latencyMs: 70);

        AgentCallFilter[] filters =
        [
            new AgentCallFilter(),
            new AgentCallFilter(AgentId: agent.Id),
            new AgentCallFilter(From: DateTimeOffset.UtcNow.AddDays(-1)),
            new AgentCallFilter(HttpStatus: (int)HttpStatusCode.InternalServerError),
            new AgentCallFilter(HttpStatusClass: 5),
            new AgentCallFilter(MinLatencyMs: 100),
            new AgentCallFilter(MaxTokens: 20),
        ];

        foreach (var filter in filters)
        {
            var summary = await repo.GetSummaryAsync(filter, CancellationToken);
            var (_, total) = await repo.GetFilteredListAsync(filter, 1, 10_000, CancellationToken);

            summary.Count.Should().Be(total, $"summary and list must agree for filter {filter}");
        }
    }

    [TestMethod]
    public async Task GetSummary_PricesEachEndpointWithItsOwnRate()
    {
        IServiceProvider services = GetServices();
        var repo = services.GetRequiredService<IAgentCallRepository>();
        var agent = await services.GetRequiredService<IDomainEntityGenerator<IAgent>>()
            .CreateAsync(CancellationToken);

        // 1 EUR / 1M in, 2 EUR / 1M out.
        var cheap = await SeedEndpointAsync(services, inputCost: 1m, outputCost: 2m);
        // 10 EUR / 1M in, 20 EUR / 1M out.
        var pricey = await SeedEndpointAsync(services, inputCost: 10m, outputCost: 20m);

        await SeedCallAsync(services, agent, cheap, new TokenUsage(1_000_000, 1_000_000), latencyMs: 10);
        await SeedCallAsync(services, agent, pricey, new TokenUsage(1_000_000, 1_000_000), latencyMs: 10);

        var result = await repo.GetSummaryAsync(new AgentCallFilter(), CancellationToken);

        // Applying one endpoint's rate to both rows would give 6 or 60, not 33.
        result.TotalCost.Should().Be(33m);
    }

    [TestMethod]
    public async Task GetSummary_CountsNon2xxAsErrors()
    {
        IServiceProvider services = GetServices();
        var repo = services.GetRequiredService<IAgentCallRepository>();
        var (agent, endpoint) = await SeedAgentAsync(services);

        await SeedCallAsync(services, agent, endpoint, new TokenUsage(1, 1), latencyMs: 10,
            httpStatus: HttpStatusCode.OK);
        await SeedCallAsync(services, agent, endpoint, new TokenUsage(1, 1), latencyMs: 10,
            httpStatus: HttpStatusCode.Created);
        await SeedCallAsync(services, agent, endpoint, new TokenUsage(1, 1), latencyMs: 10,
            httpStatus: HttpStatusCode.NotFound);
        await SeedCallAsync(services, agent, endpoint, new TokenUsage(1, 1), latencyMs: 10,
            httpStatus: HttpStatusCode.InternalServerError);

        var result = await repo.GetSummaryAsync(new AgentCallFilter(), CancellationToken);

        result.Count.Should().Be(4);
        result.ErrorCount.Should().Be(2);
    }

    [TestMethod]
    public async Task GetSummary_IgnoresRowsWithoutLatencyInTheAverage()
    {
        IServiceProvider services = GetServices();
        var repo = services.GetRequiredService<IAgentCallRepository>();
        var (agent, endpoint) = await SeedAgentAsync(services);

        await SeedCallAsync(services, agent, endpoint, new TokenUsage(1, 1), latencyMs: 100);
        await SeedCallAsync(services, agent, endpoint, new TokenUsage(1, 1), latencyMs: 200);
        // No response → no latency recorded. Averaging over all three rows would give 100.
        await SeedCallAsync(services, agent, endpoint, usage: null, latencyMs: null);

        var result = await repo.GetSummaryAsync(new AgentCallFilter(), CancellationToken);

        result.Count.Should().Be(3);
        result.AvgLatencyMs.Should().BeApproximately(150d, 1e-6);
    }

    [TestMethod]
    public async Task GetSummary_ComputesPopulationLatencyStandardDeviation()
    {
        IServiceProvider services = GetServices();
        var repo = services.GetRequiredService<IAgentCallRepository>();
        var (agent, endpoint) = await SeedAgentAsync(services);

        foreach (double ms in new double[] { 2, 4, 4, 4, 5, 5, 7, 9 })
        {
            await SeedCallAsync(services, agent, endpoint, new TokenUsage(1, 1), latencyMs: ms);
        }

        var result = await repo.GetSummaryAsync(new AgentCallFilter(), CancellationToken);

        // mean 5; population variance (232 - 40²/8) / 8 = 4, so the deviation is 2.
        result.AvgLatencyMs.Should().BeApproximately(5d, 1e-6);
        result.LatencyStdDevMs.Should().BeApproximately(2d, 1e-6);
    }

    [TestMethod]
    public async Task GetSummary_RespectsTimeRange()
    {
        IServiceProvider services = GetServices();
        var repo = services.GetRequiredService<IAgentCallRepository>();
        var (agent, endpoint) = await SeedAgentAsync(services);

        await SeedCallAsync(services, agent, endpoint, new TokenUsage(100, 100), latencyMs: 10,
            createdAt: DateTimeOffset.UtcNow.AddDays(-10));
        await SeedCallAsync(services, agent, endpoint, new TokenUsage(5, 5), latencyMs: 10);

        var result = await repo.GetSummaryAsync(
            new AgentCallFilter(From: DateTimeOffset.UtcNow.AddDays(-1)),
            CancellationToken);

        result.Count.Should().Be(1);
        result.InputTokens.Should().Be(5);
    }

    [TestMethod]
    public async Task GetSummary_RespectsAgentScope()
    {
        IServiceProvider services = GetServices();
        var repo = services.GetRequiredService<IAgentCallRepository>();
        var (agent, endpoint) = await SeedAgentAsync(services);
        var (otherAgent, otherEndpoint) = await SeedAgentAsync(services);

        await SeedCallAsync(services, agent, endpoint, new TokenUsage(10, 10), latencyMs: 10);
        await SeedCallAsync(services, otherAgent, otherEndpoint, new TokenUsage(999, 999), latencyMs: 10);

        var result = await repo.GetSummaryAsync(new AgentCallFilter(AgentId: agent.Id), CancellationToken);

        result.Count.Should().Be(1);
        result.InputTokens.Should().Be(10);
    }

    private async Task<(IAgent Agent, IModelEndpoint Endpoint)> SeedAgentAsync(IServiceProvider services)
    {
        var agent = await services.GetRequiredService<IDomainEntityGenerator<IAgent>>().CreateAsync(CancellationToken);
        var endpoint = await services.GetRequiredService<IDomainEntityGenerator<IModelEndpoint>>()
            .GetOrCreateAsync(CancellationToken);
        return (agent, endpoint);
    }

    /// <summary>Endpoint with explicit per-million pricing, so cost assertions are exact.</summary>
    private async Task<IModelEndpoint> SeedEndpointAsync(
        IServiceProvider services,
        decimal inputCost,
        decimal outputCost)
    {
        var model = await services.GetRequiredService<IDomainEntityGenerator<IModel>>().CreateAsync(CancellationToken);
        var provider = await services.GetRequiredService<IDomainEntityGenerator<IModelProvider>>()
            .GetOrCreateAsync(CancellationToken);
        var endpoint = services.GetRequiredService<IModelEndpoint.CreateNew>()(
            model, provider, inputCost, outputCost, cachedInputTokenCost: null);
        return await services.GetRequiredService<IRepository<IModelEndpoint>>().AddAsync(endpoint, CancellationToken);
    }

    /// <summary>
    /// Seeds a call with controlled usage/latency/status. A null <paramref name="usage"/> produces a
    /// response-less error trace — the shape whose latency is unknown and must not drag the mean.
    /// </summary>
    private async Task<IAgentCall> SeedCallAsync(
        IServiceProvider services,
        IAgent agent,
        IModelEndpoint endpoint,
        TokenUsage? usage,
        double? latencyMs,
        DateTimeOffset? createdAt = null,
        HttpStatusCode? httpStatus = null)
    {
        var conversationGen = services.GetRequiredService<IDomainObjectGenerator<Conversation>>();
        var createCompletion = services.GetRequiredService<ICompletion.Create>();
        var request = await conversationGen.CreateAsync(CancellationToken);

        ICompletion? response = usage is null
            ? null
            : createCompletion(
                new AssistantMessage([Content.FromText("ok")], []),
                usage,
                TimeSpan.FromMilliseconds(latencyMs ?? 0));

        var resolvedHttpStatus = httpStatus ?? (response is null ? HttpStatusCode.BadGateway : HttpStatusCode.OK);

        IAgentCall call = createdAt is { } timestamp
            ? services.GetRequiredService<IAgentCall.CreateExisting>()(
                agent: agent,
                version: agent.CurrentVersion,
                endpoint: endpoint,
                request: request,
                response: response,
                httpStatus: resolvedHttpStatus,
                finishReason: response is null ? null : "stop",
                errorMessage: response is null ? "upstream timeout" : null,
                modelParameters: agent.ModelParameters,
                existing: new SeededEntityData(Guid.NewGuid(), timestamp, timestamp),
                sessionId: null)
            : services.GetRequiredService<IAgentCall.CreateNew>()(
                agent,
                agent.CurrentVersion,
                endpoint,
                request,
                response,
                httpStatus: resolvedHttpStatus,
                errorMessage: response is null ? "upstream timeout" : null,
                sessionId: null);

        var repo = services.GetRequiredService<IRepository<IAgentCall>>();
        return await repo.AddAsync(call, CancellationToken);
    }

    private sealed record SeededEntityData(Guid Id, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt) : IDomainEntityData;
}
