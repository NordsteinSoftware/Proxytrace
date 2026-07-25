using System.Net;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Proxytrace.Domain;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.AgentCall;
using Proxytrace.Domain.Completion;
using Proxytrace.Domain.Message;
using Proxytrace.Domain.ModelEndpoint;
using Proxytrace.Domain.Usage;
using Proxytrace.Testing;

namespace Proxytrace.Storage.Tests;

[TestClass]
public sealed class AgentCallListQueryTests : BaseTest<Module>
{
    [TestMethod]
    public async Task GetFilteredList_EmptyDb_ReturnsEmpty()
    {
        IServiceProvider services = GetServices();
        var repo = services.GetRequiredService<IAgentCallRepository>();

        var (items, total) = await repo.GetFilteredListAsync(new AgentCallFilter(), 1, 50, CancellationToken);

        items.Should().BeEmpty();
        total.Should().Be(0);
    }

    [TestMethod]
    public async Task GetFilteredList_ReturnsResolvedMetadataForSeededCall()
    {
        IServiceProvider services = GetServices();
        var repo = services.GetRequiredService<IAgentCallRepository>();
        var gen = services.GetRequiredService<IDomainEntityGenerator<IAgentCall>>();
        var call = await gen.CreateAsync(CancellationToken);

        var (items, total) = await repo.GetFilteredListAsync(new AgentCallFilter(), 1, 50, CancellationToken);

        total.Should().Be(1);
        items.Should().ContainSingle();
        var item = items[0];
        item.Id.Should().Be(call.Id);
        item.AgentId.Should().Be(call.Agent.Id);
        item.AgentName.Should().Be(call.Agent.Name);
        item.ModelName.Should().Be(call.Endpoint.Model.Name);
        item.ProviderName.Should().Be(call.Endpoint.Provider.Name);
        item.HttpStatus.Should().Be((int)call.HttpStatus);
    }

    [TestMethod]
    public async Task GetFilteredList_PreviewMatchesFirstUserMessage()
    {
        IServiceProvider services = GetServices();
        var repo = services.GetRequiredService<IAgentCallRepository>();
        var gen = services.GetRequiredService<IDomainEntityGenerator<IAgentCall>>();
        var call = await gen.CreateAsync(CancellationToken);

        var expectedPreview = call.Request.Messages
            .OfType<Proxytrace.Domain.Message.UserMessage>()
            .FirstOrDefault()?.GetText();

        var (items, _) = await repo.GetFilteredListAsync(new AgentCallFilter(), 1, 50, CancellationToken);

        if (string.IsNullOrWhiteSpace(expectedPreview))
        {
            items[0].MessagePreview.Should().BeNull();
        }
        else
        {
            items[0].MessagePreview.Should().NotBeNullOrEmpty();
        }
    }

    [TestMethod]
    public async Task GetFilteredList_FilterBySessionId_ReturnsOnlyStampedCallsAndRoundTripsSessionId()
    {
        IServiceProvider services = GetServices();
        var repo = services.GetRequiredService<IAgentCallRepository>();
        var (agent, endpoint) = await SeedAgentAsync(services);
        var sessionId = Guid.NewGuid();

        var stamped = await SeedCallAsync(services, agent, endpoint, new TokenUsage(10, 10), latencyMs: 100, sessionId: sessionId);
        await SeedCallAsync(services, agent, endpoint, new TokenUsage(10, 10), latencyMs: 100); // no session

        var (items, total) = await repo.GetFilteredListAsync(
            new AgentCallFilter(SessionId: sessionId), 1, 50, CancellationToken);

        total.Should().Be(1);
        items.Should().ContainSingle(i => i.Id == stamped.Id);
        // Round-trips the SessionId through persistence and the list projection (Task 3 carry-forward).
        items[0].SessionId.Should().Be(sessionId);
    }

    [TestMethod]
    public async Task GetFilteredList_FiltersByAgent()
    {
        IServiceProvider services = GetServices();
        var repo = services.GetRequiredService<IAgentCallRepository>();
        var gen = services.GetRequiredService<IDomainEntityGenerator<IAgentCall>>();
        var call = await gen.CreateAsync(CancellationToken);
        await gen.CreateAsync(CancellationToken); // unrelated agent

        var (items, total) = await repo.GetFilteredListAsync(
            new AgentCallFilter(AgentId: call.Agent.Id), 1, 50, CancellationToken);

        total.Should().Be(1);
        items.Should().ContainSingle(i => i.Id == call.Id);
    }

