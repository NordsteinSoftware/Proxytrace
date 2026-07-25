using Autofac;
using Proxytrace.Domain.Statistics;
using Proxytrace.Application.Statistics;
using Proxytrace.Domain;
using Proxytrace.Domain.AgentCall;
using Proxytrace.Domain.Project;
using Proxytrace.Domain.Session;
using Proxytrace.PerfHarness.Bootstrap;
using Proxytrace.PerfHarness.Reporting;

namespace Proxytrace.PerfHarness.Scenarios;

/// <summary>
/// Times every read-heavy path the product depends on — the statistics aggregations, the per-agent
/// overview/distributions, and the traces list/histogram — against the seeded database, reporting p50
/// (logged) and p95 (budgeted). Ids and the time window are discovered from the data so the scenario
/// runs standalone after a separate <c>seed</c> invocation.
/// </summary>
internal static class QueryLatencyScenario
{
    public static async Task<IReadOnlyList<MetricResult>> RunAsync(
        PerfContainer container,
        PerfBudgets budgets,
        int warmup,
        int iterations,
        CancellationToken cancellationToken)
    {
        using var scope = container.BeginScope();
        var statsReader = scope.Resolve<IAgentCallStatsReader>();
        var agentStats = scope.Resolve<IAgentStatistics>();
        var callRepo = scope.Resolve<IAgentCallRepository>();
        var sessionRepo = scope.Resolve<ISessionRepository>();
        var projectRepo = scope.Resolve<IRepository<IProject>>();

        var lastCalls = await callRepo.GetLastCallTimesAsync(cancellationToken);
        if (lastCalls.Count == 0)
        {
            throw new InvalidOperationException("No agent calls found — run `seed` against this database first.");
        }

        Guid agentId = lastCalls.MaxBy(kv => kv.Value).Key;
        var project = await projectRepo.FindFirstAsync(cancellationToken);
        Guid? projectId = project?.Id;

        var now = DateTimeOffset.UtcNow;
        var from = now.AddDays(-90);
        var recent = now.AddDays(-7);
        var filter = new StatisticsFilter(From: from, To: now, ProjectId: projectId);

        Console.WriteLine($"[db-layer] querying agent {agentId}, project {projectId}, window {from:d}..{now:d}");

        var results = new List<MetricResult>();

        async Task Measure(string name, Func<Task> action)
        {
            var (p50, p95) = await PerfReport.MeasureLatencyAsync(warmup, iterations, action);
            Console.WriteLine($"[db-layer] {name,-26} p50={p50,8:N1}ms  p95={p95,8:N1}ms");
            results.Add(new MetricResult("db-layer", name, p95, budgets.DbQueryBudget(name), "ms", BudgetDirection.LowerIsBetter));
        }

        // Traces table (the hot list + histogram paths).
        await Measure("agentCallsList",
            () => callRepo.GetFilteredListAsync(new AgentCallFilter(), 1, 50, cancellationToken));
        await Measure("agentCallsListByAgent",
            () => callRepo.GetFilteredListAsync(new AgentCallFilter(AgentId: agentId), 1, 50, cancellationToken));
        await Measure("agentCallsListByTimeRange",
            () => callRepo.GetFilteredListAsync(new AgentCallFilter(From: recent, To: now), 1, 50, cancellationToken));
        await Measure("agentCallsHistogram",
            () => callRepo.GetHistogramAsync(new AgentCallFilter(AgentId: agentId), 50, cancellationToken));

        // Sorted list paths — server-side ORDER BY on the denormalised columns, worst case (no
        // narrowing filter, whole-table top-50). CreatedAt is the default already covered above.
        await Measure("agentCallsListSortLatency",
            () => callRepo.GetFilteredListAsync(new AgentCallFilter(SortBy: AgentCallSortField.Latency), 1, 50, cancellationToken));
        await Measure("agentCallsListSortTokens",
            () => callRepo.GetFilteredListAsync(new AgentCallFilter(SortBy: AgentCallSortField.TotalTokens), 1, 50, cancellationToken));
        await Measure("agentCallsListSortToolCount",
            () => callRepo.GetFilteredListAsync(new AgentCallFilter(SortBy: AgentCallSortField.ToolCount), 1, 50, cancellationToken));
        await Measure("agentCallsListSortCacheHit",
            () => callRepo.GetFilteredListAsync(new AgentCallFilter(SortBy: AgentCallSortField.CacheHitRate), 1, 50, cancellationToken));

        // Tool-name filter (EXISTS semi-join against the per-call tool rows) + the distinct
        // tool-name picker that feeds the filter's options.
        if (projectId is { } toolProjectId)
        {
            var toolNames = await callRepo.GetToolNamesAsync(toolProjectId, cancellationToken: cancellationToken);
            if (toolNames.Count > 0)
            {
                string toolName = toolNames[0];
                await Measure("agentCallsListByToolName",
                    () => callRepo.GetFilteredListAsync(new AgentCallFilter(ToolName: toolName), 1, 50, cancellationToken));
            }
            else
            {
                Console.WriteLine("[db-layer] no tool rows seeded — skipping agentCallsListByToolName");
            }

            await Measure("agentCallToolNames",
                () => callRepo.GetToolNamesAsync(toolProjectId, cancellationToken: cancellationToken));

            // Agent-scoped picker: the tool list shown when an agent filter is active. AgentId is
            // denormalised onto the tool row, so this stays a single-table index-only DISTINCT.
            await Measure("agentCallToolNamesByAgent",
                () => callRepo.GetToolNamesAsync(toolProjectId, agentId, cancellationToken));
        }

        // Session paths — the session-filtered traces list (session detail page) rides the
        // (SessionId, CreatedAt) index; the recent-sessions list rides (ProjectId, LastActivityAt desc)
        // and never aggregates the traces table (counters are denormalized on the Sessions row).
        if (projectId is { } sessionProjectId)
        {
            var (recentSessions, _) = await sessionRepo.GetRecentAsync(sessionProjectId, 1, 50, cancellationToken);
            if (recentSessions.Count > 0)
            {
                Guid sessionId = recentSessions[0].Id;
                await Measure("agentCallsListBySession",
                    () => callRepo.GetFilteredListAsync(new AgentCallFilter(SessionId: sessionId), 1, 50, cancellationToken));
            }
            else
            {
                Console.WriteLine("[db-layer] no sessions seeded — skipping agentCallsListBySession");
            }

            await Measure("sessionsRecent",
                () => sessionRepo.GetRecentAsync(sessionProjectId, 1, 50, cancellationToken));
        }

        // Dashboard statistics aggregations.
        await Measure("statsSummary",
            () => statsReader.GetSummaryAsync(filter, cancellationToken));
        await Measure("statsLatencyPercentiles",
            () => statsReader.GetLatencyAsync(filter, cancellationToken));
        await Measure("statsTokenUsage",
            () => statsReader.GetTokenUsageAsync(filter, StatisticsBucket.Daily, cancellationToken));
        await Measure("statsAgentBreakdown",
            () => statsReader.GetAgentBreakdownAsync(filter, cancellationToken));
        await Measure("statsModelBreakdown",
            () => statsReader.GetModelBreakdownAsync(filter, cancellationToken));
        await Measure("statsCostEstimate",
            () => statsReader.GetCostEstimateAsync(filter, cancellationToken));
        await Measure("statsCallTrends",
            () => statsReader.GetCallTrendsAsync(filter, 20, from, now, cancellationToken));
        await Measure("statsPulse",
            () => statsReader.GetPulseAsync(filter, now.AddMinutes(-60), now, 60, cancellationToken));
        await Measure("anomalyTimeline",
            () => statsReader.GetAnomalyCountsByAgentAsync(filter, StatisticsBucket.Daily, cancellationToken));

        // Per-agent overview page.
        await Measure("agentOverview",
            () => agentStats.GetAgentOverviewAsync(agentId, from, now, StatisticsBucket.Daily, cancellationToken));
        await Measure("agentDistributions",
            () => agentStats.GetAgentDistributionsAsync(agentId, from, now, cancellationToken));

        return results;
    }
}
