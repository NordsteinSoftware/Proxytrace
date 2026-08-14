using JetBrains.Annotations;
using Proxytrace.Domain;
using Proxytrace.Domain.Evaluation;
using Proxytrace.Domain.Evaluator;
using Nordstein.Core.AI.Messages;
using Proxytrace.Domain.ModelEndpoint;
using Proxytrace.Domain.TestResult;
using Proxytrace.Domain.TestRun;
using Proxytrace.Domain.TestRunGroup;
using Proxytrace.Domain.TestSuite;
using Nordstein.Core.AI.Completions;

namespace Proxytrace.Application.Demo.Scenarios;

[UsedImplicitly]
internal sealed class TestRunSeedScenario : IDemoScenario
{
    private readonly DemoSeedContext ctx;
    private readonly ITestRunGroup.CreateExisting groupExisting;
    private readonly ITestRun.CreateExisting runExisting;
    private readonly ITestResult.CreateExisting resultExisting;
    private readonly IEvaluation.Create createEvaluation;

    public TestRunSeedScenario(
        DemoSeedContext ctx,
        ITestRunGroup.CreateExisting groupExisting,
        ITestRun.CreateExisting runExisting,
        ITestResult.CreateExisting resultExisting,
        IEvaluation.Create createEvaluation)
    {
        this.ctx = ctx;
        this.groupExisting = groupExisting;
        this.runExisting = runExisting;
        this.resultExisting = resultExisting;
        this.createEvaluation = createEvaluation;
    }

    public int Order => 30;

    private sealed record RunSpec(
        string SuiteKey,
        [UsedImplicitly] string GroupKey,
        IReadOnlyList<EndpointPick> Endpoints,
        bool IsRegressedTriageGroup = false);

    [UsedImplicitly]
    private sealed record EndpointPick(
        Func<DemoSeedContext, IModelEndpoint> SelectEndpoint,
        double PassRate,
        int LatencyBaseMs = 720);

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        var specs = new RunSpec[]
        {
            // Customer Support tone: improving trend, plus a Claude comparison on the latest group
            new("customer-support-tone", "tone-baseline",
                [new(c => c.RequireGpt54Endpoint(), PassRate: 0.50)]),
            new("customer-support-tone", "tone-after-prompt-tweak",
                [new(c => c.RequireGpt54Endpoint(), PassRate: 0.67)]),
            new("customer-support-tone", "tone-current",
                [
                    new(c => c.RequireGpt54Endpoint(), PassRate: 0.83),
                    new(c => c.RequireClaudeEndpoint(), PassRate: 1.00),
                ]),

            // Customer Support refunds: all-green history — no policy evaluator is seeded, so the
            // suite passes 10/10 in every historical run. The showcase punchline is that the
            // presenter creates a Policy Compliance evaluator live and the suite immediately fails
            // the new social-engineering cases. The triage suite carries the "failing long tail"
            // storyline; this one stays entirely green until the evaluator is added on stage.
            new("customer-support-refunds", "refunds-week-1",
                [new(c => c.RequireGpt54Endpoint(), PassRate: 1.00)]),
            new("customer-support-refunds", "refunds-week-2",
                [new(c => c.RequireGpt54Endpoint(), PassRate: 1.00)]),

            // Code review bugs: high pass rate from the start
            new("code-review-bugs", "bugs-initial",
                [new(c => c.RequireClaudeEndpoint(), PassRate: 0.86)]),
            new("code-review-bugs", "bugs-rerun",
                [new(c => c.RequireClaudeEndpoint(), PassRate: 0.86)]),

            // Code review style: stagnant; motivates a rejected prompt proposal
            new("code-review-style", "style-baseline",
                [new(c => c.RequireClaudeEndpoint(), PassRate: 0.40)]),
            new("code-review-style", "style-after-prompt-attempt",
                [new(c => c.RequireClaudeEndpoint(), PassRate: 0.40)]),

            // Analytics: cheaper-model comparison favours gpt-5.4-mini
            new("data-analytics-queries", "analytics-baseline",
                [new(c => c.RequireGpt54Endpoint(), PassRate: 0.75)]),
            new("data-analytics-queries", "analytics-mini-comparison",
                [
                    new(c => c.RequireGpt54Endpoint(), PassRate: 0.75),
                    new(c => c.RequireGpt54MiniEndpoint(), PassRate: 0.88),
                ]),

            // Email triage: a stable baseline, then a fresh sharp regression (pass rate and
            // latency) shaped to trip the real anomaly detector's rules during seeding. The two
            // hard cases at the end of the suite fail in every run, including the baseline.
            new("email-triage-priority", "triage-baseline",
                [new(c => c.RequireGpt54MiniEndpoint(), PassRate: 0.75)]),
            new("email-triage-priority", "triage-week-2",
                [new(c => c.RequireGpt54MiniEndpoint(), PassRate: 0.75)]),
            new("email-triage-priority", "triage-week-3",
                [new(c => c.RequireGpt54MiniEndpoint(), PassRate: 0.75)]),
            new("email-triage-priority", "triage-regression",
                [new(c => c.RequireGpt54MiniEndpoint(), PassRate: 0.25, LatencyBaseMs: 1290)],
                IsRegressedTriageGroup: true),
        };