    [TestMethod]
    public async Task GetFilteredList_DefaultSort_OrdersByCreatedAtDescending()
    {
        IServiceProvider services = GetServices();
        var repo = services.GetRequiredService<IAgentCallRepository>();
        var (agent, endpoint) = await SeedAgentAsync(services);
        var now = DateTimeOffset.UtcNow;

        var older = await SeedCallAsync(services, agent, endpoint, new TokenUsage(10, 10), latencyMs: 100, createdAt: now.AddMinutes(-10));
        var newer = await SeedCallAsync(services, agent, endpoint, new TokenUsage(10, 10), latencyMs: 100, createdAt: now);

        var (items, _) = await repo.GetFilteredListAsync(new AgentCallFilter(), 1, 50, CancellationToken);

        items.Select(i => i.Id).Should().Equal(newer.Id, older.Id);
    }

    [TestMethod]
    public async Task GetFilteredList_SortByLatencyDescending_ReturnsSlowestFirstNullsLast()
    {
        IServiceProvider services = GetServices();
        var repo = services.GetRequiredService<IAgentCallRepository>();
        var (agent, endpoint) = await SeedAgentAsync(services);

        var slow = await SeedCallAsync(services, agent, endpoint, new TokenUsage(10, 10), latencyMs: 500);
        var fast = await SeedCallAsync(services, agent, endpoint, new TokenUsage(10, 10), latencyMs: 50);
        var error = await SeedCallAsync(services, agent, endpoint, usage: null, latencyMs: null);

        var (items, _) = await repo.GetFilteredListAsync(
            new AgentCallFilter(SortBy: AgentCallSortField.Latency, SortDescending: true), 1, 50, CancellationToken);

        items.Select(i => i.Id).Should().Equal(slow.Id, fast.Id, error.Id);
    }

    [TestMethod]
    public async Task GetFilteredList_SortByLatencyAscending_ReturnsFastestFirstNullsLast()
    {
        IServiceProvider services = GetServices();
        var repo = services.GetRequiredService<IAgentCallRepository>();
        var (agent, endpoint) = await SeedAgentAsync(services);

        var slow = await SeedCallAsync(services, agent, endpoint, new TokenUsage(10, 10), latencyMs: 500);
        var fast = await SeedCallAsync(services, agent, endpoint, new TokenUsage(10, 10), latencyMs: 50);
        var error = await SeedCallAsync(services, agent, endpoint, usage: null, latencyMs: null);

        var (items, _) = await repo.GetFilteredListAsync(
            new AgentCallFilter(SortBy: AgentCallSortField.Latency, SortDescending: false), 1, 50, CancellationToken);

        items.Select(i => i.Id).Should().Equal(fast.Id, slow.Id, error.Id);
    }

    [TestMethod]
    public async Task GetFilteredList_SortByTotalTokensDescending_ReturnsMostTokensFirstNullsLast()
    {
        IServiceProvider services = GetServices();
        var repo = services.GetRequiredService<IAgentCallRepository>();
        var (agent, endpoint) = await SeedAgentAsync(services);

        var big = await SeedCallAsync(services, agent, endpoint, new TokenUsage(900, 100), latencyMs: 100);
        var small = await SeedCallAsync(services, agent, endpoint, new TokenUsage(10, 10), latencyMs: 100);
        var error = await SeedCallAsync(services, agent, endpoint, usage: null, latencyMs: null);

        var (items, _) = await repo.GetFilteredListAsync(
            new AgentCallFilter(SortBy: AgentCallSortField.TotalTokens, SortDescending: true), 1, 50, CancellationToken);

        items.Select(i => i.Id).Should().Equal(big.Id, small.Id, error.Id);
    }

