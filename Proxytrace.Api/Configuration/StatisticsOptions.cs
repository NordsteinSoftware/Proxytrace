namespace Proxytrace.Api.Configuration;

/// <summary>
/// Default and maximum page sizes for the dashboard statistics endpoint, plus the dashboard
/// composite cache TTL.
/// </summary>
public sealed record StatisticsOptions
{
    /// <summary>
    /// The client polls the dashboard on this interval; the cache TTL below must stay under it so
    /// two consecutive polls never see the same frozen payload.
    /// </summary>
    public const double DashboardPollIntervalSeconds = 30d;

    /// <summary>
    /// Gets or sets the default recent trace count.
    /// </summary>
    public int DefaultRecentTraceCount { get; init; } = 6;
    /// <summary>
    /// Gets or sets the max recent trace count.
    /// </summary>
    public int MaxRecentTraceCount { get; init; } = 50;
    /// <summary>
    /// Gets or sets the default agent limit.
    /// </summary>
    public int DefaultAgentLimit { get; init; } = 10;
    /// <summary>
    /// Gets or sets the max agent limit.
    /// </summary>
    public int MaxAgentLimit { get; init; } = 100;

    /// <summary>
    /// TTL of the in-process dashboard composite cache in seconds (<c>0</c> disables it). Bounds
    /// how stale a served dashboard can be; see <c>DashboardCacheOptions</c> in the Application
    /// layer, which this value feeds.
    /// </summary>
    public double DashboardCacheTtlSeconds { get; init; } = 10d;

    /// <summary>
    /// Validates.
    /// </summary>
    public void Validate()
    {
        if (DashboardCacheTtlSeconds is < 0d or >= DashboardPollIntervalSeconds)
        {
            throw new InvalidOperationException(
                $"{nameof(StatisticsOptions)}: {nameof(DashboardCacheTtlSeconds)} must be >= 0 and < {DashboardPollIntervalSeconds} (the dashboard poll interval).");
        }

        if (DefaultRecentTraceCount < 1 || DefaultRecentTraceCount > MaxRecentTraceCount)
        {
            throw new InvalidOperationException(
                $"{nameof(StatisticsOptions)}: {nameof(DefaultRecentTraceCount)} must be >= 1 and <= {nameof(MaxRecentTraceCount)}.");
        }

        if (DefaultAgentLimit < 1 || DefaultAgentLimit > MaxAgentLimit)
        {
            throw new InvalidOperationException(
                $"{nameof(StatisticsOptions)}: {nameof(DefaultAgentLimit)} must be >= 1 and <= {nameof(MaxAgentLimit)}.");
        }
    }
}
