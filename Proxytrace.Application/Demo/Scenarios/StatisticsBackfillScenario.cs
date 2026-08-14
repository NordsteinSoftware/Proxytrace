using System.Net;
using JetBrains.Annotations;
using Nordstein.Core.Common.Random;
using Proxytrace.Domain;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.AgentCall;
using Nordstein.Core.AI.Completions;
using Nordstein.Core.AI.Messages;
using Proxytrace.Domain.ModelEndpoint;
using Proxytrace.Domain.TestResult;
using Proxytrace.Domain.TestRun;
using Proxytrace.Domain.TestRunGroup;
using Nordstein.Core.AI.Tools;

namespace Proxytrace.Application.Demo.Scenarios;

[UsedImplicitly]
internal sealed class StatisticsBackfillScenario : IDemoScenario
{
    private const int WindowDays = 14;
    private const int MinCallsPerDay = 50;
    private const int MaxCallsPerDay = 81;
    private const double ErrorRate = 0.03;
    private const double LatencyTailRate = 0.05;

    // Rare, genuinely extreme calls flagged as outliers (matching what the ingestion-time
    // detector would flag at mean ± 3σ against this backfill's baseline), so the "outliers only"
    // trace filter, the distribution histograms and Tracey's anomaly tools have real data.
    private const double LatencySpikeRate = 0.008;
    private const double TokenSpikeRate = 0.01;
    private const double UncachedShareRate = 0.30;

    // Tool round-trips are the showcase's bread and butter: each profile carries a per-agent share
    // of interactions that go through a two-call tool conversation (see BackfillProfile.ToolRate).
    // Analytics runs at 1.0 — its prompt forbids invented numbers, so every successful answer is
    // grounded in run_sql/get_schema.

    private static readonly int[] DiurnalWeights =
        [2, 1, 1, 1, 1, 2, 4, 7, 9, 10, 10, 9, 8, 9, 10, 10, 9, 7, 5, 4, 3, 3, 2, 2];

    private static readonly (HttpStatusCode Status, string Message)[] ErrorVariants =
    [
        (HttpStatusCode.TooManyRequests, "rate_limit_exceeded"),
        (HttpStatusCode.InternalServerError, "internal_error"),
        (HttpStatusCode.BadGateway, "bad_gateway"),
    ];

    // Plain-text answers only where no tool is needed; anything that requires an order lookup or a
    // return goes through SupportToolStories instead, matching the agent's system prompt.
    private static readonly (string User, string Assistant)[] SupportPool =
    [
        ("My refund hasn't arrived yet.", "Refunds usually settle in 3-5 business days of us receiving the return. Could you share the order number so I can check where yours is?"),
        ("Can you help me change my shipping address?", "Sure — what's the order number?"),
        ("Do you offer international shipping?", "Yes — international shipping is available to 38 countries."),
        ("How do I reset my password?", "Use the 'Forgot password' link on the login page; a reset email arrives within a minute."),
        ("My package shows delivered but it isn't here.", "I'm sorry to hear that — could you send me the order number? I'll check the carrier scan and sort out a replacement."),
        ("What payment methods do you accept?", "We accept all major credit cards, PayPal and Klarna. Bank transfer is available for orders over €500."),
    ];

    // Tool-using support interactions: two agent calls sharing a conversation — the model requests
    // lookup_order/start_return, then answers from the tool result.
    private static readonly ToolStory[] SupportToolStories =
    [
        new(
            id => $"Hi, where is my order #{id}?",
            "lookup_order",
            id => $$"""{"order_id":"{{id}}"}""",
            id => $$"""{"order_id":"{{id}}","status":"in_transit","carrier":"DHL","eta_days":2}""",
            id => $"Order #{id} is in transit with DHL and should arrive within 2 business days. Anything else I can help with?"),
        new(
            id => $"Has order #{id} shipped yet? I ordered last weekend.",
            "lookup_order",
            id => $$"""{"order_id":"{{id}}"}""",
            id => $$"""{"order_id":"{{id}}","status":"processing","warehouse":"Leipzig"}""",
            id => $"Not yet — order #{id} is still being packed at our Leipzig warehouse. Dispatch is expected within 24 hours and the tracking link will arrive by email."),
        new(
            id => $"Order #{id} arrived with a cracked screen. I'd like to return it.",
            "start_return",
            id => $$"""{"order_id":"{{id}}","reason":"damaged"}""",
            id => $$"""{"return_id":"RMA-{{id}}","label_url":"https://shop.example.com/labels/RMA-{{id}}","refund_estimate_days":3}""",
            id => $"Sorry about that! Return RMA-{id} is open for order #{id} — the prepaid label is on its way to your inbox, and the refund lands within 3 business days of us receiving the device."),
    ];

