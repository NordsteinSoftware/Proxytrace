namespace Proxytrace.Application.Statistics;

/// <summary>
/// Tuning for the in-process dashboard composite cache (see <c>DashboardStatistics</c>). The
/// dashboard is polled every 30 s by every open viewer; a short TTL lets N concurrent viewers share
/// one query fan-out instead of each re-running ~12 statistics queries. The default is registered by
/// the Application module; the API host overrides it from the <c>Statistics</c> configuration
/// section (<c>StatisticsOptions.DashboardCacheTtlSeconds</c>).
/// </summary>
public sealed record DashboardCacheOptions
{
    /// <summary>
    /// How long a computed dashboard view is served from the cache, in seconds. <c>0</c> disables
    /// caching entirely. Must stay well below the client's 30 s poll interval so data never appears
    /// frozen across two consecutive polls.
    /// </summary>
    public double TtlSeconds { get; init; } = 10d;

    /// <summary>
    /// Gets the ttl.
    /// </summary>
    public TimeSpan Ttl => TimeSpan.FromSeconds(TtlSeconds);
}
