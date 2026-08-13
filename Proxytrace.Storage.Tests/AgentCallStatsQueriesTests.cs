using System.Net;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Proxytrace.Domain.Statistics;
using Proxytrace.Domain;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.AgentCall;
using Proxytrace.Domain.Completion;
using Proxytrace.Domain.Inference;
using Proxytrace.Domain.Message;
using Proxytrace.Domain.ModelEndpoint;
using Proxytrace.Domain.Project;
using Proxytrace.Domain.Prompt;
using Proxytrace.Domain.Usage;
using Nordstein.Core.Testing;

namespace Proxytrace.Storage.Tests;

[TestClass]
public sealed class AgentCallStatsQueriesTests : BaseTest<Module>
{
    [TestMethod]
    public async Task GetSummary_EmptyDb_ReturnsZeros()
    {
        IServiceProvider services = GetServices();
        var reader = services.GetRequiredService<IAgentCallStatsReader>();

        var summary = await reader.GetSummaryAsync(new StatisticsFilter(), CancellationToken);

        summary.TotalCalls.Should().Be(0);
        summary.TotalInputTokens.Should().Be(0);
        summary.TotalOutputTokens.Should().Be(0);
        summary.AvgLatencyMs.Should().Be(0);
    }

    [TestMethod]
    public async Task GetSummary_AfterSeedingCalls_AggregatesTotals()
    {
        IServiceProvider services = GetServices();
        var reader = services.GetRequiredService<IAgentCallStatsReader>();
        var gen = services.GetRequiredService<IDomainEntityGenerator<IAgentCall>>();
        await gen.CreateAsync(CancellationToken);
        await gen.CreateAsync(CancellationToken);

        var summary = await reader.GetSummaryAsync(new StatisticsFilter(), CancellationToken);

        summary.TotalCalls.Should().Be(2);
    }

    [TestMethod]
    public async Task GetTokenUsage_AggregatesPerEndpointPerDate()
    {
        IServiceProvider services = GetServices();
        var reader = services.GetRequiredService<IAgentCallStatsReader>();
        var gen = services.GetRequiredService<IDomainEntityGenerator<IAgentCall>>();
        await gen.CreateAsync(CancellationToken);

        var rows = await reader.GetTokenUsageAsync(new StatisticsFilter(), StatisticsBucket.Daily, CancellationToken);

        rows.Should().NotBeEmpty();
    }

    [TestMethod]
    public async Task GetEarliestCall_EmptyDb_ReturnsNull()
    {
        IServiceProvider services = GetServices();
        var reader = services.GetRequiredService<IAgentCallStatsReader>();

        var earliest = await reader.GetEarliestCallAsync(new StatisticsFilter(), CancellationToken);

        earliest.Should().BeNull();
    }

    [TestMethod]
    public async Task GetEarliestCall_AfterSeedingCalls_ReturnsOldestTimestamp()
    {
        IServiceProvider services = GetServices();
        var reader = services.GetRequiredService<IAgentCallStatsReader>();
        var gen = services.GetRequiredService<IDomainEntityGenerator<IAgentCall>>();
        var first = await gen.CreateAsync(CancellationToken);
        await gen.CreateAsync(CancellationToken);

        var earliest = await reader.GetEarliestCallAsync(new StatisticsFilter(), CancellationToken);

        earliest.Should().NotBeNull();
        earliest.Should().BeOnOrBefore(first.CreatedAt);
    }

    [TestMethod]
    public async Task GetErrorRates_EmptyDb_ReturnsEmpty()
    {
        IServiceProvider services = GetServices();
        var reader = services.GetRequiredService<IAgentCallStatsReader>();

        var rows = await reader.GetErrorRatesAsync(new StatisticsFilter(), CancellationToken);

        rows.Should().BeEmpty();
    }

    [TestMethod]
    public async Task GetAgentBreakdown_EmptyDb_ReturnsEmpty()
    {
        IServiceProvider services = GetServices();
        var reader = services.GetRequiredService<IAgentCallStatsReader>();

        var rows = await reader.GetAgentBreakdownAsync(new StatisticsFilter(), CancellationToken);

        rows.Should().BeEmpty();
    }

    [TestMethod]
    public async Task GetCostEstimate_EmptyDb_ReturnsEmpty()
    {
        IServiceProvider services = GetServices();
        var reader = services.GetRequiredService<IAgentCallStatsReader>();

        var rows = await reader.GetCostEstimateAsync(new StatisticsFilter(), CancellationToken);

        rows.Should().BeEmpty();
    }