    private static readonly (string User, string Assistant)[] CodeReviewPool =
    [
        ("Review this pull request for null safety.", "Two potential NREs in PaymentProcessor.cs; suggest adding guard clauses."),
        ("Audit this method for SQL injection risk.", "Parameter `userId` is concatenated; switch to a parameterised query."),
        ("Is this implementation thread-safe?", "Field `_cache` is read without synchronisation; consider ConcurrentDictionary."),
        ("Comment on naming consistency.", "Mix of camelCase and snake_case in DTOs; align with project style guide."),
        ("Flag any obvious performance smells.", "Inner loop allocates a new List per iteration; hoist outside the loop."),
        ("Check error handling.", "Bare catch suppresses cancellation; rethrow OperationCanceledException."),
    ];

    // At ToolRate 1.0 every successful analytics interaction goes through AnalyticsToolStories;
    // this pool only supplies the user question for error calls (no response is rendered there).
    private static readonly (string User, string Assistant)[] AnalyticsPool =
    [
        ("How many active users did we have last week?", "Let me run that query."),
        ("Top acquisition channels in May?", "Let me run that query."),
        ("DAU/MAU ratio for last month?", "Let me run that query."),
        ("Revenue split by region?", "Let me run that query."),
        ("Churn rate by plan tier?", "Let me run that query."),
        ("Median order value last quarter?", "Let me run that query."),
    ];

    private static readonly ToolStory[] AnalyticsToolStories =
    [
        new(
            _ => "How many active users did we have last week?",
            "run_sql",
            _ => """{"query":"SELECT COUNT(DISTINCT user_id) FROM events WHERE event_at >= now() - INTERVAL '7 days';"}""",
            n => $$"""{"rows":[{"count":{{n}}}],"row_count":1,"duration_ms":388}""",
            n => $"Active users in the last 7 full days: {n}.\n```sql\nSELECT COUNT(DISTINCT user_id)\nFROM events\nWHERE event_at >= now() - INTERVAL '7 days';\n```"),
        new(
            _ => "How many orders did we take yesterday?",
            "run_sql",
            _ => """{"query":"SELECT COUNT(*) FROM orders WHERE placed_at::date = (now() - INTERVAL '1 day')::date;"}""",
            n => $$"""{"rows":[{"count":{{n}}}],"row_count":1,"duration_ms":214}""",
            n => $"Orders taken yesterday: {n}.\n```sql\nSELECT COUNT(*) FROM orders\nWHERE placed_at::date = (now() - INTERVAL '1 day')::date;\n```"),
        new(
            _ => "What was our total revenue last month?",
            "run_sql",
            _ => """{"query":"SELECT SUM(total) AS revenue FROM orders WHERE placed_at >= date_trunc('month', now()) - INTERVAL '1 month' AND placed_at < date_trunc('month', now());"}""",
            n => $$"""{"rows":[{"revenue":{{n}}.00}],"row_count":1,"duration_ms":492}""",
            n => $"Total revenue last month: €{n}.\n```sql\nSELECT SUM(total) AS revenue FROM orders\nWHERE placed_at >= date_trunc('month', now()) - INTERVAL '1 month'\n  AND placed_at < date_trunc('month', now());\n```"),
        new(
            _ => "Which columns can I segment users by?",
            "get_schema",
            _ => """{"table":"users"}""",
            _ => """{"table":"users","columns":[{"name":"id","type":"bigint"},{"name":"plan","type":"text"},{"name":"channel","type":"text"},{"name":"region","type":"text"},{"name":"created_at","type":"timestamptz"},{"name":"last_active_at","type":"timestamptz"}]}""",
            _ => "The users table segments on: plan, channel, region, plus the created_at/last_active_at timestamps for cohorting. id is the join key to events."),
    ];

