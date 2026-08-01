using Proxytrace.Domain.Statistics;
using AwesomeAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Proxytrace.Api.Configuration;
using Proxytrace.Api.Controllers;
using Proxytrace.Api.Dto.AgentCalls;
using Proxytrace.Api.Dto.Agents;
using Proxytrace.Api.Dto.Tools;
using Proxytrace.Application.Statistics;
using Proxytrace.Domain.Agent;
using Proxytrace.Testing;

namespace Proxytrace.Api.Tests;

[TestClass]
public sealed class StatisticsControllerTests : BaseTest<Module>
{
    [TestMethod]
    public async Task GetDashboardView_DelegatesToService_AndMapsDto()
    {
        var dashboard = Substitute.For<IDashboardStatistics>();
        var agentId = Guid.NewGuid();
        var endpointId = Guid.NewGuid();
        var date = DateOnly.FromDateTime(DateTime.UtcNow);
        dashboard.GetDashboardViewAsync(Arg.Any<StatisticsFilter>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new DashboardView(
                Summary: new StatisticsSummary(TotalCalls: 42, TotalInputTokens: 100, TotalOutputTokens: 200, TotalCachedInputTokens: 40, AvgLatencyMs: 12.5, OverallPassRate: 0.95),
                LiveTelemetry: new LiveTelemetry(TracesPerMinute: 1, TokensPerSecond: 2, QueueDepth: 3, ErrorRate: 0.1, P95Ms: 55),
                Trends: new DashboardTrends([1], [2], [3], [4]),
                AgentBreakdown: [new AgentBreakdownStat(agentId, 7)],
                Latency: [new LatencyStat(endpointId, 10, 20, 30, 1, 100, 50)],
                ModelBreakdown: [new ModelBreakdownStat(endpointId, "gpt-4o", CallCount: 3, TotalInputTokens: null, TotalOutputTokens: null, TotalCachedInputTokens: null, AvgDurationMs: null)],
                TokenUsage: [new TokenUsageStat(date.ToDateTime(TimeOnly.MinValue), endpointId, InputTokens: 10, OutputTokens: 20, CachedInputTokens: 4)],
                TokenUsageByAgent: [new AgentTokenUsageStat(date.ToDateTime(TimeOnly.MinValue), agentId, InputTokens: 5, OutputTokens: 6, CachedInputTokens: 2)],
                TokenBucket: StatisticsBucket.Hourly,
                RecentTraces: [],
                Agents: [],
                AgentLastCallTimes: new Dictionary<Guid, DateTimeOffset>(),
                Pulse: new int[60]));

        var controller = ResolveController(dashboard);

        var result = await controller.GetDashboardView(cancellationToken: CancellationToken);
        var dto = result.Value;

        dto.Should().NotBeNull();
        dto.Summary.TotalCalls.Should().Be(42);
        dto.LiveTelemetry.P95Ms.Should().Be(55);
        dto.AgentBreakdown.Should().ContainSingle();
        dto.Latency[0].P95Ms.Should().Be(20);
        dto.ModelBreakdown[0].TotalInputTokens.Should().Be(0); // null coerced
        dto.TokenUsage[0].InputTokens.Should().Be(10);
        dto.TokenUsageByAgent[0].OutputTokens.Should().Be(6);
        dto.TokenBucket.Should().Be("hourly");
        dto.RecentTraces.Should().BeEmpty();
        dto.Agents.Should().BeEmpty();
    }

