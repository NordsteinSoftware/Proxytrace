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
    /// How many recent traces the dashboard statistics endpoint returns when the caller does not
    /// specify a count.
    /// </summary>
    public int DefaultRecentTraceCount { get; init; } = 6;
    /// <summary>
    /// Hard cap on the number of recent traces the dashboard endpoint will return, regardless of what
    /// the caller requests.
    /// </summary>
    public int MaxRecentTraceCount { get; init; } = 50;
    /// <summary>
    /// How many agents the dashboard's agent-breakdown list returns when the caller does not specify
    /// a limit.
    /// </summary>
    public int DefaultAgentLimit { get; init; } = 10;
    /// <summary>
    /// Hard cap on the agent count the dashboard endpoint returns, regardless of what the caller
    /// requests.
    /// </summary>
    public int MaxAgentLimit { get; init; } = 100;

    /// <summary>
    /// TTL of the in-process dashboard composite cache in seconds (<c>0</c> disables it). Bounds
    /// how stale a served dashboard can be; see <c>DashboardCacheOptions</c> in the Application
    /// layer, which this value feeds.
    /// </summary>
    public double DashboardCacheTtlSeconds { get; init; } = 10d;

    /// <summary>
    /// Asserts that the configured page sizes and cache TTL are internally consistent; throws
    /// <see cref="InvalidOperationException"/> on startup when they are not.
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