    [TestMethod]
    public async Task GetCostEstimate_SeededCalls_AgreesWithCalculateCost()
    {
        IServiceProvider services = GetServices();
        var reader = services.GetRequiredService<IAgentCallStatsReader>();

        // An endpoint with known per-1M-token prices (input <= output, cached <= input).
        var endpointSeedGen = services.GetRequiredService<IDomainObjectGenerator<IModelEndpoint>>();
        var seed = await endpointSeedGen.CreateAsync(CancellationToken);
        var createEndpoint = services.GetRequiredService<IModelEndpoint.CreateNew>();
        var endpointRepo = services.GetRequiredService<IRepository<IModelEndpoint>>();
        IModelEndpoint endpoint = await endpointRepo.AddAsync(
            createEndpoint(seed.Model, seed.Provider, inputTokenCost: 2.5m, outputTokenCost: 10m, cachedInputTokenCost: 1m),
            CancellationToken);

        // Two calls on that endpoint with known token counts.
        var agentGen = services.GetRequiredService<IDomainEntityGenerator<IAgent>>();
        var agent = await agentGen.CreateAsync(CancellationToken);
        var conversationGen = services.GetRequiredService<IDomainObjectGenerator<Conversation>>();
        var completionGen = services.GetRequiredService<IDomainObjectGenerator<ICompletion>>();
        var sampleResponse = (await completionGen.CreateAsync(CancellationToken)).Response;
        var createCompletion = services.GetRequiredService<ICompletion.Create>();
        var createCall = services.GetRequiredService<IAgentCall.CreateNew>();
        var callRepo = services.GetRequiredService<IRepository<IAgentCall>>();

        var usages = new[] { new TokenUsage(1000, 500, 200), new TokenUsage(3000, 1500, 0) };
        foreach (var usage in usages)
        {
            var response = createCompletion(sampleResponse, usage, TimeSpan.FromMilliseconds(10));
            var request = await conversationGen.CreateAsync(CancellationToken);
            var call = createCall(agent, agent.CurrentVersion, endpoint, request, response);
            await callRepo.AddAsync(call, CancellationToken);
        }

        var rows = await reader.GetCostEstimateAsync(new StatisticsFilter(), CancellationToken);

        var stat = rows.Single(r => r.EndpointId == endpoint.Id);
        var summed = new TokenUsage(
            usages.Aggregate(0UL, (acc, u) => acc + u.InputTokenCount),
            usages.Aggregate(0UL, (acc, u) => acc + u.OutputTokenCount),
            usages.Aggregate(0UL, (acc, u) => acc + u.CachedInputTokenCount));

        // Single source of truth: the estimate must match ModelEndpoint.CalculateCost (which divides
        // by 1M). Concretely TotalCostEur is ~0.0297 EUR here, not the ~29,700 EUR the pre-fix bug gave.
        stat.TotalCostEur.Should().Be(endpoint.CalculateCost(summed));
    }

    [TestMethod]
    public async Task GetLatency_EmptyDb_ReturnsEmpty()
    {
        IServiceProvider services = GetServices();
        var reader = services.GetRequiredService<IAgentCallStatsReader>();

        var rows = await reader.GetLatencyAsync(new StatisticsFilter(), CancellationToken);

        rows.Should().BeEmpty();
    }

    [TestMethod]
    public async Task GetModelBreakdown_AfterSeed_ReturnsRowPerEndpoint()
    {
        IServiceProvider services = GetServices();
        var reader = services.GetRequiredService<IAgentCallStatsReader>();
        var gen = services.GetRequiredService<IDomainEntityGenerator<IAgentCall>>();
        await gen.CreateAsync(CancellationToken);

        var rows = await reader.GetModelBreakdownAsync(new StatisticsFilter(), CancellationToken);

        rows.Should().NotBeEmpty();
        rows[0].CallCount.Should().BeGreaterThan(0);
    }

    [TestMethod]
    public async Task GetSummary_WithExcludeSystemAgents_DropsSystemAgentCalls()
    {
        IServiceProvider services = GetServices();
        var reader = services.GetRequiredService<IAgentCallStatsReader>();
        var gen = services.GetRequiredService<IDomainEntityGenerator<IAgentCall>>();

        await gen.CreateAsync(CancellationToken);          // an ordinary user-agent call
        await SeedSystemAgentCall(services);               // a system-agent (Tracey/evaluator) call

        var all = await reader.GetSummaryAsync(new StatisticsFilter(), CancellationToken);
        var nonSystem = await reader.GetSummaryAsync(new StatisticsFilter(ExcludeSystemAgents: true), CancellationToken);

        all.TotalCalls.Should().Be(2);
        nonSystem.TotalCalls.Should().Be(1);
    }

    [TestMethod]
    public async Task GetAgentBreakdown_WithExcludeSystemAgents_OmitsSystemAgent()
    {
        IServiceProvider services = GetServices();
        var reader = services.GetRequiredService<IAgentCallStatsReader>();
        var gen = services.GetRequiredService<IDomainEntityGenerator<IAgentCall>>();

        var userCall = await gen.CreateAsync(CancellationToken);
        var systemAgentId = await SeedSystemAgentCall(services);

        var rows = await reader.GetAgentBreakdownAsync(new StatisticsFilter(ExcludeSystemAgents: true), CancellationToken);

        rows.Select(r => r.AgentId).Should().Contain(userCall.Agent.Id).And.NotContain(systemAgentId);
    }

    private async Task<Guid> SeedSystemAgentCall(IServiceProvider services)
    {
        var systemAgent = await services.GetRequiredService<IAgentGenerator>()
            .CreateAsync("Tracey", systemPrompt: "internal", isSystemAgent: true, cancellationToken: CancellationToken);
        var endpoint = await services.GetRequiredService<IDomainEntityGenerator<IModelEndpoint>>().GetOrCreateAsync(CancellationToken);
        var request = await services.GetRequiredService<IDomainObjectGenerator<Conversation>>().CreateAsync(CancellationToken);
        var response = await services.GetRequiredService<IDomainObjectGenerator<ICompletion>>().CreateAsync(CancellationToken);
        var createCall = services.GetRequiredService<IAgentCall.CreateNew>();
        var callRepo = services.GetRequiredService<IRepository<IAgentCall>>();

        var call = createCall(systemAgent, systemAgent.CurrentVersion, endpoint, request, response);
        await callRepo.AddAsync(call, CancellationToken);
        return systemAgent.Id;
    }

    // ── multi-project scope (#483) ─────────────────────────────────────────────
    //
    // The LINQ chokepoint Query() is what these exercise: on the in-memory provider the latency
    // percentiles fall back to it too, so the raw-SQL twin is covered separately by
    // StatisticsFilterWhereTests (its WHERE fragment) and StatisticsFilterParityTests (that both
    // paths know every filter member).

