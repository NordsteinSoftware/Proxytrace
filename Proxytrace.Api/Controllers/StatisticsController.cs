using Proxytrace.Domain.Statistics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Proxytrace.Api.Auth;
using Proxytrace.Api.Configuration;
using Proxytrace.Api.Dto.AgentCalls;
using Proxytrace.Api.Dto.Agents;
using Proxytrace.Api.Dto.Costs;
using Proxytrace.Api.Dto.Statistics;
using Proxytrace.Application.CostControl;
using Proxytrace.Application.Statistics;
using Proxytrace.Domain.Agent;

namespace Proxytrace.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/statistics")]
public class StatisticsController : ControllerBase
{
    private readonly IDashboardStatistics dashboard;
    private readonly IAgentStatistics agentStatistics;
    private readonly ICostStatistics costStatistics;
    private readonly IAgentRepository agents;
    private readonly AgentCallDtoMapper agentCallDtoMapper;
    private readonly AgentDtoMapper agentDtoMapper;
    private readonly StatisticsOptions options;
    private readonly IProjectAccessGuard accessGuard;

    public StatisticsController(
        IDashboardStatistics dashboard,
        IAgentStatistics agentStatistics,
        ICostStatistics costStatistics,
        IAgentRepository agents,
        AgentCallDtoMapper agentCallDtoMapper,
        AgentDtoMapper agentDtoMapper,
        StatisticsOptions options,
        IProjectAccessGuard accessGuard)
    {
        this.dashboard = dashboard;
        this.agentStatistics = agentStatistics;
        this.costStatistics = costStatistics;
        this.agents = agents;
        this.agentCallDtoMapper = agentCallDtoMapper;
        this.agentDtoMapper = agentDtoMapper;
        this.options = options;
        this.accessGuard = accessGuard;
    }

    [HttpGet("dashboard")]
    public async Task<ActionResult<DashboardViewDto>> GetDashboardView(
        [FromQuery] DateTimeOffset? from = null,
        [FromQuery] DateTimeOffset? to = null,
        [FromQuery] Guid? projectId = null,
        [FromQuery] int? recentTraceCount = null,
        [FromQuery] int? agentLimit = null,
        [FromQuery] bool excludeSystemAgents = false,
        CancellationToken cancellationToken = default)
    {
        if (from is not null && to is not null && from.Value >= to.Value)
            return BadRequest("Query parameter 'from' must be before 'to'.");

        // Tenant scoping: a supplied projectId must be one the caller can access (hidden behind 404).
        // Omitting projectId yields a cross-tenant global aggregate, which only an admin may see — a
        // non-admin (non-null accessible set) is refused rather than served every tenant's data.
        if (projectId is { } requestedProjectId)
        {
            if (!await accessGuard.CanAccessProjectAsync(requestedProjectId, cancellationToken))
                return NotFound();
        }
        else if (await accessGuard.GetAccessibleProjectIdsAsync(cancellationToken) is not null)
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        var resolvedRecentTraceCount = Math.Clamp(
            recentTraceCount ?? options.DefaultRecentTraceCount, 1, options.MaxRecentTraceCount);
        var resolvedAgentLimit = Math.Clamp(
            agentLimit ?? options.DefaultAgentLimit, 1, options.MaxAgentLimit);
        var filter = new StatisticsFilter(from, to, projectId, ExcludeSystemAgents: excludeSystemAgents);
        DashboardView view = await dashboard.GetDashboardViewAsync(filter, resolvedRecentTraceCount, resolvedAgentLimit, cancellationToken);

        return new DashboardViewDto(
            Summary: new SummaryDto(view.Summary.TotalCalls, view.Summary.TotalInputTokens, view.Summary.TotalOutputTokens, view.Summary.TotalCachedInputTokens, view.Summary.AvgLatencyMs, view.Summary.OverallPassRate),
            LiveTelemetry: new LiveTelemetryDto(view.LiveTelemetry.TracesPerMinute, view.LiveTelemetry.TokensPerSecond, view.LiveTelemetry.QueueDepth, view.LiveTelemetry.ErrorRate, view.LiveTelemetry.P95Ms),
            Trends: new DashboardTrendsDto(view.Trends.Traces, view.Trends.LatencyMs, view.Trends.Throughput, view.Trends.PassRate),
            AgentBreakdown: view.AgentBreakdown.Select(r => new AgentBreakdownDto(r.AgentId, r.CallCount)).ToArray(),
            Latency: view.Latency.Select(r => new LatencyDto(r.EndpointId, r.P50Ms, r.P95Ms, r.P99Ms, r.MinMs, r.MaxMs, r.SampleCount)).ToArray(),
            ModelBreakdown: view.ModelBreakdown.Select(r => new ModelBreakdownDto(r.EndpointId, r.ModelName, r.CallCount, r.TotalInputTokens ?? 0, r.TotalOutputTokens ?? 0, r.TotalCachedInputTokens ?? 0, r.AvgDurationMs ?? 0)).ToArray(),
            TokenUsage: view.TokenUsage.Select(r => new TokenUsageDto(r.BucketStart, r.EndpointId, r.InputTokens ?? 0, r.OutputTokens ?? 0, r.CachedInputTokens ?? 0)).ToArray(),
            TokenUsageByAgent: view.TokenUsageByAgent.Select(r => new AgentTokenUsageDto(r.BucketStart, r.AgentId, r.InputTokens, r.OutputTokens, r.CachedInputTokens)).ToArray(),
            TokenBucket: view.TokenBucket switch
            {
                StatisticsBucket.FiveMinutes => "fiveMinutes",
                StatisticsBucket.Hourly => "hourly",
                _ => "daily",
            },
            RecentTraces: view.RecentTraces.Select(agentCallDtoMapper.ToListItemDto).ToArray(),
            Agents: view.Agents.Select(a => agentDtoMapper.ToListItemDto(a, view.AgentLastCallTimes.TryGetValue(a.Id, out var t) ? t : null)).ToArray(),
            Pulse: view.Pulse);
    }