    [TestMethod]
    public async Task GetDashboardView_PassesFilterAndLimits()
    {
        var dashboard = Substitute.For<IDashboardStatistics>();
        dashboard.GetDashboardViewAsync(Arg.Any<StatisticsFilter>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new DashboardView(
                new StatisticsSummary(0, 0, 0, 0, 0, null),
                new LiveTelemetry(0, 0, 0, 0, 0),
                new DashboardTrends([], [], [], []),
                [], [], [], [], [],
                StatisticsBucket.Daily,
                [], [], new Dictionary<Guid, DateTimeOffset>(), new int[60]));
        var controller = ResolveController(dashboard);
        var projectId = Guid.NewGuid();
        var from = DateTimeOffset.UtcNow.AddDays(-1);

        await controller.GetDashboardView(from, null, projectId, recentTraceCount: 3, agentLimit: 4, cancellationToken: CancellationToken);

        await dashboard.Received(1).GetDashboardViewAsync(
            Arg.Is<StatisticsFilter>(f => f != null && f.From == from && f.ProjectId == projectId),
            3, 4, Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task GetAgentOverview_MissingFromTo_ReturnsBadRequest()
    {
        var controller = ResolveController(agentStatistics: Substitute.For<IAgentStatistics>());

        var result = await controller.GetAgentOverview(Guid.NewGuid(), from: null, to: null, cancellationToken: CancellationToken);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [TestMethod]
    public async Task GetAgentOverview_WithFromTo_MapsDto()
    {
        var agentStatistics = Substitute.For<IAgentStatistics>();
        var bucketStart = DateTimeOffset.UtcNow;
        var suiteId = Guid.NewGuid();
        agentStatistics.GetAgentOverviewAsync(Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<StatisticsBucket>(), Arg.Any<CancellationToken>())
            .Returns(new AgentOverviewStat(
                Summary: new AgentTimeSummary(10, 100, 200, 40, 5.5m, 12.5),
                TimeSeries: [new AgentTimeSeriesPoint(bucketStart, 1, 10, 20, 4, 0.5m, 50)],
                PassRateTrend: [new AgentPassRatePoint(bucketStart, 4, 5)],
                SuitePassRates: [new AgentSuitePassRate(suiteId, "Suite A", bucketStart, 3, 3)],
                Counts: new AgentEntityCounts(SuiteCount: 2, TestCaseCount: 6, OpenProposalCount: 1, TotalProposalCount: 4)));
        var controller = ResolveController(agentStatistics: agentStatistics);

        var result = await controller.GetAgentOverview(Guid.NewGuid(), from: bucketStart, to: bucketStart.AddDays(1), bucket: StatisticsBucket.Daily, cancellationToken: CancellationToken);

        var dto = result.Value;
        dto.Should().NotBeNull();
        dto.Summary.TotalTraces.Should().Be(10);
        dto.TimeSeries.Should().ContainSingle();
        dto.PassRateTrend.Should().ContainSingle();
        dto.SuitePassRates.Should().ContainSingle();
        dto.SuitePassRates[0].SuiteId.Should().Be(suiteId);
        dto.Counts.SuiteCount.Should().Be(2);
        dto.Counts.TestCaseCount.Should().Be(6);
        dto.Counts.OpenProposalCount.Should().Be(1);
        dto.Counts.TotalProposalCount.Should().Be(4);
    }

    // ── cost overview ─────────────────────────────────────────────────────────

    [TestMethod]
    public async Task GetCostOverview_MissingFromTo_ReturnsBadRequest()
    {
        var controller = ResolveController();

        var result = await controller.GetCostOverview(
            projectId: Guid.NewGuid(), from: null, to: null, cancellationToken: CancellationToken);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [TestMethod]
    public async Task GetCostOverview_FromNotBeforeTo_ReturnsBadRequest()
    {
        var controller = ResolveController();
        var now = DateTimeOffset.UtcNow;

        var result = await controller.GetCostOverview(
            projectId: Guid.NewGuid(), from: now, to: now.AddDays(-1), cancellationToken: CancellationToken);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [TestMethod]
    public async Task GetCostOverview_WhenProjectInaccessible_ReturnsNotFound()
    {
        var controller = ResolveController(accessGuard: DenyingGuard());
        var now = DateTimeOffset.UtcNow;

        // Cost figures are always project-scoped: there is no cross-tenant aggregate to fall back to.
        var result = await controller.GetCostOverview(
            projectId: Guid.NewGuid(), from: now.AddDays(-30), to: now, cancellationToken: CancellationToken);

        result.Result.Should().BeOfType<NotFoundResult>();
    }

    [TestMethod]
    public async Task GetCostOverview_WithWindow_MapsEveryPartOfThePayload()
    {
        var projectId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var bucketStart = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);

        var costStatistics = Substitute.For<Proxytrace.Application.CostControl.ICostStatistics>();
        costStatistics.GetCostOverviewAsync(
                Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(),
                Arg.Any<StatisticsBucket>(), Arg.Any<CancellationToken>())
            .Returns(new Proxytrace.Application.CostControl.CostOverview(
                MonthToDateSpendEur: 12.5m,
                PreviousMonthSpendEur: 30m,
                Series: [new AgentCostPoint(bucketStart, agentId, 12.5m)],
                AgentTotals: [new Proxytrace.Application.CostControl.AgentCostTotal(agentId, "Support bot", 12.5m)],
                ApiKeySeries: [],
                ApiKeyTotals: [],
                HasUnpricedEndpoints: true,
                // The service coarsened the requested 5-minute bucket to fit the window; the
                // response must report what it actually aggregated at.
                Bucket: StatisticsBucket.Daily));

        var controller = ResolveController(costStatistics: costStatistics);
        var now = DateTimeOffset.UtcNow;

        var result = await controller.GetCostOverview(
            projectId, from: now.AddDays(-30), to: now,
            bucket: StatisticsBucket.FiveMinutes, cancellationToken: CancellationToken);

        Proxytrace.Api.Dto.Costs.CostOverviewDto? maybeDto = result.Value;
        ArgumentNullException.ThrowIfNull(maybeDto);
        Proxytrace.Api.Dto.Costs.CostOverviewDto dto = maybeDto;
        dto.MonthToDateSpendEur.Should().Be(12.5m);
        dto.PreviousMonthSpendEur.Should().Be(30m);
        dto.Series.Should().ContainSingle().Which.AgentId.Should().Be(agentId);
        dto.AgentTotals.Should().ContainSingle().Which.AgentName.Should().Be("Support bot");
        dto.HasUnpricedEndpoints.Should().BeTrue();
        // The effective bucket, not the requested one — the client densifies against this.
        dto.Bucket.Should().Be("daily");

        await costStatistics.Received(1).GetCostOverviewAsync(
            projectId, Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(),
            StatisticsBucket.FiveMinutes, Arg.Any<CancellationToken>());
    }

    private StatisticsController ResolveController(
        IDashboardStatistics? dashboard = null,
        IAgentStatistics? agentStatistics = null,
        IAgentRepository? agents = null,
        Proxytrace.Api.Auth.IProjectAccessGuard? accessGuard = null,
        Proxytrace.Application.CostControl.ICostStatistics? costStatistics = null)
    {
        var toolDtoMapper = new ToolDtoMapper();
        var agentRepo = agents ?? Substitute.For<IAgentRepository>();
        if (agents is null)
        {
            // Default: every agent resolves to some project so the access guard governs the outcome.
            agentRepo.GetProjectIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                .Returns(Guid.NewGuid());
        }
        return new StatisticsController(
            dashboard ?? Substitute.For<IDashboardStatistics>(),
            agentStatistics ?? Substitute.For<IAgentStatistics>(),
            costStatistics ?? Substitute.For<Proxytrace.Application.CostControl.ICostStatistics>(),
            agentRepo,
            new AgentCallDtoMapper(toolDtoMapper),
            new AgentDtoMapper(toolDtoMapper),
            new StatisticsOptions(),
            accessGuard ?? AdminGuard());
    }

    // Admin-equivalent: may access any project, and GetAccessibleProjectIdsAsync returns null
    // (the "admin sees everything" signal), so the unscoped global dashboard is permitted.
    private static Proxytrace.Api.Auth.IProjectAccessGuard AdminGuard()
    {
        var guard = Substitute.For<Proxytrace.Api.Auth.IProjectAccessGuard>();
        guard.CanAccessProjectAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);
        guard.GetAccessibleProjectIdsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<Guid>?>(null));
        return guard;
    }

    // Non-admin member of nothing: denied every project, empty (non-null) accessible set.
    private static Proxytrace.Api.Auth.IProjectAccessGuard DenyingGuard()
    {
        var guard = Substitute.For<Proxytrace.Api.Auth.IProjectAccessGuard>();
        guard.CanAccessProjectAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(false);
        guard.GetAccessibleProjectIdsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<Guid>?>([]));
        return guard;
    }

    [TestMethod]
    public async Task GetDashboardView_WhenProjectIdSuppliedButInaccessible_ReturnsNotFound()
    {
        var controller = ResolveController(accessGuard: DenyingGuard());

        var result = await controller.GetDashboardView(projectId: Guid.NewGuid(), cancellationToken: CancellationToken);

        result.Result.Should().BeOfType<NotFoundResult>();
    }

    [TestMethod]
    public async Task GetDashboardView_AsNonAdminWithoutProjectId_ReturnsForbidden()
    {
        var controller = ResolveController(accessGuard: DenyingGuard());

        var result = await controller.GetDashboardView(cancellationToken: CancellationToken);

        // The unscoped global aggregate spans all tenants — refused to a non-admin.
        result.Result.Should().BeOfType<StatusCodeResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [TestMethod]
    public async Task GetAgentOverview_WhenAgentInaccessible_ReturnsNotFound()
    {
        var agents = Substitute.For<IAgentRepository>();
        agents.GetProjectIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(Guid.NewGuid());
        var controller = ResolveController(agents: agents, accessGuard: DenyingGuard());

        var now = DateTimeOffset.UtcNow;
        var result = await controller.GetAgentOverview(Guid.NewGuid(), from: now.AddDays(-1), to: now, cancellationToken: CancellationToken);

        result.Result.Should().BeOfType<NotFoundResult>();
    }

    [TestMethod]
    public async Task GetAgentDistributions_WhenAgentInaccessible_ReturnsNotFound()
    {
        var agents = Substitute.For<IAgentRepository>();
        agents.GetProjectIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(Guid.NewGuid());
        var controller = ResolveController(agents: agents, accessGuard: DenyingGuard());

        var now = DateTimeOffset.UtcNow;
        var result = await controller.GetAgentDistributions(Guid.NewGuid(), from: now.AddDays(-1), to: now, cancellationToken: CancellationToken);

        result.Result.Should().BeOfType<NotFoundResult>();
    }

    // ── anomaly timeline ───────────────────────────────────────────────────────

    [TestMethod]
    public async Task GetAnomalyTimeline_MissingFromTo_ReturnsBadRequest()
    {
        var controller = ResolveController();

        var result = await controller.GetAnomalyTimeline(from: null, to: null, cancellationToken: CancellationToken);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [TestMethod]
    public async Task GetAnomalyTimeline_FromNotBeforeTo_ReturnsBadRequest()
    {
        var controller = ResolveController();
        var now = DateTimeOffset.UtcNow;

        var result = await controller.GetAnomalyTimeline(from: now, to: now.AddDays(-1), cancellationToken: CancellationToken);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [TestMethod]
    public async Task GetAnomalyTimeline_WithWindow_MapsDtoAndPassesFilter()
    {
        var dashboard = Substitute.For<IDashboardStatistics>();
        var agentId = Guid.NewGuid();
        var bucketStart = DateTimeOffset.UtcNow;
        dashboard.GetAnomalyCountsByAgentAsync(Arg.Any<StatisticsFilter>(), Arg.Any<StatisticsBucket>(), Arg.Any<CancellationToken>())
            .Returns([new AgentAnomalyStat(bucketStart, agentId, StaticCount: 3, CustomCount: 1)]);
        var controller = ResolveController(dashboard);
        var from = bucketStart.AddDays(-7);
        var to = bucketStart.AddDays(1);

        var result = await controller.GetAnomalyTimeline(from: from, to: to, bucket: StatisticsBucket.Hourly, cancellationToken: CancellationToken);

        var dto = result.Value.Should().ContainSingle().Subject;
        dto.BucketStart.Should().Be(bucketStart);
        dto.AgentId.Should().Be(agentId);
        dto.StaticCount.Should().Be(3);
        dto.CustomCount.Should().Be(1);
        await dashboard.Received(1).GetAnomalyCountsByAgentAsync(
            Arg.Is<StatisticsFilter>(f => f != null && f.From == from && f.To == to),
            StatisticsBucket.Hourly, Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task GetAnomalyTimeline_WhenProjectInaccessible_ReturnsNotFound()
    {
        var controller = ResolveController(accessGuard: DenyingGuard());
        var now = DateTimeOffset.UtcNow;

        var result = await controller.GetAnomalyTimeline(
            from: now.AddDays(-1), to: now, projectId: Guid.NewGuid(), cancellationToken: CancellationToken);

        result.Result.Should().BeOfType<NotFoundResult>();
    }

    [TestMethod]
    public async Task GetAnomalyTimeline_WhenAgentInaccessible_ReturnsNotFound()
    {
        var agents = Substitute.For<IAgentRepository>();
        agents.GetProjectIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(Guid.NewGuid());
        var controller = ResolveController(agents: agents, accessGuard: DenyingGuard());
        var now = DateTimeOffset.UtcNow;

        var result = await controller.GetAnomalyTimeline(
            from: now.AddDays(-1), to: now, agentId: Guid.NewGuid(), cancellationToken: CancellationToken);

        result.Result.Should().BeOfType<NotFoundResult>();
    }

    [TestMethod]
    public async Task GetAnomalyTimeline_AsNonAdminWithoutScope_ReturnsForbidden()
    {
        var controller = ResolveController(accessGuard: DenyingGuard());
        var now = DateTimeOffset.UtcNow;

        var result = await controller.GetAnomalyTimeline(from: now.AddDays(-1), to: now, cancellationToken: CancellationToken);

        // The unscoped global series spans all tenants — refused to a non-admin, like the dashboard.
        result.Result.Should().BeOfType<StatusCodeResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }
}