    [TestMethod]
    public async Task GetSummary_ScopedToSeveralProjects_CountsOnlyThoseProjectsCalls()
    {
        IServiceProvider services = GetServices();
        var reader = services.GetRequiredService<IAgentCallStatsReader>();
        var first = await SeedAgentInNewProjectAsync(services, "first");
        var second = await SeedAgentInNewProjectAsync(services, "second");
        var outsider = await SeedAgentInNewProjectAsync(services, "outsider");
        await SeedPlainCallAsync(services, first);
        await SeedPlainCallAsync(services, second);
        await SeedPlainCallAsync(services, outsider);

        var summary = await reader.GetSummaryAsync(
            new StatisticsFilter(ProjectIds: [first.Project.Id, second.Project.Id]), CancellationToken);

        summary.TotalCalls.Should().Be(2);
    }

    [TestMethod]
    public async Task GetAgentBreakdown_ScopedToSeveralProjects_ListsOnlyThoseProjectsAgents()
    {
        IServiceProvider services = GetServices();
        var reader = services.GetRequiredService<IAgentCallStatsReader>();
        var first = await SeedAgentInNewProjectAsync(services, "first");
        var second = await SeedAgentInNewProjectAsync(services, "second");
        var outsider = await SeedAgentInNewProjectAsync(services, "outsider");
        await SeedPlainCallAsync(services, first);
        await SeedPlainCallAsync(services, second);
        await SeedPlainCallAsync(services, outsider);

        var rows = await reader.GetAgentBreakdownAsync(
            new StatisticsFilter(ProjectIds: [first.Project.Id, second.Project.Id]), CancellationToken);

        rows.Select(r => r.AgentId).Should().BeEquivalentTo(new[] { first.Id, second.Id });
    }

    [TestMethod]
    public async Task GetLatency_ScopedToSeveralProjects_SamplesOnlyThoseProjectsCalls()
    {
        IServiceProvider services = GetServices();
        var reader = services.GetRequiredService<IAgentCallStatsReader>();
        var first = await SeedAgentInNewProjectAsync(services, "first");
        var second = await SeedAgentInNewProjectAsync(services, "second");
        var outsider = await SeedAgentInNewProjectAsync(services, "outsider");
        await SeedPlainCallAsync(services, first);
        await SeedPlainCallAsync(services, second);
        await SeedPlainCallAsync(services, outsider);

        var rows = await reader.GetLatencyAsync(
            new StatisticsFilter(ProjectIds: [first.Project.Id, second.Project.Id]), CancellationToken);

        rows.Sum(r => r.SampleCount).Should().Be(2);
    }

    [TestMethod]
    public async Task GetSummary_ScopedToAnEmptyProjectSet_IsNotRestricted()
    {
        // An empty set means "no set filter", not "no rows" — endpoints short-circuit an empty scope
        // before they ever build a filter (ProjectListScope.IsEmpty), and both filter paths mirror
        // AgentCallFilter in ignoring a zero-length set.
        IServiceProvider services = GetServices();
        var reader = services.GetRequiredService<IAgentCallStatsReader>();
        await services.GetRequiredService<IDomainEntityGenerator<IAgentCall>>().CreateAsync(CancellationToken);

        var summary = await reader.GetSummaryAsync(new StatisticsFilter(ProjectIds: []), CancellationToken);

        summary.TotalCalls.Should().Be(1);
    }

    /// <summary>An agent in a project of its own, so a test can tell two tenants' rows apart.</summary>
    private async Task<IAgent> SeedAgentInNewProjectAsync(IServiceProvider services, string name)
    {
        var endpoint = await services.GetRequiredService<IDomainEntityGenerator<IModelEndpoint>>()
            .GetOrCreateAsync(CancellationToken);
        var project = await services.GetRequiredService<IProjectRepository>().AddAsync(
            services.GetRequiredService<IProject.CreateNew>()($"P-{name}-{Guid.NewGuid():N}", endpoint, []),
            CancellationToken);
        var template = services.GetRequiredService<IPromptTemplate.Create>()($"T-{name}", "You are a test agent.");
        var parameters = services.GetRequiredService<IModelParameters.Create>()(null, null, null, null, null);

        return await services.GetRequiredService<IAgentRepository>().AddAsync(
            services.GetRequiredService<IAgent.CreateNew>()($"A-{name}", template, [], endpoint, project, parameters),
            CancellationToken);
    }

    private async Task<IAgentCall> SeedPlainCallAsync(IServiceProvider services, IAgent agent)
    {
        AssistantMessage sample = (await services.GetRequiredService<IDomainObjectGenerator<ICompletion>>()
            .CreateAsync(CancellationToken)).Response;
        return await SeedCallAsync(services, agent, agent.Endpoint, sample, conversationId: null,
            new TokenUsage(100, 50, 0), latencyMs: 120, toolCount: 0, HttpStatusCode.OK);
    }

    [TestMethod]
    public async Task GetSummary_FilteredByUnknownAgent_ReturnsZeros()
    {
        IServiceProvider services = GetServices();
        var reader = services.GetRequiredService<IAgentCallStatsReader>();
        var gen = services.GetRequiredService<IDomainEntityGenerator<IAgentCall>>();
        await gen.CreateAsync(CancellationToken);

        var summary = await reader.GetSummaryAsync(new StatisticsFilter(AgentId: Guid.NewGuid()), CancellationToken);

        summary.TotalCalls.Should().Be(0);
    }

