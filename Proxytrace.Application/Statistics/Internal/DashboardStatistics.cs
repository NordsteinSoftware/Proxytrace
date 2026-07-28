using System.Collections.Concurrent;
using Proxytrace.Domain.Statistics;
using Proxytrace.Domain.Statistics.TestRun;
using Proxytrace.Common.Time;
using Proxytrace.Domain;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.AgentCall;
using Proxytrace.Messaging;

namespace Proxytrace.Application.Statistics.Internal;

internal class DashboardStatistics : IDashboardStatistics
{
    /// <summary>
    /// The pass-rate sparkline renders at most this many recent (group, endpoint) cohorts. The
    /// trend query is bounded server-side by this cap, so the payload no longer grows with total
    /// test-run history (the projection table has no retention).
    /// </summary>
    private const int SparklineCohortLimit = 50;

    // The pulse band's fixed contract: 60 one-minute buckets over the trailing hour.
    private const int PulseMinutes = 60;

    /// <summary>
    /// When the view cache holds at least this many entries, expired ones are swept on the next
    /// miss. Distinct filters (e.g. per-viewer <c>From</c> timestamps) would otherwise leave
    /// abandoned expired entries behind, since normal eviction only happens on a same-key hit.
    /// </summary>
    private const int CacheSweepThreshold = 64;

    private readonly ITestRunStatsReader runStats;
    private readonly IAgentCallStatsReader callStats;
    private readonly IAgentRepository agents;
    private readonly IAgentCallRepository agentCalls;
    private readonly IIngestionStream ingestionStream;
    private readonly ITransaction transaction;
    private readonly IClock clock;
    private readonly DashboardCacheOptions cacheOptions;

    // Keyed by the full request identity, so two requests only ever share a computation when they
    // would produce the identical payload. ProjectId being part of the key keeps tenants isolated:
    // the controller enforces project access before calling in, and the global (ProjectId == null)
    // entry is only ever requested — and therefore only ever served — for admins.
    private readonly ConcurrentDictionary<CacheKey, CacheEntry> viewCache = new();

    public DashboardStatistics(
        ITestRunStatsReader runStats,
        IAgentCallStatsReader callStats,
        IAgentRepository agents,
        IAgentCallRepository agentCalls,
        IIngestionStream ingestionStream,
        ITransaction transaction,
        IClock clock,
        DashboardCacheOptions cacheOptions)
    {
        this.runStats = runStats;
        this.callStats = callStats;
        this.agents = agents;
        this.agentCalls = agentCalls;
        this.ingestionStream = ingestionStream;
        this.transaction = transaction;
        this.clock = clock;
        this.cacheOptions = cacheOptions;
    }

    public async Task<DashboardView> GetDashboardViewAsync(StatisticsFilter filter, int recentTraceCount, int agentLimit, CancellationToken cancellationToken = default)
    {
        TimeSpan ttl = cacheOptions.Ttl;
        if (ttl <= TimeSpan.Zero)
        {
            return await ComputeDashboardViewAsync(filter, recentTraceCount, agentLimit, cancellationToken);
        }

        // Short-TTL single-flight cache: concurrent requests for the same key await one shared
        // computation (the Lazy makes exactly one caller start it) instead of stampeding the
        // database, and every viewer polling within the TTL is served the cached composite. The
        // shared computation deliberately runs on CancellationToken.None so one viewer
        // disconnecting cannot fault the result every other viewer is awaiting; each caller's own
        // token still cancels its await via WaitAsync.
        var key = new CacheKey(filter, recentTraceCount, agentLimit);
        while (true)
        {
            DateTimeOffset now = clock.UtcNow;
            CacheEntry entry = viewCache.GetOrAdd(key, _ =>
            {
                SweepExpiredEntries(now, ttl);
                return new CacheEntry(now, new Lazy<Task<DashboardView>>(
                    () => ComputeDashboardViewAsync(filter, recentTraceCount, agentLimit, CancellationToken.None)));
            });

            if (now - entry.CachedAt > ttl)
            {
                // Remove only this exact expired entry (the KeyValuePair overload compares the
                // value), never a fresh one a concurrent caller may just have added, then retry.
                viewCache.TryRemove(new KeyValuePair<CacheKey, CacheEntry>(key, entry));
                continue;
            }

            try
            {
                return await entry.View.Value.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // This caller gave up; the shared computation keeps running for other awaiters.
                throw;
            }
            catch
            {
                // Never cache a failure: evict so the next request retries the computation.
                viewCache.TryRemove(new KeyValuePair<CacheKey, CacheEntry>(key, entry));
                throw;
            }
        }
    }