    // Billing/plan questions stay in the plain pool — the triage agent has no tool to look those
    // up (that gap is the seeded lookup_customer_plan theory); how-to and known-bug emails go
    // through search_kb via TriageToolStories.
    private static readonly (string User, string Assistant)[] TriagePool =
    [
        ("Subject: API returns 429 for our nightly import since Tuesday.", "Category: Bug. Priority: P2."),
        ("Subject: Please upgrade us to the annual plan.", "Category: Billing. Priority: P3."),
        ("Subject: Invoice PDF shows the wrong company address.", "Category: Billing. Priority: P3."),
        ("Subject: Can we get SSO with Okta?", "Category: Feature Request. Priority: P4."),
        ("Subject: Everything is down again!!", "Category: UI Feedback. Priority: P3."),
    ];

    private static readonly ToolStory[] TriageToolStories =
    [
        new(
            _ => "Subject: Password reset email never arrives.",
            "search_kb",
            _ => """{"query":"password reset email not arriving"}""",
            _ => """{"articles":[{"id":"KB-217","title":"Password reset emails and domain allowlists","url":"https://help.example.com/kb/217"}]}""",
            _ => "Category: Account Access. Priority: P3. Suggested reply: check spam and the domain allowlist (KB-217); an admin can also trigger the reset from the members page."),
        new(
            _ => "Subject: Dashboard loads blank in Safari.",
            "search_kb",
            _ => """{"query":"dashboard blank page Safari"}""",
            _ => """{"articles":[{"id":"KB-334","title":"Blank dashboard in Safari 17","url":"https://help.example.com/kb/334"}]}""",
            _ => "Category: Bug. Priority: P3. Suggested reply: known Safari 17 issue — clearing site data restores the dashboard; permanent fix is rolling out (KB-334)."),
        new(
            _ => "Subject: How do I bulk-invite my whole team?",
            "search_kb",
            _ => """{"query":"bulk invite team members CSV"}""",
            _ => """{"articles":[{"id":"KB-089","title":"Importing members from CSV","url":"https://help.example.com/kb/089"}]}""",
            _ => "Category: How-To. Priority: P4. Suggested reply: Settings → Members → Import CSV handles up to 500 invites at once (KB-089)."),
    ];

    // HighTokens outliers carry content that visibly justifies the token count — a pasted wall of
    // text in the request — so opening a flagged trace never shows a two-line chat labelled
    // "high token count".
    private static readonly SpikeSample SupportSpike = new(
        "I've been going back and forth with your team for three weeks about order #58121 and I'm done repeating myself. "
        + "Pasting the ENTIRE email thread below so you finally have the full context:\n\n"
        + string.Join("\n", Enumerable.Range(1, 90).Select(i =>
            $"> [message {i}] Re: order #58121 — delivery rescheduled again, promised callback never happened, partial refund of €12.40 discussed but not issued.")),
        "Thanks for the full history — here is where order #58121 actually stands: the delivery was rescheduled twice by the carrier, "
        + "the €12.40 partial refund agreed in the middle of the thread was never issued, and the replacement lamp shade was never dispatched. "
        + "I've escalated this to our fulfillment lead with the full thread attached — you'll receive the refund confirmation and the "
        + "replacement tracking link by email today, plus a €10 goodwill voucher for the runaround.",
        MinIn: 2600, MaxIn: 4200, MinOut: 170, MaxOut: 300);