    /// <summary>
    /// Bucketed per-agent anomaly counts for the anomaly dashboard timeline: flagged (outlier) calls
    /// per (bucket, agent), split into the statistical ingestion-time flags and the custom-detector
    /// flag (a call carrying both kinds counts in both).
    /// </summary>
    [HttpGet("anomalies/timeline")]
    public async Task<ActionResult<IReadOnlyList<AgentAnomalyStatDto>>> GetAnomalyTimeline(
        [FromQuery] DateTimeOffset? from = null,
        [FromQuery] DateTimeOffset? to = null,
        [FromQuery] StatisticsBucket bucket = StatisticsBucket.Daily,
        [FromQuery] Guid? agentId = null,
        [FromQuery] Guid? projectId = null,
        CancellationToken cancellationToken = default)
    {
        if (from is null || to is null)
            return BadRequest("Query parameters 'from' and 'to' are required.");
        if (from.Value >= to.Value)
            return BadRequest("Query parameter 'from' must be before 'to'.");

        // Tenant scoping mirrors GetDashboardView/GetAgentOverview: a supplied projectId or agentId
        // must be one the caller can access (hidden behind 404); the unscoped cross-tenant series is
        // admin-only.
        if (projectId is { } requestedProjectId && !await accessGuard.CanAccessProjectAsync(requestedProjectId, cancellationToken))
            return NotFound();
        if (agentId is { } requestedAgentId && !await CanAccessAgentAsync(requestedAgentId, cancellationToken))
            return NotFound();
        if (projectId is null && agentId is null
            && await accessGuard.GetAccessibleProjectIdsAsync(cancellationToken) is not null)
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        var filter = new StatisticsFilter(from, to, projectId, agentId);
        var rows = await dashboard.GetAnomalyCountsByAgentAsync(filter, bucket, cancellationToken);
        return rows
            .Select(r => new AgentAnomalyStatDto(r.BucketStart, r.AgentId, r.StaticCount, r.CustomCount))
            .ToArray();
    }

    /// <summary>
    /// The Costs page payload for one project: month-to-date and previous-month spend, the
    /// per-agent cost series and totals over the requested window, and the project's budgets joined
    /// with this month's breach state. Free for every project member — only *changing* a budget is
    /// licensed.
    /// </summary>
    [HttpGet("cost-overview")]
    public async Task<ActionResult<CostOverviewDto>> GetCostOverview(
        [FromQuery] Guid projectId,
        [FromQuery] DateTimeOffset? from = null,
        [FromQuery] DateTimeOffset? to = null,
        [FromQuery] StatisticsBucket bucket = StatisticsBucket.Daily,
        CancellationToken cancellationToken = default)
    {
        if (from is null || to is null)
            return BadRequest("Query parameters 'from' and 'to' are required.");
        if (from.Value >= to.Value)
            return BadRequest("Query parameter 'from' must be before 'to'.");

        // Cost figures are always project-scoped: unlike the dashboard there is no cross-tenant
        // aggregate to fall back to, so an inaccessible project is a 404 and nothing else.
        if (!await accessGuard.CanAccessProjectAsync(projectId, cancellationToken))
            return NotFound();

        CostOverview overview = await costStatistics.GetCostOverviewAsync(
            projectId, from.Value, to.Value, bucket, cancellationToken);

        return new CostOverviewDto(
            MonthToDateSpendEur: overview.MonthToDateSpendEur,
            PreviousMonthSpendEur: overview.PreviousMonthSpendEur,
            Series: overview.Series
                .Select(p => new AgentCostPointDto(p.BucketStart, p.AgentId, p.CostEur)).ToArray(),
            AgentTotals: overview.AgentTotals
                .Select(t => new AgentCostTotalDto(t.AgentId, t.AgentName, t.CostEur)).ToArray(),
            Budgets: overview.Budgets
                .Select(b => new CostBudgetStatusDto(
                    b.CostLimitId, b.AgentId, b.AgentName, b.SoftLimitEur, b.HardLimitEur,
                    b.Enabled, b.MonthToDateSpendEur, b.SoftBreached, b.HardBreached))
                .ToArray(),
            HasUnpricedEndpoints: overview.HasUnpricedEndpoints,
            Bucket: bucket switch
            {
                StatisticsBucket.FiveMinutes => "fiveMinutes",
                StatisticsBucket.Hourly => "hourly",
                _ => "daily",
            });
    }