    [TestMethod]
    public async Task GetTokenUsage_SumsTokensAcrossCallsInSameBucket()
    {
        IServiceProvider services = GetServices();
        var reader = services.GetRequiredService<IAgentCallStatsReader>();
        var gen = services.GetRequiredService<IDomainEntityGenerator<IAgentCall>>();
        var a = await gen.CreateAsync(CancellationToken);
        var b = await gen.CreateAsync(CancellationToken);

        var rows = await reader.GetTokenUsageAsync(new StatisticsFilter(), StatisticsBucket.Daily, CancellationToken);

        long expectedInput =
            (long)(a.Response?.Usage?.InputTokenCount ?? 0) + (long)(b.Response?.Usage?.InputTokenCount ?? 0);
        rows.Sum(r => r.InputTokens ?? 0L).Should().Be(expectedInput);
    }

    [TestMethod]
    public async Task GetCallTrends_TotalTracesAcrossBuckets_EqualsSeededCount()
    {
        IServiceProvider services = GetServices();
        var reader = services.GetRequiredService<IAgentCallStatsReader>();
        var gen = services.GetRequiredService<IDomainEntityGenerator<IAgentCall>>();
        await gen.CreateAsync(CancellationToken);
        await gen.CreateAsync(CancellationToken);
        await gen.CreateAsync(CancellationToken);

        var from = DateTimeOffset.UtcNow.AddHours(-1);
        var to = DateTimeOffset.UtcNow.AddHours(1);
        var trends = await reader.GetCallTrendsAsync(new StatisticsFilter(), 10, from, to, CancellationToken);

        trends.Traces.Should().HaveCount(10);
        trends.Traces.Sum().Should().Be(3d);
    }

    [TestMethod]
    public async Task GetPulse_SeededCalls_ReturnsDenseBucketArrayPreservingTotal()
    {
        IServiceProvider services = GetServices();
        var reader = services.GetRequiredService<IAgentCallStatsReader>();
        var gen = services.GetRequiredService<IDomainEntityGenerator<IAgentCall>>();
        await gen.CreateAsync(CancellationToken);
        await gen.CreateAsync(CancellationToken);
        await gen.CreateAsync(CancellationToken);

        // Calls are persisted "now" — a window reaching 1 minute past now guarantees they land inside.
        var to = DateTimeOffset.UtcNow.AddMinutes(1);
        var from = to.AddMinutes(-60);
        IReadOnlyList<int> pulse = await reader.GetPulseAsync(new StatisticsFilter(), from, to, 60, CancellationToken);

        pulse.Should().HaveCount(60);
        pulse.Sum().Should().Be(3);
    }

    [TestMethod]
    public async Task GetPulse_EmptyWindow_ReturnsAllZeroBuckets()
    {
        IServiceProvider services = GetServices();
        var reader = services.GetRequiredService<IAgentCallStatsReader>();

        var to = DateTimeOffset.UtcNow;
        IReadOnlyList<int> pulse = await reader.GetPulseAsync(new StatisticsFilter(), to.AddMinutes(-60), to, 60, CancellationToken);

        pulse.Should().HaveCount(60);
        pulse.Sum().Should().Be(0);
    }

    [TestMethod]
    public async Task GetPulse_ForeignProjectFilter_CountsNothing()
    {
        IServiceProvider services = GetServices();
        var reader = services.GetRequiredService<IAgentCallStatsReader>();
        var gen = services.GetRequiredService<IDomainEntityGenerator<IAgentCall>>();
        await gen.CreateAsync(CancellationToken);

        var to = DateTimeOffset.UtcNow.AddMinutes(1);
        IReadOnlyList<int> pulse = await reader.GetPulseAsync(
            new StatisticsFilter(ProjectId: Guid.NewGuid()), to.AddMinutes(-60), to, 60, CancellationToken);

        pulse.Sum().Should().Be(0);
    }

    [TestMethod]
    public async Task GetAgentWindow_AggregatesOnlyThatAgentsCalls()
    {
        IServiceProvider services = GetServices();
        var reader = services.GetRequiredService<IAgentCallStatsReader>();
        var gen = services.GetRequiredService<IDomainEntityGenerator<IAgentCall>>();
        var call = await gen.CreateAsync(CancellationToken);
        await gen.CreateAsync(CancellationToken); // a different agent — must be excluded

        var (series, summary) = await reader.GetAgentWindowAsync(
            call.Agent.Id,
            DateTimeOffset.UtcNow.AddHours(-1),
            DateTimeOffset.UtcNow.AddHours(1),
            StatisticsBucket.Hourly,
            CancellationToken);

        summary.TotalTraces.Should().Be(1);
        summary.TotalInputTokens.Should().Be((long)(call.Response?.Usage?.InputTokenCount ?? 0));
        series.Sum(p => p.TraceCount).Should().Be(1);
    }

    [TestMethod]
    public async Task GetAgentWindow_NoMatches_ReturnsEmptySeriesAndZeroSummary()
    {
        IServiceProvider services = GetServices();
        var reader = services.GetRequiredService<IAgentCallStatsReader>();

        var (series, summary) = await reader.GetAgentWindowAsync(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow,
            StatisticsBucket.Daily,
            CancellationToken);

        series.Should().BeEmpty();
        summary.TotalTraces.Should().Be(0);
    }

    private static readonly DateTimeOffset WindowFrom = DateTimeOffset.UtcNow.AddHours(-1);
    private static readonly DateTimeOffset WindowTo = DateTimeOffset.UtcNow.AddHours(1);

    [TestMethod]
    public async Task GetAgentDistributions_EmptyDb_ReturnsEmptyDistributions()
    {
        IServiceProvider services = GetServices();
        var reader = services.GetRequiredService<IAgentCallStatsReader>();

        var dist = await reader.GetAgentDistributionsAsync(Guid.NewGuid(), WindowFrom, WindowTo, CancellationToken);

        dist.InputTokensPerCall.SampleCount.Should().Be(0);
        dist.CostPerConversationEur.SampleCount.Should().Be(0);
        dist.CacheHitRatePerConversation.SampleCount.Should().Be(0);
        dist.ToolCallsPerConversation.SampleCount.Should().Be(0);
    }