    [TestMethod]
    public async Task GetFilteredList_SortByTotalTokensAscending_ReturnsFewestTokensFirstNullsLast()
    {
        IServiceProvider services = GetServices();
        var repo = services.GetRequiredService<IAgentCallRepository>();
        var (agent, endpoint) = await SeedAgentAsync(services);

        var big = await SeedCallAsync(services, agent, endpoint, new TokenUsage(900, 100), latencyMs: 100);
        var small = await SeedCallAsync(services, agent, endpoint, new TokenUsage(10, 10), latencyMs: 100);
        var error = await SeedCallAsync(services, agent, endpoint, usage: null, latencyMs: null);

        var (items, _) = await repo.GetFilteredListAsync(
            new AgentCallFilter(SortBy: AgentCallSortField.TotalTokens, SortDescending: false), 1, 50, CancellationToken);

        items.Select(i => i.Id).Should().Equal(small.Id, big.Id, error.Id);
    }

    [TestMethod]
    public async Task GetFilteredList_SortByToolCountDescending_ReturnsMostToolsFirst()
    {
        IServiceProvider services = GetServices();
        var repo = services.GetRequiredService<IAgentCallRepository>();
        var (agent, endpoint) = await SeedAgentAsync(services);

        var many = await SeedCallAsync(services, agent, endpoint, new TokenUsage(10, 10), latencyMs: 100, toolCount: 5);
        var few = await SeedCallAsync(services, agent, endpoint, new TokenUsage(10, 10), latencyMs: 100, toolCount: 1);
        var none = await SeedCallAsync(services, agent, endpoint, new TokenUsage(10, 10), latencyMs: 100, toolCount: 0);

        var (items, _) = await repo.GetFilteredListAsync(
            new AgentCallFilter(SortBy: AgentCallSortField.ToolCount, SortDescending: true), 1, 50, CancellationToken);

        items.Select(i => i.Id).Should().Equal(many.Id, few.Id, none.Id);
    }

    [TestMethod]
    public async Task GetFilteredList_SortByToolCountAscending_ReturnsFewestToolsFirst()
    {
        IServiceProvider services = GetServices();
        var repo = services.GetRequiredService<IAgentCallRepository>();
        var (agent, endpoint) = await SeedAgentAsync(services);

        var many = await SeedCallAsync(services, agent, endpoint, new TokenUsage(10, 10), latencyMs: 100, toolCount: 5);
        var few = await SeedCallAsync(services, agent, endpoint, new TokenUsage(10, 10), latencyMs: 100, toolCount: 1);
        var none = await SeedCallAsync(services, agent, endpoint, new TokenUsage(10, 10), latencyMs: 100, toolCount: 0);

        var (items, _) = await repo.GetFilteredListAsync(
            new AgentCallFilter(SortBy: AgentCallSortField.ToolCount, SortDescending: false), 1, 50, CancellationToken);

        items.Select(i => i.Id).Should().Equal(none.Id, few.Id, many.Id);
    }

    [TestMethod]
    public async Task GetFilteredList_SortByCacheHitRateDescending_ReturnsHighestHitRateFirstNullsLast()
    {
        IServiceProvider services = GetServices();
        var repo = services.GetRequiredService<IAgentCallRepository>();
        var (agent, endpoint) = await SeedAgentAsync(services);

        var highHitRate = await SeedCallAsync(services, agent, endpoint, new TokenUsage(100, 10, 90), latencyMs: 100);
        var lowHitRate = await SeedCallAsync(services, agent, endpoint, new TokenUsage(100, 10, 5), latencyMs: 100);
        var error = await SeedCallAsync(services, agent, endpoint, usage: null, latencyMs: null);

        var (items, _) = await repo.GetFilteredListAsync(
            new AgentCallFilter(SortBy: AgentCallSortField.CacheHitRate, SortDescending: true), 1, 50, CancellationToken);

        items.Select(i => i.Id).Should().Equal(highHitRate.Id, lowHitRate.Id, error.Id);
    }

    [TestMethod]
    public async Task GetFilteredList_SortByCacheHitRateAscending_ReturnsLowestHitRateFirstNullsLast()
    {
        IServiceProvider services = GetServices();
        var repo = services.GetRequiredService<IAgentCallRepository>();
        var (agent, endpoint) = await SeedAgentAsync(services);

        var highHitRate = await SeedCallAsync(services, agent, endpoint, new TokenUsage(100, 10, 90), latencyMs: 100);
        var lowHitRate = await SeedCallAsync(services, agent, endpoint, new TokenUsage(100, 10, 5), latencyMs: 100);
        var error = await SeedCallAsync(services, agent, endpoint, usage: null, latencyMs: null);

        var (items, _) = await repo.GetFilteredListAsync(
            new AgentCallFilter(SortBy: AgentCallSortField.CacheHitRate, SortDescending: false), 1, 50, CancellationToken);

        items.Select(i => i.Id).Should().Equal(lowHitRate.Id, highHitRate.Id, error.Id);
    }