    private void SweepExpiredEntries(DateTimeOffset now, TimeSpan ttl)
    {
        if (viewCache.Count < CacheSweepThreshold)
        {
            return;
        }

        foreach (KeyValuePair<CacheKey, CacheEntry> pair in viewCache)
        {
            if (now - pair.Value.CachedAt > ttl)
            {
                viewCache.TryRemove(pair);
            }
        }
    }

    private async Task<DashboardView> ComputeDashboardViewAsync(StatisticsFilter filter, int recentTraceCount, int agentLimit, CancellationToken cancellationToken)
    {
        // The fan-out below offloads each independent query onto the thread pool via Task.Run. The
        // ambient transactional StorageDbContext flows through AsyncLocal into those continuations, so
        // if this method ever ran inside a transaction all the tasks would share that one context
        // concurrently and EF would throw "A second operation was started on this context instance".
        // This path is read-only and must never be wrapped in a transaction — fail loudly rather than
        // risk that race. The cache above never bypasses this guard on a miss: a cached *hit* inside a
        // transaction is safe (it runs no queries), and a miss computes through here.
        if (transaction.IsActive)
        {
            throw new InvalidOperationException(
                "GetDashboardViewAsync must not be called inside a transaction: its parallel query " +
                "fan-out would share the ambient DbContext across concurrent operations.");
        }

        // Every query below is independent and read-only. On a relational provider the awaits yield
        // on real database I/O, so a plain Task.WhenAll fan-out genuinely overlaps them. The kiosk
        // in-memory EF provider, however, executes queries synchronously (it never yields), which
        // silently collapses the fan-out into sequential execution and turns a sub-100ms dashboard
        // into a ~600ms one. Offloading each query to the thread pool restores the intended
        // concurrency there; on relational the extra hop is negligible. The reads run on fresh
        // per-call DbContexts (no ambient transaction on this path), so parallel execution is safe.
        Task<StatisticsSummary> summaryTask = Task.Run(() => GetSummaryAsync(filter, cancellationToken), cancellationToken);
        Task<LiveTelemetry> telemetryTask = Task.Run(() => GetLiveTelemetryAsync(filter, cancellationToken), cancellationToken);
        Task<DashboardTrends> trendsTask = Task.Run(() => GetDashboardTrendsAsync(filter, cancellationToken), cancellationToken);
        Task<IReadOnlyList<AgentBreakdownStat>> agentBreakdownTask = Task.Run(() => GetAgentBreakdownAsync(filter, cancellationToken), cancellationToken);
        Task<IReadOnlyList<LatencyStat>> latencyTask = Task.Run(() => GetLatencyAsync(filter, cancellationToken), cancellationToken);
        Task<IReadOnlyList<ModelBreakdownStat>> modelBreakdownTask = Task.Run(() => GetModelBreakdownAsync(filter, cancellationToken), cancellationToken);
        // The two token-volume queries need the resolved bucket width, so they await the bucket task
        // rather than blocking the fan-out on it up front.
        Task<StatisticsBucket> tokenBucketTask = Task.Run(() => ResolveTokenBucketAsync(filter, cancellationToken), cancellationToken);
        Task<IReadOnlyList<TokenUsageStat>> tokenUsageTask = Task.Run(async () => await GetTokenUsageAsync(filter, await tokenBucketTask, cancellationToken), cancellationToken);
        Task<IReadOnlyList<AgentTokenUsageStat>> tokenByAgentTask = Task.Run(async () => await GetTokenUsageByAgentAsync(filter, await tokenBucketTask, cancellationToken), cancellationToken);
        // The recent-traces list carries the same project scope as the aggregates — including a
        // multi-project one (#483), which the trace filter applies as a set (#482).
        Task<(IReadOnlyList<IAgentCall> Items, int Total)> recentTask = Task.Run(() => agentCalls.GetFilteredAsync(
            new AgentCallFilter(
                ProjectId: filter.ProjectId,
                From: filter.From,
                IncludeSystemAgents: !filter.ExcludeSystemAgents,
                ProjectIds: filter.ProjectIds),
            page: 1,
            pageSize: recentTraceCount,
            cancellationToken), cancellationToken);
        // Scope the agent load to the filter's projects, instead of loading every agent and
        // discarding the rest in memory. The unfiltered (global) dashboard still needs all agents.
        Task<IReadOnlyList<IAgent>> agentsTask = Task.Run(() => GetScopedAgentsAsync(filter, cancellationToken), cancellationToken);
        Task<IReadOnlyDictionary<Guid, DateTimeOffset>> lastCallTimesTask = Task.Run(() => agentCalls.GetLastCallTimesAsync(cancellationToken), cancellationToken);
        Task<IReadOnlyList<int>> pulseTask = Task.Run(() => GetPulseAsync(filter, cancellationToken), cancellationToken);

        await Task.WhenAll(
            summaryTask, telemetryTask, trendsTask, agentBreakdownTask, latencyTask,
            modelBreakdownTask, tokenBucketTask, tokenUsageTask, tokenByAgentTask, recentTask, agentsTask, lastCallTimesTask,
            pulseTask);

        StatisticsBucket tokenBucket = tokenBucketTask.Result;

        IReadOnlyDictionary<Guid, DateTimeOffset> lastCallTimes = lastCallTimesTask.Result;
        IReadOnlyList<IAgent> topAgents = agentsTask.Result
            // The agent load isn't routed through the call-stats query, so drop system agents here too
            // when excluded, keeping the returned Agents list consistent with the filtered aggregates.
            .Where(a => !filter.ExcludeSystemAgents || !a.IsSystemAgent)
            .OrderByDescending(a => lastCallTimes.TryGetValue(a.Id, out DateTimeOffset t) ? t : DateTimeOffset.MinValue)
            .ThenByDescending(a => a.UpdatedAt)
            .Take(agentLimit)
            .ToArray();

        return new DashboardView(
            Summary: summaryTask.Result,
            LiveTelemetry: telemetryTask.Result,
            Trends: trendsTask.Result,
            AgentBreakdown: agentBreakdownTask.Result,
            Latency: latencyTask.Result,
            ModelBreakdown: modelBreakdownTask.Result,
            TokenUsage: tokenUsageTask.Result,
            TokenUsageByAgent: tokenByAgentTask.Result,
            TokenBucket: tokenBucket,
            RecentTraces: recentTask.Result.Items,
            Agents: topAgents,
            AgentLastCallTimes: lastCallTimes,
            Pulse: pulseTask.Result);
    }