    [TestMethod]
    public async Task GetAgentDistributions_PerCallMetrics_ComputeMeanAndSampleStdDev()
    {
        IServiceProvider services = GetServices();
        var reader = services.GetRequiredService<IAgentCallStatsReader>();
        var agent = await services.GetRequiredService<IDomainEntityGenerator<IAgent>>().CreateAsync(CancellationToken);
        var endpoint = await services.GetRequiredService<IDomainEntityGenerator<IModelEndpoint>>().GetOrCreateAsync(CancellationToken);
        AssistantMessage sample = (await services.GetRequiredService<IDomainObjectGenerator<ICompletion>>().CreateAsync(CancellationToken)).Response;

        // inputs {100,200,300} → mean 200, sample-std 100; outputs {10,20,30} → mean 20, std 10;
        // latencies {50,100,150} → mean 100, std 50. Each call is its own (null) conversation.
        var cells = new[] { (100UL, 10UL, 50d), (200UL, 20UL, 100d), (300UL, 30UL, 150d) };
        foreach (var (input, output, latency) in cells)
        {
            await SeedCallAsync(services, agent, endpoint, sample, conversationId: null,
                new TokenUsage(input, output, 0), latency, toolCount: 0, HttpStatusCode.OK);
        }

        var dist = await reader.GetAgentDistributionsAsync(agent.Id, WindowFrom, WindowTo, CancellationToken);

        dist.InputTokensPerCall.SampleCount.Should().Be(3);
        dist.InputTokensPerCall.Mean.Should().BeApproximately(200d, 1e-6);
        dist.InputTokensPerCall.StdDev.Should().BeApproximately(100d, 1e-6);
        dist.OutputTokensPerCall.Mean.Should().BeApproximately(20d, 1e-6);
        dist.OutputTokensPerCall.StdDev.Should().BeApproximately(10d, 1e-6);
        dist.LatencyMsPerCall.Mean.Should().BeApproximately(100d, 1e-6);
        dist.LatencyMsPerCall.StdDev.Should().BeApproximately(50d, 1e-6);
    }

    [TestMethod]
    public async Task GetAgentDistributions_Histogram_BinsCoverAllSamplesWithMinMax()
    {
        IServiceProvider services = GetServices();
        var reader = services.GetRequiredService<IAgentCallStatsReader>();
        var agent = await services.GetRequiredService<IDomainEntityGenerator<IAgent>>().CreateAsync(CancellationToken);
        var endpoint = await services.GetRequiredService<IDomainEntityGenerator<IModelEndpoint>>().GetOrCreateAsync(CancellationToken);
        AssistantMessage sample = (await services.GetRequiredService<IDomainObjectGenerator<ICompletion>>().CreateAsync(CancellationToken)).Response;

        foreach (ulong input in new ulong[] { 100, 200, 300 })
        {
            await SeedCallAsync(services, agent, endpoint, sample, conversationId: null,
                new TokenUsage(input, 10, 0), latencyMs: 10, toolCount: 0, HttpStatusCode.OK);
        }

        var h = (await reader.GetAgentDistributionsAsync(agent.Id, WindowFrom, WindowTo, CancellationToken)).InputTokensPerCall;

        h.Min.Should().Be(100d);
        h.Max.Should().Be(300d);
        h.Histogram.Should().HaveCount(3);                       // distinct values → one bar each
        h.Histogram.Sum(b => b.Count).Should().Be(3);            // every sample lands in exactly one bin
    }

    [TestMethod]
    public async Task GetAgentDistributions_Histogram_IdenticalSamples_CollapseToSingleBin()
    {
        IServiceProvider services = GetServices();
        var reader = services.GetRequiredService<IAgentCallStatsReader>();
        var agent = await services.GetRequiredService<IDomainEntityGenerator<IAgent>>().CreateAsync(CancellationToken);
        var endpoint = await services.GetRequiredService<IDomainEntityGenerator<IModelEndpoint>>().GetOrCreateAsync(CancellationToken);
        AssistantMessage sample = (await services.GetRequiredService<IDomainObjectGenerator<ICompletion>>().CreateAsync(CancellationToken)).Response;

        await SeedCallAsync(services, agent, endpoint, sample, conversationId: null,
            new TokenUsage(500, 10, 0), latencyMs: 10, toolCount: 0, HttpStatusCode.OK);
        await SeedCallAsync(services, agent, endpoint, sample, conversationId: null,
            new TokenUsage(500, 10, 0), latencyMs: 10, toolCount: 0, HttpStatusCode.OK);

        var h = (await reader.GetAgentDistributionsAsync(agent.Id, WindowFrom, WindowTo, CancellationToken)).InputTokensPerCall;

        h.Histogram.Should().ContainSingle().Which.Count.Should().Be(2);
    }

