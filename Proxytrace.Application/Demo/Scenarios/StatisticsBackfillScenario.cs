using System.Net;
using JetBrains.Annotations;
using Nordstein.Core.Common.Random;
using Proxytrace.Application.Demo.Internal;
using Proxytrace.Domain;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.AgentCall;
using Nordstein.Core.AI.Completions;
using Nordstein.Core.AI.Messages;
using Proxytrace.Domain.ModelEndpoint;
using Proxytrace.Domain.TestResult;
using Proxytrace.Domain.TestRun;
using Proxytrace.Domain.TestRunGroup;

namespace Proxytrace.Application.Demo.Scenarios;

/// <summary>
/// Backfills the trailing <see cref="WindowDays"/> with business-scale agent traffic — content,
/// rates, token weights and per-agent daily volumes all come from <see cref="DemoTrafficCatalog"/>
/// via <see cref="DemoCallPlanner"/>, shared with the live traffic feed. Also staggers the seeded
/// test-run groups across the window so the suite history reads as an ongoing practice.
/// </summary>
[UsedImplicitly]
internal sealed class StatisticsBackfillScenario : IDemoScenario
{
    private const int WindowDays = 14;

    private static readonly IReadOnlyDictionary<string, int[]> SuiteSchedule = new Dictionary<string, int[]>
    {
        ["customer-support-tone"] = [-13, -7, -1],
        ["customer-support-refunds"] = [-10, -3],
        ["code-review-bugs"] = [-12, -4],
        ["code-review-style"] = [-11, -5],
        ["data-analytics-queries"] = [-9, -2],
        // Three stable baseline runs, then the regression lands yesterday — recent enough for the
        // anomaly to feel live, old enough that the baseline window is well established.
        ["email-triage-priority"] = [-11, -8, -4, -1],
    };

    private readonly DemoSeedContext ctx;
    private readonly DemoCallPlanner planner;
    private readonly IAgentCall.CreateExisting agentCallExisting;
    private readonly ICompletion.Create completionFactory;
    private readonly IModelParameters.Create paramsFactory;
    private readonly ITestRun.CreateExisting testRunExisting;
    private readonly ITestRunGroup.CreateExisting testRunGroupExisting;
    private readonly ITestResult.CreateExisting testResultExisting;
    private readonly IRepository<IAgentCall> agentCallRepo;
    private readonly IRepository<ITestRunGroup> groupRepo;
    private readonly IRandom random;

    public StatisticsBackfillScenario(
        DemoSeedContext ctx,
        DemoCallPlanner planner,
        IAgentCall.CreateExisting agentCallExisting,
        ICompletion.Create completionFactory,
        IModelParameters.Create paramsFactory,
        ITestRun.CreateExisting testRunExisting,
        ITestRunGroup.CreateExisting testRunGroupExisting,
        ITestResult.CreateExisting testResultExisting,
        IRepository<IAgentCall> agentCallRepo,
        IRepository<ITestRunGroup> groupRepo,
        IRandom random)
    {
        this.ctx = ctx;
        this.planner = planner;
        this.agentCallExisting = agentCallExisting;
        this.completionFactory = completionFactory;
        this.paramsFactory = paramsFactory;
        this.testRunExisting = testRunExisting;
        this.testRunGroupExisting = testRunGroupExisting;
        this.testResultExisting = testResultExisting;
        this.agentCallRepo = agentCallRepo;
        this.groupRepo = groupRepo;
        this.random = random;
    }

    public int Order => 40;

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var windowStart = now.AddDays(-WindowDays);

        var profiles = new[]
        {
            new BackfillProfile(ctx.RequireCustomerSupportAgent(), DemoTrafficCatalog.Support),
            new BackfillProfile(ctx.RequireCodeReviewAgent(), DemoTrafficCatalog.CodeReview),
            new BackfillProfile(ctx.RequireDataAnalyticsAgent(), DemoTrafficCatalog.Analytics),
            new BackfillProfile(ctx.RequireEmailTriageAgent(), DemoTrafficCatalog.Triage),
        };

        var calls = new List<IAgentCall>();
        foreach (var profile in profiles)
        {
            CollectAgentCalls(profile, windowStart, now, calls);
        }

        await agentCallRepo.AddRangeAsync(calls, cancellationToken);