    private static readonly SpikeSample CodeReviewSpike = new(
        "Please review this whole feature branch in one go (full diff below):\n\n"
        + string.Join("\n", Enumerable.Range(1, 150).Select(i =>
            $"+    public async Task<OrderDto> GetOrderVariant_{i:D3}(Guid id) => await orderQuery.LoadAsync(id).ConfigureAwait(false);")),
        "This diff adds 150 near-identical query methods — collapse them into one method taking the variant as a parameter. "
        + "Concrete issues: (1) every method awaits with ConfigureAwait(false) but the callers rely on the request culture, so localized "
        + "formatting silently breaks; (2) none of the methods pass a CancellationToken, so slow queries survive request aborts; "
        + "(3) `LoadAsync` throws on missing rows while callers expect null — each variant is a latent 500. "
        + "A diff this size also exceeds what one review pass can meaningfully cover; split it next time.",
        MinIn: 3800, MaxIn: 5200, MinOut: 320, MaxOut: 520);

    private static readonly SpikeSample AnalyticsSpike = new(
        "Here is our full warehouse schema, how many orders did we take yesterday?\n\n"
        + string.Join("\n", Enumerable.Range(1, 120).Select(i =>
            $"CREATE TABLE shard_{i:D3}_orders (id BIGSERIAL PRIMARY KEY, user_id BIGINT NOT NULL, total NUMERIC(10,2) NOT NULL, placed_at TIMESTAMPTZ NOT NULL DEFAULT now());")),
        "Your question only needs the orders tables, not the full 120-table schema — pasting it on every request is what makes "
        + "these calls expensive. Here's the query I'd run:\n```sql\nSELECT COUNT(*) FROM all_orders\nWHERE placed_at::date = (now() - INTERVAL '1 day')::date;\n```\nWant me to execute it?",
        MinIn: 3000, MaxIn: 5200, MinOut: 140, MaxOut: 260);