    [TestMethod]
    public async Task GetAgentDistributions_SingleCall_StdDevIsZero()
    {
        IServiceProvider services = GetServices();
        var reader = services.GetRequiredService<IAgentCallStatsReader>();
        var agent = await services.GetRequiredService<IDomainEntityGenerator<IAgent>>().CreateAsync(CancellationToken);
        var endpoint = await services.GetRequiredService<IDomainEntityGenerator<IModelEndpoint>>().GetOrCreateAsync(CancellationToken);
        AssistantMessage sample = (await services.GetRequiredService<IDomainObjectGenerator<ICompletion>>().CreateAsync(CancellationToken)).Response;

        await SeedCallAsync(services, agent, endpoint, sample, conversationId: null,
            new TokenUsage(123, 7, 0), latencyMs: 42, toolCount: 0, HttpStatusCode.OK);

        var dist = await reader.GetAgentDistributionsAsync(agent.Id, WindowFrom, WindowTo, CancellationToken);

        dist.InputTokensPerCall.SampleCount.Should().Be(1);
        dist.InputTokensPerCall.Mean.Should().BeApproximately(123d, 1e-6);
        dist.InputTokensPerCall.StdDev.Should().Be(0d);
    }

    [TestMethod]
    public async Task GetAgentDistributions_OnlyCountsSuccessfulCalls()
    {
        IServiceProvider services = GetServices();
        var reader = services.GetRequiredService<IAgentCallStatsReader>();
        var agent = await services.GetRequiredService<IDomainEntityGenerator<IAgent>>().CreateAsync(CancellationToken);
        var endpoint = await services.GetRequiredService<IDomainEntityGenerator<IModelEndpoint>>().GetOrCreateAsync(CancellationToken);
        AssistantMessage sample = (await services.GetRequiredService<IDomainObjectGenerator<ICompletion>>().CreateAsync(CancellationToken)).Response;

        await SeedCallAsync(services, agent, endpoint, sample, conversationId: null,
            new TokenUsage(100, 50, 0), latencyMs: 10, toolCount: 0, HttpStatusCode.OK);
        await SeedCallAsync(services, agent, endpoint, sample, conversationId: null,
            new TokenUsage(9999, 9999, 0), latencyMs: 999, toolCount: 0, HttpStatusCode.InternalServerError);

        var dist = await reader.GetAgentDistributionsAsync(agent.Id, WindowFrom, WindowTo, CancellationToken);

        dist.InputTokensPerCall.SampleCount.Should().Be(1);
        dist.InputTokensPerCall.Mean.Should().BeApproximately(100d, 1e-6);
    }

    [TestMethod]
    public async Task GetAgentDistributions_CacheHitRate_ExcludesFirstTurnAndSingletons()
    {
        IServiceProvider services = GetServices();
        var reader = services.GetRequiredService<IAgentCallStatsReader>();
        var agent = await services.GetRequiredService<IDomainEntityGenerator<IAgent>>().CreateAsync(CancellationToken);
        var endpoint = await services.GetRequiredService<IDomainEntityGenerator<IModelEndpoint>>().GetOrCreateAsync(CancellationToken);
        AssistantMessage sample = (await services.GetRequiredService<IDomainObjectGenerator<ICompletion>>().CreateAsync(CancellationToken)).Response;

        Guid conv = Guid.NewGuid();
        // Turn 1 (seeded first → earliest): fully cached, must be EXCLUDED.
        await SeedCallAsync(services, agent, endpoint, sample, conv,
            new TokenUsage(1000, 100, 1000), latencyMs: 10, toolCount: 0, HttpStatusCode.OK);
        // Turn 2: 400/1000 = 0.4 cache hit — the only sampled turn for this conversation.
        await SeedCallAsync(services, agent, endpoint, sample, conv,
            new TokenUsage(1000, 100, 400), latencyMs: 10, toolCount: 0, HttpStatusCode.OK);
        // A single-turn conversation contributes no cache sample.
        await SeedCallAsync(services, agent, endpoint, sample, conversationId: null,
            new TokenUsage(500, 50, 500), latencyMs: 10, toolCount: 0, HttpStatusCode.OK);

        var dist = await reader.GetAgentDistributionsAsync(agent.Id, WindowFrom, WindowTo, CancellationToken);

        dist.CacheHitRatePerConversation.SampleCount.Should().Be(1);
        dist.CacheHitRatePerConversation.Mean.Should().BeApproximately(0.4d, 1e-9);
    }

    [TestMethod]
    public async Task GetAgentDistributions_ToolCallsPerConversation_SumsTurnsAndCountsSingleton()
    {
        IServiceProvider services = GetServices();
        var reader = services.GetRequiredService<IAgentCallStatsReader>();
        var agent = await services.GetRequiredService<IDomainEntityGenerator<IAgent>>().CreateAsync(CancellationToken);
        var endpoint = await services.GetRequiredService<IDomainEntityGenerator<IModelEndpoint>>().GetOrCreateAsync(CancellationToken);
        AssistantMessage sample = (await services.GetRequiredService<IDomainObjectGenerator<ICompletion>>().CreateAsync(CancellationToken)).Response;

        Guid conv = Guid.NewGuid();
        await SeedCallAsync(services, agent, endpoint, sample, conv,
            new TokenUsage(100, 50, 0), latencyMs: 10, toolCount: 1, HttpStatusCode.OK);
        await SeedCallAsync(services, agent, endpoint, sample, conv,
            new TokenUsage(100, 50, 0), latencyMs: 10, toolCount: 2, HttpStatusCode.OK);
        await SeedCallAsync(services, agent, endpoint, sample, conversationId: null,
            new TokenUsage(100, 50, 0), latencyMs: 10, toolCount: 0, HttpStatusCode.OK);

        var dist = await reader.GetAgentDistributionsAsync(agent.Id, WindowFrom, WindowTo, CancellationToken);

        // Per-conversation tool counts {3 (1+2), 0 (singleton)} → mean 1.5 over two samples.
        dist.ToolCallsPerConversation.SampleCount.Should().Be(2);
        dist.ToolCallsPerConversation.Mean.Should().BeApproximately(1.5d, 1e-9);
    }