    public Task<IReadOnlyList<AgentBreakdownStat>> GetAgentBreakdownAsync(StatisticsFilter filter, CancellationToken cancellationToken = default)
        => callStats.GetAgentBreakdownAsync(filter, cancellationToken);

    public Task<IReadOnlyList<LatencyStat>> GetLatencyAsync(StatisticsFilter filter, CancellationToken cancellationToken = default)
        => callStats.GetLatencyAsync(filter, cancellationToken);

    public Task<IReadOnlyList<AgentAnomalyStat>> GetAnomalyCountsByAgentAsync(StatisticsFilter filter, StatisticsBucket bucket, CancellationToken cancellationToken = default)
        => callStats.GetAnomalyCountsByAgentAsync(filter, bucket, cancellationToken);

    internal async Task<StatisticsSummary> GetSummaryAsync(StatisticsFilter filter, CancellationToken cancellationToken = default)
    {
        StatisticsSummary callSummary = await callStats.GetSummaryAsync(filter, cancellationToken);
        TestRunStats.Filter runFilter = await ToRunFilterAsync(filter, cancellationToken);
        // Totals aggregate server-side; the table has no retention, so materializing every run's
        // stats row just to sum two columns would degrade linearly with total history.
        TestRunPassTotals totals = await runStats.GetPassTotalsAsync(runFilter, cancellationToken);

        double? passRate = totals.TotalCases > 0 ? totals.TotalPassed / (double)totals.TotalCases : null;

        return callSummary with { OverallPassRate = passRate };
    }

    internal Task<IReadOnlyList<TokenUsageStat>> GetTokenUsageAsync(StatisticsFilter filter, StatisticsBucket bucket, CancellationToken cancellationToken = default)
        => callStats.GetTokenUsageAsync(filter, bucket, cancellationToken);

    /// <summary>
    /// Derives the token-volume bucket width from the window so short ranges (1h/24h) resolve to
    /// sub-day buckets and still produce a multi-point series. The all-time view has no lower bound,
    /// so the window is measured from the earliest matching call instead — a few hours of history
    /// then renders at 5-minute/hourly resolution rather than collapsing into one daily bar.
    /// </summary>
    private async Task<StatisticsBucket> ResolveTokenBucketAsync(StatisticsFilter filter, CancellationToken cancellationToken)
    {
        DateTimeOffset to = filter.To ?? DateTimeOffset.UtcNow;
        DateTimeOffset? from = filter.From ?? await callStats.GetEarliestCallAsync(filter, cancellationToken);
        return from is null ? StatisticsBucket.Daily : StatisticsTime.ForWindow(from.Value, to);
    }