        foreach (var spec in specs)
        {
            var suite = RequireSuite(spec.SuiteKey);

            // Seed the group and its runs directly in their terminal Completed state — never through
            // the Pending → Running live-run path. In a live kiosk (a Kiosk:Endpoint is configured)
            // the TestRunnerService loop only ever picks up Pending/Running work; a group that is
            // Completed from the moment it is persisted can never be executed against the model, so
            // there is no race between the seeder and the runner and no need for any runtime guard.
            var group = await SeedCompletedGroupAsync(suite, isSystemRun: false, cancellationToken);

            foreach (var pick in spec.Endpoints)
            {
                var run = await SeedCompletedRunAsync(suite, group, pick, cancellationToken);
                ctx.AllRuns.Add(run);
            }

            if (spec.IsRegressedTriageGroup)
                ctx.RegressedTriageGroup = group;
        }

        await SeedFailedToneGroupAsync(cancellationToken);
        await SeedAbCandidateRunsAsync(cancellationToken);
    }

    private ITestSuite RequireSuite(string suiteKey)
        => ctx.SuitesByKey.TryGetValue(suiteKey, out var suite)
            ? suite
            : throw new InvalidOperationException($"Test suite '{suiteKey}' not seeded.");

    /// <summary>
    /// A group persisted straight into the terminal <see cref="TestRunStatus.Completed"/> state with
    /// its completion time already set — no transient Pending/Running window a concurrent runner
    /// could latch onto. Its runs are attached afterwards (they reference the group by FK); the
    /// group's own validation never inspects run count, so a Completed group with no runs yet is
    /// valid at the moment it is written.
    /// </summary>
    private Task<ITestRunGroup> SeedCompletedGroupAsync(
        ITestSuite suite, bool isSystemRun, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        return groupExisting(
                suite,
                status: TestRunStatus.Completed,
                completedAt: now,
                isSystemRun: isSystemRun,
                scheduleId: null,
                // Marked as already considered: these groups are seeded terminal, so the optimizer's
                // restart recovery would otherwise treat every demo group as an unprocessed backlog
                // and fire a real A/B run (and real LLM spend) on every boot.
                optimizationConsideredAt: now,
                sampleCount: 1,
                existing: new SeedData(Guid.NewGuid(), now, now))
            .AddAsync(cancellationToken);
    }

    /// <summary>
    /// Builds all of a run's results and then the run itself directly in the terminal
    /// <see cref="TestRunStatus.Completed"/> state. Results are persisted before the run because the
    /// run stores its results by id; the run is never left Pending, so it never enters the runner's
    /// work queue.
    /// </summary>
    private async Task<ITestRun> SeedCompletedRunAsync(
        ITestSuite suite,
        ITestRunGroup group,
        EndpointPick pick,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var endpoint = pick.SelectEndpoint(ctx);
        var cases = suite.TestCases.ToArray();
        int passing = (int)Math.Round(pick.PassRate * cases.Length);

        var results = new List<ITestResult>(cases.Length);
        for (int i = 0; i < cases.Length; i++)
        {
            var testCase = cases[i];
            bool shouldPass = i < passing;
            var actualResponse = shouldPass
                ? testCase.ExpectedOutput
                : new AssistantMessage(
                    [Content.FromText("(simulated weaker response for demo)")],
                    []);

            var evaluations = suite.Evaluators
                .Select(ev =>
                {
                    TokenUsage? evalUsage = null;
                    decimal? evalCost = null;
                    if (ev is IAgenticEvaluator agentic)
                    {
                        evalUsage = new TokenUsage(inputTokenCount: 360UL + (ulong)(i * 4), outputTokenCount: 55UL + (ulong)(i * 2));
                        evalCost = agentic.Agent.Endpoint.CalculateCost(evalUsage);
                    }
                    return createEvaluation(
                        ev,
                        shouldPass ? EvaluationScore.Good : EvaluationScore.Bad,
                        TimeSpan.FromMilliseconds(120 + (i * 5)),
                        tokenUsage: evalUsage,
                        cost: evalCost,
                        reasoning: shouldPass
                            ? $"{ev.Name}: response matches expected tone and content."
                            : $"{ev.Name}: response is off-target or missing key elements.");
                })
                .ToArray();

            var result = await resultExisting(
                    testCase: testCase,
                    actualResponse: actualResponse,
                    evaluations: evaluations,
                    latency: TimeSpan.FromMilliseconds(pick.LatencyBaseMs + (i * 30)),
                    usage: new TokenUsage(inputTokenCount: 240, outputTokenCount: 90),
                    existing: new SeedData(Guid.NewGuid(), now, now))
                .AddAsync(cancellationToken);
            results.Add(result);
        }

        return await runExisting(
                group: group,
                endpoint: endpoint,
                sampleIndex: 0,
                status: TestRunStatus.Completed,
                completedAt: now,
                testResults: results,
                existing: new SeedData(Guid.NewGuid(), now, now))
            .AddAsync(cancellationToken);
    }

    /// <summary>
    /// The endpoint-down shape: a group whose single run never produced a result, seeded directly in
    /// the terminal <see cref="TestRunStatus.Failed"/> state — exactly what the anomaly detector's
    /// hard rule looks for, with no Pending/Running window a live-kiosk runner could execute. Kept
    /// out of <see cref="DemoSeedContext.AllRuns"/> so it is neither backdated nor cited as evidence:
    /// it is "today's" incident.
    /// </summary>
    private async Task SeedFailedToneGroupAsync(CancellationToken cancellationToken)
    {
        var suite = RequireSuite("customer-support-tone");
        var now = DateTimeOffset.UtcNow;

        var group = await groupExisting(
                suite,
                status: TestRunStatus.Failed,
                completedAt: now,
                isSystemRun: false,
                optimizationConsideredAt: now,
                scheduleId: null,
                sampleCount: 1,
                existing: new SeedData(Guid.NewGuid(), now, now))
            .AddAsync(cancellationToken);

        await runExisting(
                group: group,
                endpoint: ctx.RequireClaudeEndpoint(),
                sampleIndex: 0,
                status: TestRunStatus.Failed,
                completedAt: now,
                testResults: [],
                existing: new SeedData(Guid.NewGuid(), now, now))
            .AddAsync(cancellationToken);

        ctx.FailedToneGroup = group;
    }

    /// <summary>
    /// Hidden system A/B candidate runs (one per agent that has a validated/invalidated theory), so
    /// theories and proposals can point their A/B evidence at a real run entity. Seeded terminal, so
    /// a live kiosk never re-executes them.
    /// </summary>
    private async Task SeedAbCandidateRunsAsync(CancellationToken cancellationToken)
    {
        var candidates = new (string SuiteKey, Func<DemoSeedContext, IModelEndpoint> SelectEndpoint, double PassRate)[]
        {
            ("customer-support-tone", c => c.RequireGpt54Endpoint(), 0.90),
            ("data-analytics-queries", c => c.RequireGpt54MiniEndpoint(), 0.88),
            // The triage candidate stays on the cheap model: the theory it backs rewrites the
            // system prompt (pinning a taxonomy), it does not switch the endpoint.
            ("email-triage-priority", c => c.RequireGpt54MiniEndpoint(), 0.62),
        };

        foreach (var (suiteKey, selectEndpoint, passRate) in candidates)
        {
            var suite = RequireSuite(suiteKey);
            var group = await SeedCompletedGroupAsync(suite, isSystemRun: true, cancellationToken);
            var run = await SeedCompletedRunAsync(
                suite, group, new EndpointPick(selectEndpoint, passRate), cancellationToken);
            ctx.AbCandidateRunsByAgent[suite.Agent.Id] = run;
        }
    }

    private sealed record SeedData(Guid Id, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt) : IDomainEntityData;
}