    [TestMethod]
    public async Task GetAgentDistributions_CostPerConversation_SumsAcrossEndpointsAndTreatsNullConvAsSingleton()
    {
        IServiceProvider services = GetServices();
        var reader = services.GetRequiredService<IAgentCallStatsReader>();
        var agent = await services.GetRequiredService<IDomainEntityGenerator<IAgent>>().CreateAsync(CancellationToken);
        AssistantMessage sample = (await services.GetRequiredService<IDomainObjectGenerator<ICompletion>>().CreateAsync(CancellationToken)).Response;

        IModelEndpoint endpointX = await CreatePricedEndpointAsync(services, inputCost: 2.5m, outputCost: 10m, cachedCost: 1m);
        IModelEndpoint endpointY = await CreatePricedEndpointAsync(services, inputCost: 5m, outputCost: 20m, cachedCost: 2m);

        // Conversation A spans two endpoints; conversation B is a single null-conversation call.
        Guid convA = Guid.NewGuid();
        var usageX = new TokenUsage(1000, 500, 0);
        var usageY = new TokenUsage(1000, 500, 0);
        var usageB = new TokenUsage(2000, 1000, 0);
        await SeedCallAsync(services, agent, endpointX, sample, convA, usageX, latencyMs: 10, toolCount: 0, HttpStatusCode.OK);
        await SeedCallAsync(services, agent, endpointY, sample, convA, usageY, latencyMs: 10, toolCount: 0, HttpStatusCode.OK);
        await SeedCallAsync(services, agent, endpointX, sample, conversationId: null, usageB, latencyMs: 10, toolCount: 0, HttpStatusCode.OK);

        var dist = await reader.GetAgentDistributionsAsync(agent.Id, WindowFrom, WindowTo, CancellationToken);

        decimal costA = (endpointX.CalculateCost(usageX) ?? 0m) + (endpointY.CalculateCost(usageY) ?? 0m);
        decimal costB = endpointX.CalculateCost(usageB) ?? 0m;
        double expectedMean = (double)(costA + costB) / 2d;

        dist.CostPerConversationEur.SampleCount.Should().Be(2);
        dist.CostPerConversationEur.Mean.Should().BeApproximately(expectedMean, 1e-9);
    }

    [TestMethod]
    public async Task GetSummary_NullLatencyCalls_DoNotBiasAverageLatency()
    {
        IServiceProvider services = GetServices();
        var reader = services.GetRequiredService<IAgentCallStatsReader>();
        var agent = await services.GetRequiredService<IDomainEntityGenerator<IAgent>>().CreateAsync(CancellationToken);
        var endpoint = await services.GetRequiredService<IDomainEntityGenerator<IModelEndpoint>>().GetOrCreateAsync(CancellationToken);
        AssistantMessage sample = (await services.GetRequiredService<IDomainObjectGenerator<ICompletion>>().CreateAsync(CancellationToken)).Response;

        await SeedCallAsync(services, agent, endpoint, sample, conversationId: null,
            new TokenUsage(100, 50, 0), latencyMs: 100, toolCount: 0, HttpStatusCode.OK);
        await SeedCallAsync(services, agent, endpoint, sample, conversationId: null,
            new TokenUsage(100, 50, 0), latencyMs: 200, toolCount: 0, HttpStatusCode.OK);
        await SeedNoLatencyCallAsync(services, agent, endpoint);

        var summary = await reader.GetSummaryAsync(new StatisticsFilter(), CancellationToken);

        // The latency-less (failed) call must not be averaged in as 0ms: (100+200)/2, not (100+200+0)/3.
        summary.TotalCalls.Should().Be(3);
        summary.AvgLatencyMs.Should().Be(150d);
    }

    [TestMethod]
    public async Task GetSummary_OnlyNullLatencyCalls_AverageLatencyIsZero()
    {
        IServiceProvider services = GetServices();
        var reader = services.GetRequiredService<IAgentCallStatsReader>();
        var agent = await services.GetRequiredService<IDomainEntityGenerator<IAgent>>().CreateAsync(CancellationToken);
        var endpoint = await services.GetRequiredService<IDomainEntityGenerator<IModelEndpoint>>().GetOrCreateAsync(CancellationToken);

        await SeedNoLatencyCallAsync(services, agent, endpoint);

        var summary = await reader.GetSummaryAsync(new StatisticsFilter(), CancellationToken);

        summary.TotalCalls.Should().Be(1);
        summary.AvgLatencyMs.Should().Be(0d);
    }

    [TestMethod]
    public async Task GetModelBreakdown_NullLatencyCalls_DoNotBiasAverageDuration()
    {
        IServiceProvider services = GetServices();
        var reader = services.GetRequiredService<IAgentCallStatsReader>();
        var agent = await services.GetRequiredService<IDomainEntityGenerator<IAgent>>().CreateAsync(CancellationToken);
        var endpoint = await services.GetRequiredService<IDomainEntityGenerator<IModelEndpoint>>().GetOrCreateAsync(CancellationToken);
        AssistantMessage sample = (await services.GetRequiredService<IDomainObjectGenerator<ICompletion>>().CreateAsync(CancellationToken)).Response;

        await SeedCallAsync(services, agent, endpoint, sample, conversationId: null,
            new TokenUsage(100, 50, 0), latencyMs: 300, toolCount: 0, HttpStatusCode.OK);
        await SeedNoLatencyCallAsync(services, agent, endpoint);

        var rows = await reader.GetModelBreakdownAsync(new StatisticsFilter(), CancellationToken);

        var stat = rows.Single(r => r.EndpointId == endpoint.Id);
        stat.CallCount.Should().Be(2);
        stat.AvgDurationMs.Should().Be(300d);
    }