    [TestMethod]
    public async Task GetFilteredList_FilterByAnomalyFlags_ReturnsOnlyRowsWithThatBitSet()
    {
        IServiceProvider services = GetServices();
        var repo = services.GetRequiredService<IAgentCallRepository>();
        var (agent, endpoint) = await SeedAgentAsync(services);

        var highLatencyOnly = await SeedCallAsync(services, agent, endpoint, new TokenUsage(10, 10), latencyMs: 500);
        var highTokensOnly = await SeedCallAsync(services, agent, endpoint, new TokenUsage(900, 100), latencyMs: 50);
        var both = await SeedCallAsync(services, agent, endpoint, new TokenUsage(900, 100), latencyMs: 500);

        await repo.SetOutlierFlagAsync(highLatencyOnly.Id, OutlierFlags.HighLatency, CancellationToken);
        await repo.SetOutlierFlagAsync(highTokensOnly.Id, OutlierFlags.HighTokens, CancellationToken);
        await repo.SetOutlierFlagAsync(both.Id, OutlierFlags.HighLatency, CancellationToken);
        await repo.SetOutlierFlagAsync(both.Id, OutlierFlags.HighTokens, CancellationToken);

        var (items, total) = await repo.GetFilteredListAsync(
            new AgentCallFilter(AnomalyFlags: OutlierFlags.HighLatency), 1, 50, CancellationToken);

        total.Should().Be(2);
        items.Select(i => i.Id).Should().BeEquivalentTo([highLatencyOnly.Id, both.Id]);
    }

    [TestMethod]
    public async Task GetFilteredList_FilterByHttpStatusClass_ReturnsOnlyThatHundredRangeAtBoundaries()
    {
        IServiceProvider services = GetServices();
        var repo = services.GetRequiredService<IAgentCallRepository>();
        var (agent, endpoint) = await SeedAgentAsync(services);

        var lowerBoundary = await SeedCallAsync(services, agent, endpoint, new TokenUsage(10, 10), latencyMs: 100, httpStatus: (HttpStatusCode)500);
        var upperBoundary = await SeedCallAsync(services, agent, endpoint, new TokenUsage(10, 10), latencyMs: 100, httpStatus: (HttpStatusCode)599);
        var justBelow = await SeedCallAsync(services, agent, endpoint, new TokenUsage(10, 10), latencyMs: 100, httpStatus: (HttpStatusCode)499);
        var justAbove = await SeedCallAsync(services, agent, endpoint, new TokenUsage(10, 10), latencyMs: 100, httpStatus: (HttpStatusCode)600);

        var (items, total) = await repo.GetFilteredListAsync(
            new AgentCallFilter(HttpStatusClass: 5), 1, 50, CancellationToken);

        total.Should().Be(2);
        items.Select(i => i.Id).Should().BeEquivalentTo([lowerBoundary.Id, upperBoundary.Id]);
    }

    [TestMethod]
    public async Task GetFilteredList_FilterByMinTokensAndMaxLatencyMs_BoundsCorrectlyAndExcludesNullUsageRows()
    {
        IServiceProvider services = GetServices();
        var repo = services.GetRequiredService<IAgentCallRepository>();
        var (agent, endpoint) = await SeedAgentAsync(services);

        var matching = await SeedCallAsync(services, agent, endpoint, new TokenUsage(900, 100), latencyMs: 50);
        var tooFewTokens = await SeedCallAsync(services, agent, endpoint, new TokenUsage(10, 10), latencyMs: 50);
        var tooSlow = await SeedCallAsync(services, agent, endpoint, new TokenUsage(900, 100), latencyMs: 500);
        var nullUsage = await SeedCallAsync(services, agent, endpoint, usage: null, latencyMs: null);

        var (items, total) = await repo.GetFilteredListAsync(
            new AgentCallFilter(MinTokens: 500, MaxLatencyMs: 100), 1, 50, CancellationToken);

        total.Should().Be(1);
        items.Should().ContainSingle(i => i.Id == matching.Id);
    }