    [HttpGet("agents/{agentId:guid}/overview")]
    public async Task<ActionResult<AgentOverviewDto>> GetAgentOverview(
        Guid agentId,
        [FromQuery] DateTimeOffset? from = null,
        [FromQuery] DateTimeOffset? to = null,
        [FromQuery] StatisticsBucket bucket = StatisticsBucket.Daily,
        CancellationToken cancellationToken = default)
    {
        if (from is null || to is null)
            return BadRequest("Query parameters 'from' and 'to' are required.");
        if (from.Value >= to.Value)
            return BadRequest("Query parameter 'from' must be before 'to'.");
        if (!await CanAccessAgentAsync(agentId, cancellationToken))
            return NotFound();

        var result = await agentStatistics.GetAgentOverviewAsync(agentId, from.Value, to.Value, bucket, cancellationToken);
        return new AgentOverviewDto(
            Summary: ToDto(result.Summary),
            TimeSeries: result.TimeSeries.Select(ToDto).ToArray(),
            PassRateTrend: result.PassRateTrend.Select(ToDto).ToArray(),
            SuitePassRates: result.SuitePassRates.Select(ToDto).ToArray(),
            Counts: ToDto(result.Counts));
    }

    [HttpGet("agents/{agentId:guid}/distributions")]
    public async Task<ActionResult<AgentDistributionsDto>> GetAgentDistributions(
        Guid agentId,
        [FromQuery] DateTimeOffset? from = null,
        [FromQuery] DateTimeOffset? to = null,
        CancellationToken cancellationToken = default)
    {
        if (from is null || to is null)
            return BadRequest("Query parameters 'from' and 'to' are required.");
        if (from.Value >= to.Value)
            return BadRequest("Query parameter 'from' must be before 'to'.");
        if (!await CanAccessAgentAsync(agentId, cancellationToken))
            return NotFound();

        AgentCallDistributions result = await agentStatistics.GetAgentDistributionsAsync(agentId, from.Value, to.Value, cancellationToken);
        return new AgentDistributionsDto(
            InputTokensPerCall: ToDto(result.InputTokensPerCall),
            OutputTokensPerCall: ToDto(result.OutputTokensPerCall),
            LatencyMsPerCall: ToDto(result.LatencyMsPerCall),
            CostPerConversationEur: ToDto(result.CostPerConversationEur),
            CacheHitRatePerConversation: ToDto(result.CacheHitRatePerConversation),
            ToolCallsPerConversation: ToDto(result.ToolCallsPerConversation));
    }

    // Agent-scoped statistics take a raw agentId. Resolve its project and hide it behind a 404 when
    // the caller is not a member (no existence oracle); a missing agent is indistinguishable.
    private async Task<bool> CanAccessAgentAsync(Guid agentId, CancellationToken cancellationToken)
    {
        var projectId = await agents.GetProjectIdAsync(agentId, cancellationToken);
        return projectId is not null && await accessGuard.CanAccessProjectAsync(projectId.Value, cancellationToken);
    }

    private static MetricDistributionDto ToDto(MetricDistribution d) =>
        new(d.Mean, d.StdDev, d.SampleCount, d.Min, d.Max,
            d.Histogram.Select(b => new HistogramBinDto(b.Start, b.End, b.Count)).ToArray());

    private static AgentTimeSummaryDto ToDto(AgentTimeSummary s) =>
        new(s.TotalTraces, s.TotalInputTokens, s.TotalOutputTokens, s.TotalCachedInputTokens, s.TotalCostEur, s.AvgLatencyMs);

    private static AgentTimeSeriesPointDto ToDto(AgentTimeSeriesPoint p) =>
        new(p.BucketStart, p.TraceCount, p.InputTokens, p.OutputTokens, p.CachedInputTokens, p.CostEur, p.AvgLatencyMs);

    private static AgentPassRatePointDto ToDto(AgentPassRatePoint p) =>
        new(p.BucketStart, p.Passed, p.TestCases);

    private static AgentSuitePassRateDto ToDto(AgentSuitePassRate s) =>
        new(s.SuiteId, s.SuiteName, s.LatestRunAt, s.Passed, s.TestCases);

    private static AgentEntityCountsDto ToDto(AgentEntityCounts c) =>
        new(c.SuiteCount, c.TestCaseCount, c.OpenProposalCount, c.TotalProposalCount);
}