    /// <summary>
    /// Seeds a failed call with no response — its stored <c>LatencyMs</c> is <c>null</c>, the shape
    /// the null-aware latency averages must ignore.
    /// </summary>
    private async Task SeedNoLatencyCallAsync(IServiceProvider services, IAgent agent, IModelEndpoint endpoint)
    {
        var conversationGen = services.GetRequiredService<IDomainObjectGenerator<Conversation>>();
        var createCall = services.GetRequiredService<IAgentCall.CreateNew>();
        var callRepo = services.GetRequiredService<IRepository<IAgentCall>>();

        var request = await conversationGen.CreateAsync(CancellationToken);
        var call = createCall(agent, agent.CurrentVersion, endpoint, request, response: null,
            httpStatus: HttpStatusCode.BadGateway, errorMessage: "upstream timeout");
        await callRepo.AddAsync(call, CancellationToken);
    }

    private async Task<IModelEndpoint> CreatePricedEndpointAsync(
        IServiceProvider services, decimal inputCost, decimal outputCost, decimal cachedCost)
    {
        var seed = await services.GetRequiredService<IDomainObjectGenerator<IModelEndpoint>>().CreateAsync(CancellationToken);
        var createEndpoint = services.GetRequiredService<IModelEndpoint.CreateNew>();
        var endpointRepo = services.GetRequiredService<IRepository<IModelEndpoint>>();
        return await endpointRepo.AddAsync(
            createEndpoint(seed.Model, seed.Provider, inputTokenCost: inputCost, outputTokenCost: outputCost, cachedInputTokenCost: cachedCost),
            CancellationToken);
    }

    [TestMethod]
    public async Task GetCostByApiKey_GroupsPerKey_AndKeepsUnattributedAsItsOwnRow()
    {
        IServiceProvider services = GetServices();
        var reader = services.GetRequiredService<IAgentCallStatsReader>();

        var endpointSeedGen = services.GetRequiredService<IDomainObjectGenerator<IModelEndpoint>>();
        var seed = await endpointSeedGen.CreateAsync(CancellationToken);
        var createEndpoint = services.GetRequiredService<IModelEndpoint.CreateNew>();
        var endpointRepo = services.GetRequiredService<IRepository<IModelEndpoint>>();
        IModelEndpoint endpoint = await endpointRepo.AddAsync(
            createEndpoint(seed.Model, seed.Provider, inputTokenCost: 2.5m, outputTokenCost: 10m, cachedInputTokenCost: 1m),
            CancellationToken);

        var agent = await services.GetRequiredService<IDomainEntityGenerator<IAgent>>().CreateAsync(CancellationToken);
        var conversationGen = services.GetRequiredService<IDomainObjectGenerator<Conversation>>();
        var completionGen = services.GetRequiredService<IDomainObjectGenerator<ICompletion>>();
        var sampleResponse = (await completionGen.CreateAsync(CancellationToken)).Response;
        var createCompletion = services.GetRequiredService<ICompletion.Create>();
        var createCall = services.GetRequiredService<IAgentCall.CreateNew>();
        var callRepo = services.GetRequiredService<IRepository<IAgentCall>>();

        var keyA = Guid.NewGuid();
        var keyB = Guid.NewGuid();
        var usage = new TokenUsage(1000, 500, 0);

        // Two calls on key A, one on key B, one unattributed (the upstream-key path).
        foreach (Guid? attribution in new Guid?[] { keyA, keyA, keyB, null })
        {
            var response = createCompletion(sampleResponse, usage, TimeSpan.FromMilliseconds(10));
            var request = await conversationGen.CreateAsync(CancellationToken);
            await callRepo.AddAsync(
                createCall(agent, agent.CurrentVersion, endpoint, request, response, apiKeyId: attribution),
                CancellationToken);
        }

        var rows = await reader.GetCostByApiKeyAsync(new StatisticsFilter(), CancellationToken);

        decimal? unitCost = endpoint.CalculateCost(usage);
        unitCost.Should().NotBeNull();
        decimal oneCall = unitCost.GetValueOrDefault();

        rows.Should().HaveCount(3);
        rows.Single(r => r.ApiKeyId == keyA).CostEur.Should().Be(oneCall * 2);
        rows.Single(r => r.ApiKeyId == keyB).CostEur.Should().Be(oneCall);

        // The unattributed remainder is a real row, not a dropped one — otherwise the per-key
        // figures would silently fail to sum to the project total.
        rows.Single(r => r.ApiKeyId == null).CostEur.Should().Be(oneCall);
        rows.Sum(r => r.CostEur).Should().Be(oneCall * 4);
    }

    private async Task<IAgentCall> SeedCallAsync(
        IServiceProvider services, IAgent agent, IModelEndpoint endpoint, AssistantMessage sample,
        Guid? conversationId, TokenUsage usage, double latencyMs, int toolCount, HttpStatusCode status)
    {
        var createCompletion = services.GetRequiredService<ICompletion.Create>();
        var conversationGen = services.GetRequiredService<IDomainObjectGenerator<Conversation>>();
        var createCall = services.GetRequiredService<IAgentCall.CreateNew>();
        var callRepo = services.GetRequiredService<IRepository<IAgentCall>>();

        var toolRequests = Enumerable.Range(0, toolCount).Select(i => new ToolRequest($"tr{i}", "tool", "{}")).ToList();
        var response = createCompletion(new AssistantMessage(sample.Contents, toolRequests), usage, TimeSpan.FromMilliseconds(latencyMs));
        var request = await conversationGen.CreateAsync(CancellationToken);
        var call = createCall(agent, agent.CurrentVersion, endpoint, request, response, status, conversationId: conversationId);
        return await callRepo.AddAsync(call, CancellationToken);
    }
}