        await StaggerTestRunsAsync(now, cancellationToken);
    }

    private void CollectAgentCalls(
        BackfillProfile profile,
        DateTimeOffset windowStart,
        DateTimeOffset now,
        List<IAgentCall> calls)
    {
        var traffic = profile.Traffic;
        for (int day = 0; day < WindowDays; day++)
        {
            var dayStart = windowStart.AddDays(day);
            int count = random.Int(traffic.MinCallsPerDay, traffic.MaxCallsPerDay);
            for (int i = 0; i < count; i++)
            {
                var createdAt = SampleTimestamp(dayStart);
                if (createdAt > now)
                {
                    continue;
                }

                var endpoint = PickWeighted(traffic.EndpointMix);
                var plan = planner.Plan(traffic);
                Guid? conversationId = plan.SharesConversation ? Guid.NewGuid() : null;

                foreach (var planned in plan.Calls)
                {
                    calls.Add(BuildBackdatedCall(
                        profile.Agent, endpoint, planned,
                        createdAt.AddSeconds(planned.OffsetSeconds), conversationId));
                }
            }
        }
    }

    private IAgentCall BuildBackdatedCall(
        IAgent agent,
        IModelEndpoint endpoint,
        PlannedDemoCall planned,
        DateTimeOffset createdAt,
        Guid? conversationId)
    {
        var request = new Conversation([agent.CreateSystemMessage(), .. planned.RequestTail]);
        ICompletion? response = planned.ResponseMessage is null
            ? null
            : completionFactory(
                planned.ResponseMessage,
                planned.Usage,
                TimeSpan.FromMilliseconds(planned.LatencyMs));

        return agentCallExisting(
            agent: agent,
            version: agent.CurrentVersion,
            endpoint: endpoint,
            request: request,
            response: response,
            httpStatus: planned.HttpStatus,
            finishReason: planned.FinishReason,
            errorMessage: planned.ErrorMessage,
            modelParameters: paramsFactory(temperature: 0.3),
            existing: new BackdatedData(Guid.NewGuid(), createdAt, createdAt),
            conversationId: conversationId,
            outlierFlags: planned.OutlierFlags);
    }

    private DateTimeOffset SampleTimestamp(DateTimeOffset dayStart)
    {
        int hour = SampleDiurnalHour();
        int minute = random.Int(0, 60);
        int second = random.Int(0, 60);
        return dayStart.AddHours(hour).AddMinutes(minute).AddSeconds(second);
    }

    private int SampleDiurnalHour()
    {
        int[] weights = DemoTrafficCatalog.DiurnalWeights;
        int total = weights.Sum();
        int pick = random.Int(0, total);
        int acc = 0;
        for (int h = 0; h < weights.Length; h++)
        {
            acc += weights[h];
            if (pick < acc)
            {
                return h;
            }
        }
        return weights.Length - 1;
    }

    private IModelEndpoint PickWeighted(IReadOnlyList<DemoTrafficCatalog.EndpointShare> mix)
    {
        double pick = random.Double();
        double acc = 0;
        foreach (var share in mix)
        {
            acc += share.Weight;
            if (pick <= acc)
            {
                return Resolve(share.Endpoint);
            }
        }
        return Resolve(mix[^1].Endpoint);
    }

    private IModelEndpoint Resolve(DemoTrafficCatalog.DemoEndpointKey key)
        => key switch
        {
            DemoTrafficCatalog.DemoEndpointKey.Gpt54 => ctx.RequireGpt54Endpoint(),
            DemoTrafficCatalog.DemoEndpointKey.Gpt54Mini => ctx.RequireGpt54MiniEndpoint(),
            DemoTrafficCatalog.DemoEndpointKey.ClaudeSonnet => ctx.RequireClaudeEndpoint(),
            _ => throw new ArgumentOutOfRangeException(nameof(key), key, "Unknown demo endpoint key"),
        };

    private async Task StaggerTestRunsAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var groupOrder = new List<ITestRunGroup>();
        var seenGroups = new HashSet<Guid>();
        foreach (var run in ctx.AllRuns)
        {
            if (seenGroups.Add(run.Group.Id))
            {
                groupOrder.Add(run.Group);
            }
        }

        var perSuiteIndex = new Dictionary<Guid, int>();
        foreach (var group in groupOrder)
        {
            string? suiteKey = ctx.SuitesByKey.FirstOrDefault(kv => kv.Value.Id == group.Suite.Id).Key;
            if (suiteKey is null || !SuiteSchedule.TryGetValue(suiteKey, out var offsets))
            {
                continue;
            }

            int idx = perSuiteIndex.GetValueOrDefault(group.Suite.Id);
            if (idx >= offsets.Length)
            {
                continue;
            }
            perSuiteIndex[group.Suite.Id] = idx + 1;

            var groupTime = now.AddDays(offsets[idx]);
            await BackdateGroupAsync(group, groupTime, cancellationToken);
        }
    }

    private async Task BackdateGroupAsync(
        ITestRunGroup group,
        DateTimeOffset groupTime,
        CancellationToken cancellationToken)
    {
        var fresh = await groupRepo.GetAsync(group.Id, cancellationToken);
        var runs = await fresh.GetTestRuns(cancellationToken);

        foreach (var run in runs)
        {
            foreach (var result in run.TestResults)
            {
                var backdatedResult = testResultExisting(
                    testCase: result.TestCase,
                    actualResponse: result.ActualResponse,
                    evaluations: result.Evaluations,
                    latency: result.Latency,
                    usage: result.Usage,
                    existing: new BackdatedData(result.Id, groupTime, result.UpdatedAt));
                await backdatedResult.UpdateAsync(cancellationToken);
            }

            var backdatedRun = testRunExisting(
                group: fresh,
                endpoint: run.Endpoint,
                sampleIndex: run.SampleIndex,
                status: run.Status,
                completedAt: groupTime.AddMinutes(5),
                testResults: run.TestResults,
                existing: new BackdatedData(run.Id, groupTime, run.UpdatedAt));
            await backdatedRun.UpdateAsync(cancellationToken);
        }

        var backdatedGroup = testRunGroupExisting(
            suite: fresh.Suite,
            status: fresh.Status,
            completedAt: groupTime.AddMinutes(5),
            isSystemRun: fresh.IsSystemRun,
            scheduleId: fresh.ScheduleId,
            sampleCount: fresh.SampleCount,
            optimizationConsideredAt: fresh.OptimizationConsideredAt,
            existing: new BackdatedData(fresh.Id, groupTime, fresh.UpdatedAt));
        await backdatedGroup.UpdateAsync(cancellationToken);
    }

    private sealed record BackdatedData(Guid Id, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt) : IDomainEntityData;

    private sealed record BackfillProfile(
        IAgent Agent,
        DemoTrafficCatalog.AgentTraffic Traffic);
}
