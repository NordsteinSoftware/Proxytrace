using Proxytrace.Domain.Statistics;
using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using Proxytrace.Api.Dto.Statistics;
using Proxytrace.Application.Statistics;
using Proxytrace.Domain.Agent;

namespace Proxytrace.Api.Mcp.Tools;

/// <summary>
/// Compact project dashboard returned by <see cref="StatsTools.GetDashboard"/>: headline totals plus a
/// per-model usage breakdown. Purpose-built for agents — the full REST dashboard payload is UI-shaped.
/// </summary>
internal sealed record McpDashboardDto(SummaryDto Summary, IReadOnlyList<ModelBreakdownDto> Models);

/// <summary>
/// MCP tools exposing usage statistics for the current project and its agents.
/// </summary>
[McpServerToolType]
internal sealed class StatsTools
{
    private const int RecentTraceCount = 5;
    private const int AgentLimit = 20;

    private readonly IMcpProjectAccessor project;
    private readonly IDashboardStatistics dashboard;
    private readonly IAgentStatistics agentStatistics;
    private readonly IAgentRepository agents;

    /// <summary>
    /// Initializes a new instance of the <see cref="StatsTools"/> class.
    /// </summary>
    public StatsTools(
        IMcpProjectAccessor project,
        IDashboardStatistics dashboard,
        IAgentStatistics agentStatistics,
        IAgentRepository agents)
    {
        this.project = project;
        this.dashboard = dashboard;
        this.agentStatistics = agentStatistics;
        this.agents = agents;
    }

    [McpServerTool(Name = "get_dashboard")]
    [Description("Get project-wide usage statistics: total calls, token usage, average latency, overall " +
                 "pass rate, and a per-model breakdown. Optionally bound the window with from/to (ISO-8601).")]
    /// <summary>
    /// Gets the dashboard.
    /// </summary>
    public async Task<McpDashboardDto> GetDashboard(
        [Description("Optional start of the window (ISO-8601). Omit for all-time.")] DateTimeOffset? from = null,
        [Description("Optional end of the window (ISO-8601). Omit for all-time.")] DateTimeOffset? to = null,
        CancellationToken cancellationToken = default)
    {
        var p = await project.GetProjectAsync(cancellationToken);
        var filter = new StatisticsFilter(from, to, p.Id);
        var view = await dashboard.GetDashboardViewAsync(filter, RecentTraceCount, AgentLimit, cancellationToken);

        var summary = new SummaryDto(
            view.Summary.TotalCalls,
            view.Summary.TotalInputTokens,
            view.Summary.TotalOutputTokens,
            view.Summary.TotalCachedInputTokens,
            view.Summary.AvgLatencyMs,
            view.Summary.OverallPassRate);
        var models = view.ModelBreakdown
            .Select(r => new ModelBreakdownDto(
                r.EndpointId, r.ModelName, r.CallCount,
                r.TotalInputTokens ?? 0, r.TotalOutputTokens ?? 0, r.TotalCachedInputTokens ?? 0, r.AvgDurationMs ?? 0))
            .ToArray();

        return new McpDashboardDto(summary, models);
    }

    [McpServerTool(Name = "get_agent_overview")]
    [Description("Get an agent's overview over a window (default last 30 days): token/cost/latency summary, " +
                 "daily time series, pass-rate trend, per-suite pass rates and entity counts. Use it to motivate " +
                 "an optimization (e.g. a model switch). The agent must belong to the current project.")]
    /// <summary>
    /// Gets the agent overview.
    /// </summary>
    public async Task<AgentOverviewDto> GetAgentOverview(
        [Description("The agent id (GUID), from list_agents.")] Guid agentId,
        [Description("Optional window start (ISO-8601). Defaults to 30 days before the end.")] DateTimeOffset? from = null,
        [Description("Optional window end (ISO-8601). Defaults to now.")] DateTimeOffset? to = null,
        CancellationToken cancellationToken = default)
    {
        var p = await project.GetProjectAsync(cancellationToken);
        var agent = await agents.FindAsync(agentId, cancellationToken);
        if (agent is null || agent.Project.Id != p.Id)
            throw new McpException($"Agent '{agentId}' was not found in this project.");

        var toTime = to ?? DateTimeOffset.UtcNow;
        var fromTime = from ?? toTime.AddDays(-30);
        if (fromTime >= toTime)
            throw new McpException("`from` must be before `to`.");

        var result = await agentStatistics.GetAgentOverviewAsync(agentId, fromTime, toTime, StatisticsBucket.Daily, cancellationToken);
        return new AgentOverviewDto(
            Summary: ToDto(result.Summary),
            TimeSeries: result.TimeSeries.Select(ToDto).ToArray(),
            PassRateTrend: result.PassRateTrend.Select(ToDto).ToArray(),
            SuitePassRates: result.SuitePassRates.Select(ToDto).ToArray(),
            Counts: ToDto(result.Counts));
    }

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