    internal Task<IReadOnlyList<ModelBreakdownStat>> GetModelBreakdownAsync(StatisticsFilter filter, CancellationToken cancellationToken = default)
        => callStats.GetModelBreakdownAsync(filter, cancellationToken);

    internal Task<IReadOnlyList<AgentTokenUsageStat>> GetTokenUsageByAgentAsync(StatisticsFilter filter, StatisticsBucket bucket, CancellationToken cancellationToken = default)
        => callStats.GetTokenUsageByAgentAsync(filter, bucket, cancellationToken);

    private Task<IReadOnlyList<int>> GetPulseAsync(StatisticsFilter filter, CancellationToken cancellationToken)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        return callStats.GetPulseAsync(filter, now.AddMinutes(-PulseMinutes), now, PulseMinutes, cancellationToken);
    }

    internal async Task<LiveTelemetry> GetLiveTelemetryAsync(StatisticsFilter filter, CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        LiveTelemetry traffic = await callStats.GetLiveTelemetryAsync(filter, now.AddMinutes(-5), now, cancellationToken);
        long queueDepth = await ingestionStream.GetQueueDepthAsync(cancellationToken);
        return traffic with
        {
            QueueDepth = (int)Math.Min(queueDepth, int.MaxValue),
        };
    }

    internal async Task<DashboardTrends> GetDashboardTrendsAsync(StatisticsFilter filter, CancellationToken cancellationToken = default)
    {
        const int buckets = 20;
        DateTimeOffset to = filter.To ?? DateTimeOffset.UtcNow;
        DateTimeOffset from = filter.From ?? to.AddDays(-1);

        CallTrends trends = await callStats.GetCallTrendsAsync(filter, buckets, from, to, cancellationToken);

        TestRunStats.Filter runFilter = await ToRunFilterAsync(filter, cancellationToken);
        // One sparkline point per (group, endpoint) cohort so sampled runs don't cluster N points.
        // The cohorts aggregate server-side and are capped to the most recent SparklineCohortLimit,
        // so the payload stays bounded regardless of accumulated test-run history.
        IReadOnlyList<TestRunCohort> cohorts = await runStats.GetRecentCohortsAsync(runFilter, SparklineCohortLimit, cancellationToken);
        double[] passRate = cohorts
            .Where(c => c.TestCases > 0)
            .Select(c => c.Passed / (double)c.TestCases * 100d)
            .ToArray();

        return new DashboardTrends(trends.Traces, trends.LatencyMs, trends.Throughput, passRate);
    }

    /// <summary>
    /// The agents the filter's project scope covers: one project's agents when it names a project,
    /// the union of the scope's projects when it spans several (#483), and every agent when it is
    /// unscoped (the admin-only global dashboard).
    /// </summary>
    /// <remarks>
    /// The multi-project case loads all agents and narrows in memory, mirroring how the agent and
    /// evaluator listings scope themselves — the agents table is small and bounded by the licensed
    /// agent limit, unlike the trace table its aggregates run over.
    /// </remarks>
    private async Task<IReadOnlyList<IAgent>> GetScopedAgentsAsync(StatisticsFilter filter, CancellationToken cancellationToken)
    {
        if (filter.ProjectId is { } projectId)
        {
            return await agents.GetByProjectAsync(projectId, cancellationToken);
        }

        IReadOnlyList<IAgent> all = await agents.GetAllAsync(cancellationToken);
        return filter.ProjectIds is { Count: > 0 } projectIds
            ? all.Where(a => projectIds.Contains(a.Project.Id)).ToArray()
            : all;
    }

    private async Task<TestRunStats.Filter> ToRunFilterAsync(StatisticsFilter filter, CancellationToken cancellationToken)
    {
        IReadOnlyCollection<Guid>? agentIds = null;
        if (filter.ProjectId is not null || filter.ProjectIds is { Count: > 0 })
        {
            IReadOnlyList<IAgent> scopedAgents = await GetScopedAgentsAsync(filter, cancellationToken);
            agentIds = scopedAgents.Select(a => a.Id).ToArray();
        }

        return new TestRunStats.Filter(
            AgentId: filter.AgentId,
            AgentIds: agentIds,
            EndpointId: filter.EndpointId,
            From: filter.From,
            To: filter.To);
    }

    private sealed record CacheKey(StatisticsFilter Filter, int RecentTraceCount, int AgentLimit);

    private sealed record CacheEntry(DateTimeOffset CachedAt, Lazy<Task<DashboardView>> View);
}
