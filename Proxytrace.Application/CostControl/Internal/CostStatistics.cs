using Proxytrace.Common.Time;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.CostLimit;
using Proxytrace.Domain.CostLimitBreach;
using Proxytrace.Domain.Statistics;

namespace Proxytrace.Application.CostControl.Internal;

internal class CostStatistics : ICostStatistics
{
    private readonly IAgentCallStatsReader callStats;
    private readonly ICostLimitRepository costLimits;
    private readonly ICostLimitBreachRepository breaches;
    private readonly IAgentRepository agents;
    private readonly IClock clock;

    public CostStatistics(
        IAgentCallStatsReader callStats,
        ICostLimitRepository costLimits,
        ICostLimitBreachRepository breaches,
        IAgentRepository agents,
        IClock clock)
    {
        this.callStats = callStats;
        this.costLimits = costLimits;
        this.breaches = breaches;
        this.agents = agents;
        this.clock = clock;
    }

    public Task<IReadOnlyList<ProjectAgentCostStat>> GetMonthToDateSpendAsync(
        DateTimeOffset monthStart,
        CancellationToken cancellationToken = default)
        => callStats.GetCostByProjectAndAgentAsync(
            new StatisticsFilter(From: monthStart),
            cancellationToken);

    public async Task<CostOverview> GetCostOverviewAsync(
        Guid projectId,
        DateTimeOffset from,
        DateTimeOffset to,
        StatisticsBucket bucket,
        CancellationToken cancellationToken = default)
    {
        DateTimeOffset monthStart = CostMonth.StartOf(clock.UtcNow);
        DateTimeOffset previousMonthStart = monthStart.AddMonths(-1);

        var windowFilter = new StatisticsFilter(From: from, To: to, ProjectId: projectId);

        Task<IReadOnlyList<AgentCostPoint>> seriesTask =
            callStats.GetCostSeriesByAgentAsync(windowFilter, bucket, cancellationToken);
        Task<IReadOnlyList<ProjectAgentCostStat>> windowTotalsTask =
            callStats.GetCostByProjectAndAgentAsync(windowFilter, cancellationToken);
        Task<IReadOnlyList<ProjectAgentCostStat>> monthTask =
            callStats.GetCostByProjectAndAgentAsync(
                new StatisticsFilter(From: monthStart, ProjectId: projectId), cancellationToken);
        Task<IReadOnlyList<ProjectAgentCostStat>> previousMonthTask =
            callStats.GetCostByProjectAndAgentAsync(
                new StatisticsFilter(From: previousMonthStart, To: monthStart.AddTicks(-1), ProjectId: projectId),
                cancellationToken);
        Task<bool> unpricedTask = callStats.HasUnpricedEndpointsAsync(windowFilter, cancellationToken);
        Task<IReadOnlyList<ICostLimit>> limitsTask = costLimits.GetByProjectAsync(projectId, cancellationToken);
        Task<IReadOnlyList<ICostLimitBreach>> breachTask = breaches.GetForMonthAsync(monthStart, cancellationToken);
        Task<IReadOnlyList<IAgent>> agentsTask = agents.GetByProjectAsync(projectId, cancellationToken);

        await Task.WhenAll(
            seriesTask, windowTotalsTask, monthTask, previousMonthTask,
            unpricedTask, limitsTask, breachTask, agentsTask);

        IReadOnlyList<AgentCostPoint> series = await seriesTask;
        IReadOnlyList<ProjectAgentCostStat> windowTotals = await windowTotalsTask;
        IReadOnlyList<ProjectAgentCostStat> monthTotals = await monthTask;
        IReadOnlyList<ProjectAgentCostStat> previousMonthTotals = await previousMonthTask;
        bool hasUnpriced = await unpricedTask;
        IReadOnlyList<ICostLimit> limits = await limitsTask;
        IReadOnlyList<ICostLimitBreach> monthBreaches = await breachTask;
        IReadOnlyList<IAgent> projectAgents = await agentsTask;

        Dictionary<Guid, string> agentNames = projectAgents
            .GroupBy(a => a.Id)
            .ToDictionary(g => g.Key, g => g.First().Name);

        decimal monthToDate = monthTotals.Sum(s => s.CostEur);
        Dictionary<Guid, decimal> monthByAgent = monthTotals
            .GroupBy(s => s.AgentId)
            .ToDictionary(g => g.Key, g => g.Sum(s => s.CostEur));

        AgentCostTotal[] agentTotals = windowTotals
            .GroupBy(s => s.AgentId)
            .Select(g => new AgentCostTotal(
                AgentId: g.Key,
                // An archived or since-deleted agent still owns historical spend; fall back to the
                // id rather than dropping the row and silently under-reporting the total.
                AgentName: agentNames.TryGetValue(g.Key, out var name) ? name : g.Key.ToString(),
                CostEur: g.Sum(s => s.CostEur)))
            .OrderByDescending(t => t.CostEur)
            .ToArray();

        var breachSet = monthBreaches
            .Select(b => (b.CostLimit.Id, b.Threshold))
            .ToHashSet();

        CostBudgetStatus[] budgets = limits
            .Select(limit => new CostBudgetStatus(
                CostLimitId: limit.Id,
                AgentId: limit.Agent?.Id,
                AgentName: limit.Agent?.Name,
                SoftLimitEur: limit.SoftLimitEur,
                HardLimitEur: limit.HardLimitEur,
                Enabled: limit.Enabled,
                MonthToDateSpendEur: limit.Agent is { } agent
                    ? monthByAgent.GetValueOrDefault(agent.Id)
                    : monthToDate,
                SoftBreached: breachSet.Contains((limit.Id, CostThreshold.Soft)),
                HardBreached: breachSet.Contains((limit.Id, CostThreshold.Hard))))
            // Project-wide budget first, then agent overrides alphabetically.
            .OrderBy(b => b.AgentId is null ? 0 : 1)
            .ThenBy(b => b.AgentName)
            .ToArray();

        return new CostOverview(
            MonthToDateSpendEur: monthToDate,
            PreviousMonthSpendEur: previousMonthTotals.Sum(s => s.CostEur),
            Series: series,
            AgentTotals: agentTotals,
            Budgets: budgets,
            HasUnpricedEndpoints: hasUnpriced);
    }
}
