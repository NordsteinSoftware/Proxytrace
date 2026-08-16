using Proxytrace.Domain.Statistics;
namespace Proxytrace.Application.Statistics;

/// <summary>
/// Read facade for the main dashboard and the traces overview. Consumed by
/// <c>StatisticsController</c> (dashboard) and <c>AgentCallsController</c> (traces overview).
/// </summary>
public interface IDashboardStatistics
{
    /// <summary>
    /// Composes the entire dashboard payload in a single call by fanning out to the granular
    /// statistics readers in parallel. Replaces the client's per-widget request waterfall.
    /// </summary>
    Task<DashboardView> GetDashboardViewAsync(StatisticsFilter filter, int recentTraceCount, int agentLimit, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns per-agent call volume, error rate, and cost totals for the filter window, used to populate the agents table on the dashboard.
    /// </summary>
    Task<IReadOnlyList<AgentBreakdownStat>> GetAgentBreakdownAsync(StatisticsFilter filter, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns bucketed p50/p95 latency series for the filter window, used to draw the latency chart.
    /// </summary>
    Task<IReadOnlyList<LatencyStat>> GetLatencyAsync(StatisticsFilter filter, CancellationToken cancellationToken = default);

    /// <summary>
    /// Bucketed per-agent anomaly counts (statistical vs custom-detector split) for the anomaly
    /// dashboard timeline.
    /// </summary>
    Task<IReadOnlyList<AgentAnomalyStat>> GetAnomalyCountsByAgentAsync(StatisticsFilter filter, StatisticsBucket bucket, CancellationToken cancellationToken = default);
}