    private static readonly SpikeSample TriageSpike = new(
        "Subject: FWD: FWD: RE: unresolved ticket — forwarding our complete internal thread so you can see everything:\n\n"
        + string.Join("\n", Enumerable.Range(1, 80).Select(i =>
            $"> [reply {i}] RE: intermittent 502s on the reporting API since the last maintenance window; retried nightly import, same result.")),
        "Category: Bug. Priority: P2.",
        MinIn: 2400, MaxIn: 3600, MinOut: 18, MaxOut: 34);

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
            new BackfillProfile(
                ctx.RequireCustomerSupportAgent(),
                [new(ctx.RequireGpt54Endpoint(), 0.70), new(ctx.RequireClaudeEndpoint(), 0.30)],
                SupportPool,
                SupportSpike,
                SupportToolStories,
                ToolRate: 0.35),
            new BackfillProfile(
                ctx.RequireCodeReviewAgent(),
                [new(ctx.RequireClaudeEndpoint(), 0.80), new(ctx.RequireGpt54MiniEndpoint(), 0.20)],
                CodeReviewPool,
                CodeReviewSpike,
                [],
                ToolRate: 0),
            new BackfillProfile(
                ctx.RequireDataAnalyticsAgent(),
                [new(ctx.RequireGpt54Endpoint(), 0.60), new(ctx.RequireGpt54MiniEndpoint(), 0.40)],
                AnalyticsPool,
                AnalyticsSpike,
                AnalyticsToolStories,
                ToolRate: 1.0),
            new BackfillProfile(
                ctx.RequireEmailTriageAgent(),
                [new(ctx.RequireGpt54MiniEndpoint(), 1.00)],
                TriagePool,
                TriageSpike,
                TriageToolStories,
                ToolRate: 0.35),
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
        for (int day = 0; day < WindowDays; day++)
        {
            var dayStart = windowStart.AddDays(day);
            int count = random.Int(MinCallsPerDay, MaxCallsPerDay);
            for (int i = 0; i < count; i++)
            {
                var createdAt = SampleTimestamp(dayStart);
                if (createdAt > now)
                {
                    continue;
                }

                var endpoint = PickWeighted(profile.EndpointMix);
                bool isError = random.Double() < ErrorRate;
                bool isSpike = !isError && random.Double() < TokenSpikeRate;

                if (!isError && !isSpike && profile.ToolStories.Length > 0 && random.Double() < profile.ToolRate)
                {
                    CollectToolConversation(profile, endpoint, createdAt, calls);
                    continue;
                }

                var flags = OutlierFlags.None;
                string userText;
                string assistantText;
                ulong inTok;
                ulong outTok;
                ulong cachedIn;

                if (isSpike)
                {
                    // A conversation that ballooned: far above the ~325-token mean of the window,
                    // with a pasted wall of text in the request to match the numbers. The one-off
                    // paste also misses the prompt cache.
                    var spike = profile.Spike;
                    userText = spike.User;
                    assistantText = spike.Assistant;
                    inTok = (ulong)random.Int(spike.MinIn, spike.MaxIn + 1);
                    outTok = (ulong)random.Int(spike.MinOut, spike.MaxOut + 1);
                    cachedIn = 0;
                    flags |= OutlierFlags.HighTokens;
                }
                else
                {
                    (userText, assistantText) = random.Any(profile.Pool);
                    inTok = (ulong)random.Int(200, 451);
                    outTok = (ulong)random.Int(40, 221);

                    // Most calls hit the prompt cache for part of the input; ~30% miss entirely,
                    // giving the cache-hit KPI and distribution a realistic spread.
                    cachedIn = random.Double() < UncachedShareRate
                        ? 0UL
                        : (ulong)(inTok * random.Double(0.3, 0.8));
                }

                int latencyMs;
                if (random.Double() < LatencySpikeRate)
                {
                    latencyMs = random.Int(4200, 9001);
                    flags |= OutlierFlags.HighLatency;
                }
                else if (flags.HasFlag(OutlierFlags.HighTokens))
                {
                    // A big context takes longer, but stays inside the unflagged latency tail.
                    latencyMs = random.Int(1600, 3401);
                }
                else
                {
                    latencyMs = random.Double() < LatencyTailRate
                        ? random.Int(1500, 3501)
                        : random.Int(400, 901);
                }

                var systemMsg = profile.Agent.CreateSystemMessage();
                var userMsg = new UserMessage([Content.FromText(userText)]);
                var request = new Conversation([systemMsg, userMsg]);

                ICompletion? response = isError
                    ? null
                    : completionFactory(
                        new AssistantMessage([Content.FromText(assistantText)], []),
                        new TokenUsage(inTok, outTok, cachedIn),
                        TimeSpan.FromMilliseconds(latencyMs));

                (HttpStatusCode status, string? errorMessage, string? finishReason) = isError
                    ? BuildError()
                    : (HttpStatusCode.OK, (string?)null, "stop");

                calls.Add(BuildBackdatedCall(
                    profile.Agent, endpoint, request, response,
                    status, errorMessage, finishReason,
                    createdAt, conversationId: null,
                    outlierFlags: isError ? OutlierFlags.None : flags));
            }
        }
    }

    private void CollectToolConversation(
        BackfillProfile profile,
        IModelEndpoint endpoint,
        DateTimeOffset createdAt,
        List<IAgentCall> calls)
    {
        var story = random.Any(profile.ToolStories);
        string orderId = random.Int(10000, 100000).ToString();
        var conversationId = Guid.NewGuid();
        var system = profile.Agent.CreateSystemMessage();
        var user = new UserMessage([Content.FromText(story.User(orderId))]);

        var toolRequest = new ToolRequest(
            id: $"call_{story.ToolName}_{orderId}",
            name: story.ToolName,
            arguments: story.Arguments(orderId));
        var assistantToolMsg = new AssistantMessage([], [toolRequest]);

        ulong toolTurnIn = (ulong)random.Int(230, 331);
        calls.Add(BuildBackdatedCall(
            profile.Agent, endpoint,
            request: new Conversation([system, user]),
            response: completionFactory(
                assistantToolMsg,
                new TokenUsage(toolTurnIn, (ulong)random.Int(24, 46), CachedShare(toolTurnIn)),
                TimeSpan.FromMilliseconds(random.Int(380, 721))),
            httpStatus: HttpStatusCode.OK,
            errorMessage: null,
            finishReason: "tool_calls",
            createdAt: createdAt,
            conversationId: conversationId,
            outlierFlags: OutlierFlags.None));

        var toolMsg = new ToolMessage(new ToolResponse(
            toolRequest, [Content.FromText(story.ToolResult(orderId))]));
        var finalAssistant = new AssistantMessage([Content.FromText(story.Final(orderId))], []);

        ulong finalTurnIn = toolTurnIn + (ulong)random.Int(70, 141);
        calls.Add(BuildBackdatedCall(
            profile.Agent, endpoint,
            request: new Conversation([system, user, assistantToolMsg, toolMsg]),
            response: completionFactory(
                finalAssistant,
                new TokenUsage(finalTurnIn, (ulong)random.Int(45, 91), CachedShare(finalTurnIn)),
                TimeSpan.FromMilliseconds(random.Int(430, 821))),
            httpStatus: HttpStatusCode.OK,
            errorMessage: null,
            finishReason: "stop",
            createdAt: createdAt.AddSeconds(random.Int(2, 8)),
            conversationId: conversationId,
            outlierFlags: OutlierFlags.None));
    }

    private ulong CachedShare(ulong inTok)
        => random.Double() < UncachedShareRate
            ? 0UL
            : (ulong)(inTok * random.Double(0.3, 0.8));

    private IAgentCall BuildBackdatedCall(
        IAgent agent,
        IModelEndpoint endpoint,
        Conversation request,
        ICompletion? response,
        HttpStatusCode httpStatus,
        string? errorMessage,
        string? finishReason,
        DateTimeOffset createdAt,
        Guid? conversationId,
        OutlierFlags outlierFlags)
        => agentCallExisting(
            agent: agent,
            version: agent.CurrentVersion,
            endpoint: endpoint,
            request: request,
            response: response,
            httpStatus: httpStatus,
            finishReason: finishReason,
            errorMessage: errorMessage,
            modelParameters: paramsFactory(temperature: 0.3),
            existing: new BackdatedData(Guid.NewGuid(), createdAt, createdAt),
            conversationId: conversationId,
            outlierFlags: outlierFlags);

    private (HttpStatusCode Status, string? ErrorMessage, string? FinishReason) BuildError()
    {
        var variant = random.Any(ErrorVariants);
        return (variant.Status, variant.Message, null);
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
        int total = DiurnalWeights.Sum();
        int pick = random.Int(0, total);
        int acc = 0;
        for (int h = 0; h < DiurnalWeights.Length; h++)
        {
            acc += DiurnalWeights[h];
            if (pick < acc)
            {
                return h;
            }
        }
        return DiurnalWeights.Length - 1;
    }

    private IModelEndpoint PickWeighted(IReadOnlyList<EndpointWeight> mix)
    {
        double pick = random.Double();
        double acc = 0;
        foreach (var w in mix)
        {
            acc += w.Weight;
            if (pick <= acc)
            {
                return w.Endpoint;
            }
        }
        return mix[^1].Endpoint;
    }

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

    private sealed record EndpointWeight(IModelEndpoint Endpoint, double Weight);

    private sealed record BackfillProfile(
        IAgent Agent,
        IReadOnlyList<EndpointWeight> EndpointMix,
        (string User, string Assistant)[] Pool,
        SpikeSample Spike,
        ToolStory[] ToolStories,
        double ToolRate);

    /// <summary>
    /// Content for a HighTokens outlier: a request with a pasted wall of text and token ranges
    /// that match it, so the flag is visibly justified when the trace is opened.
    /// </summary>
    private sealed record SpikeSample(
        string User,
        string Assistant,
        int MinIn,
        int MaxIn,
        int MinOut,
        int MaxOut);

    /// <summary>
    /// A two-call tool round-trip (request tool → answer from tool result), templated on a random
    /// order id.
    /// </summary>
    private sealed record ToolStory(
        Func<string, string> User,
        string ToolName,
        Func<string, string> Arguments,
        Func<string, string> ToolResult,
        Func<string, string> Final);
}