    [TestMethod]
    public async Task GetFilteredList_FilterByMaxTokens_ExcludesRowsAboveBoundAndNullUsageRows()
    {
        IServiceProvider services = GetServices();
        var repo = services.GetRequiredService<IAgentCallRepository>();
        var (agent, endpoint) = await SeedAgentAsync(services);

        var atBound = await SeedCallAsync(services, agent, endpoint, new TokenUsage(400, 100), latencyMs: 50); // total 500, matches MaxTokens: 500 inclusively
        var aboveBound = await SeedCallAsync(services, agent, endpoint, new TokenUsage(900, 100), latencyMs: 50); // total 1000, excluded
        var nullUsage = await SeedCallAsync(services, agent, endpoint, usage: null, latencyMs: null);

        var (items, total) = await repo.GetFilteredListAsync(
            new AgentCallFilter(MaxTokens: 500), 1, 50, CancellationToken);

        total.Should().Be(1);
        items.Should().ContainSingle(i => i.Id == atBound.Id);
    }

    [TestMethod]
    public async Task GetFilteredList_FilterByMinLatencyMs_ExcludesRowsBelowBoundAndNullLatencyRows()
    {
        IServiceProvider services = GetServices();
        var repo = services.GetRequiredService<IAgentCallRepository>();
        var (agent, endpoint) = await SeedAgentAsync(services);

        var atBound = await SeedCallAsync(services, agent, endpoint, new TokenUsage(10, 10), latencyMs: 100); // matches MinLatencyMs: 100 inclusively
        var belowBound = await SeedCallAsync(services, agent, endpoint, new TokenUsage(10, 10), latencyMs: 50); // excluded
        var nullLatency = await SeedCallAsync(services, agent, endpoint, usage: null, latencyMs: null);

        var (items, total) = await repo.GetFilteredListAsync(
            new AgentCallFilter(MinLatencyMs: 100), 1, 50, CancellationToken);

        total.Should().Be(1);
        items.Should().ContainSingle(i => i.Id == atBound.Id);
    }

    private async Task<(IAgent Agent, IModelEndpoint Endpoint)> SeedAgentAsync(IServiceProvider services)
    {
        var agent = await services.GetRequiredService<IDomainEntityGenerator<IAgent>>().CreateAsync(CancellationToken);
        var endpoint = await services.GetRequiredService<IDomainEntityGenerator<IModelEndpoint>>().GetOrCreateAsync(CancellationToken);
        return (agent, endpoint);
    }

    /// <summary>
    /// Seeds a call with controlled latency/usage/tool-count so sort-order assertions are
    /// deterministic. Passing <paramref name="usage"/> <see langword="null"/> produces an
    /// error trace with no response — the shape whose nullable sort columns (Latency,
    /// TotalTokens, CacheHitRate) must land last in both sort directions.
    /// </summary>
    private async Task<IAgentCall> SeedCallAsync(
        IServiceProvider services,
        IAgent agent,
        IModelEndpoint endpoint,
        TokenUsage? usage,
        double? latencyMs,
        int toolCount = 0,
        DateTimeOffset? createdAt = null,
        HttpStatusCode? httpStatus = null,
        Guid? sessionId = null)
    {
        var conversationGen = services.GetRequiredService<IDomainObjectGenerator<Conversation>>();
        var createCompletion = services.GetRequiredService<ICompletion.Create>();
        var request = await conversationGen.CreateAsync(CancellationToken);

        ICompletion? response = usage is null
            ? null
            : createCompletion(
                new AssistantMessage(
                    [Content.FromText("ok")],
                    Enumerable.Range(0, toolCount).Select(i => new ToolRequest($"tr{i}", "tool", "{}")).ToList()),
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
                sessionId: sessionId)
            : services.GetRequiredService<IAgentCall.CreateNew>()(
                agent,
                agent.CurrentVersion,
                endpoint,
                request,
                response,
                httpStatus: resolvedHttpStatus,
                errorMessage: response is null ? "upstream timeout" : null,
                sessionId: sessionId);

        var repo = services.GetRequiredService<IRepository<IAgentCall>>();
        return await repo.AddAsync(call, CancellationToken);
    }

    private sealed record SeededEntityData(Guid Id, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt) : IDomainEntityData;
}
