using System.Net;

namespace Proxytrace.Application.Demo.Internal;

/// <summary>
/// The shared content and shape of the kiosk's simulated agent traffic: per-agent conversation
/// pools, tool round-trip stories, token/volume shapes, and the rates that govern errors, latency
/// tails and outlier spikes. Consumed by <c>StatisticsBackfillScenario</c> (the 14-day historical
/// backfill) and <c>KioskLiveTrafficService</c> (the continuous live feed), so both paint the same
/// business out of the same material and the live traffic is statistically indistinguishable from
/// the history it extends.
/// </summary>
internal static class DemoTrafficCatalog
{
    internal const double ErrorRate = 0.03;
    internal const double LatencyTailRate = 0.05;

    // Rare, genuinely extreme calls flagged as outliers (matching what the ingestion-time
    // detector would flag at mean ± 3σ against the backfill's baseline), so the "outliers only"
    // trace filter, the distribution histograms and Tracey's anomaly tools have real data.
    internal const double LatencySpikeRate = 0.008;
    internal const double TokenSpikeRate = 0.01;
    internal const double UncachedShareRate = 0.30;

    internal static readonly int[] DiurnalWeights =
        [2, 1, 1, 1, 1, 2, 4, 7, 9, 10, 10, 9, 8, 9, 10, 10, 9, 7, 5, 4, 3, 3, 2, 2];

    internal static readonly (HttpStatusCode Status, string Message)[] ErrorVariants =
    [
        (HttpStatusCode.TooManyRequests, "rate_limit_exceeded"),
        (HttpStatusCode.InternalServerError, "internal_error"),
        (HttpStatusCode.BadGateway, "bad_gateway"),
    ];

    /// <summary>
    /// The seeded demo endpoints a profile's traffic is spread across. Consumers resolve these to
    /// the actual <c>IModelEndpoint</c> entities via <c>DemoSeedContext</c>.
    /// </summary>
    internal enum DemoEndpointKey
    {
        Gpt54,
        Gpt54Mini,
        ClaudeSonnet,
    }

    /// <summary>
    /// One endpoint's share of a profile's traffic; shares within a mix sum to 1.
    /// </summary>
    internal sealed record EndpointShare(DemoEndpointKey Endpoint, double Weight);

    /// <summary>
    /// Sampled token ranges for one call. Input counts are sized like a production agent's context
    /// (system prompt + conversation history + retrieved data), not a bare two-line chat — the
    /// token volumes and the resulting cost cards are part of what the kiosk showcases.
    /// </summary>
    internal sealed record TokenShape(int MinIn, int MaxIn, int MinOut, int MaxOut);

    /// <summary>
    /// Content for a HighTokens outlier: a request with a pasted wall of text and token ranges
    /// that match it, so the flag is visibly justified when the trace is opened.
    /// </summary>
    internal sealed record SpikeSample(
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
    internal sealed record ToolStory(
        Func<string, string> User,
        string ToolName,
        Func<string, string> Arguments,
        Func<string, string> ToolResult,
        Func<string, string> Final);

    /// <summary>
    /// Everything that characterizes one demo agent's traffic: what its conversations say, how
    /// often they go through a tool round-trip, how many tokens they carry, and how many
    /// interactions it handles per day. Daily volumes are per-agent because a real fleet is
    /// lopsided — triage and support churn through hundreds of cheap/medium calls while code
    /// review sees a few dozen expensive ones.
    /// </summary>
    internal sealed record AgentTraffic(
        (string User, string Assistant)[] Pool,
        SpikeSample Spike,
        ToolStory[] ToolStories,
        double ToolRate,
        TokenShape Text,
        TokenShape ToolTurn,
        int MinCallsPerDay,
        int MaxCallsPerDay,
        EndpointShare[] EndpointMix);

    // Plain-text answers only where no tool is needed; anything that requires an order lookup or a
    // return goes through the support ToolStories instead, matching the agent's system prompt.
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

    // At ToolRate 1.0 every successful analytics interaction goes through the tool stories; this
    // pool only supplies the user question for error calls (no response is rendered there).
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
    // through search_kb via the triage ToolStories.
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
    // "high token count". Spike ranges sit far above each profile's baseline mean + 3σ so the
    // seeded flags match what the ingestion-time detector would compute.
    private static readonly SpikeSample SupportSpike = new(
        "I've been going back and forth with your team for three weeks about order #58121 and I'm done repeating myself. "
        + "Pasting the ENTIRE email thread below so you finally have the full context:\n\n"
        + string.Join("\n", Enumerable.Range(1, 90).Select(i =>
            $"> [message {i}] Re: order #58121 — delivery rescheduled again, promised callback never happened, partial refund of €12.40 discussed but not issued.")),
        "Thanks for the full history — here is where order #58121 actually stands: the delivery was rescheduled twice by the carrier, "
        + "the €12.40 partial refund agreed in the middle of the thread was never issued, and the replacement lamp shade was never dispatched. "
        + "I've escalated this to our fulfillment lead with the full thread attached — you'll receive the refund confirmation and the "
        + "replacement tracking link by email today, plus a €10 goodwill voucher for the runaround.",
        MinIn: 7000, MaxIn: 11000, MinOut: 300, MaxOut: 520);

    private static readonly SpikeSample CodeReviewSpike = new(
        "Please review this whole feature branch in one go (full diff below):\n\n"
        + string.Join("\n", Enumerable.Range(1, 150).Select(i =>
            $"+    public async Task<OrderDto> GetOrderVariant_{i:D3}(Guid id) => await orderQuery.LoadAsync(id).ConfigureAwait(false);")),
        "This diff adds 150 near-identical query methods — collapse them into one method taking the variant as a parameter. "
        + "Concrete issues: (1) every method awaits with ConfigureAwait(false) but the callers rely on the request culture, so localized "
        + "formatting silently breaks; (2) none of the methods pass a CancellationToken, so slow queries survive request aborts; "
        + "(3) `LoadAsync` throws on missing rows while callers expect null — each variant is a latent 500. "
        + "A diff this size also exceeds what one review pass can meaningfully cover; split it next time.",
        MinIn: 9000, MaxIn: 14000, MinOut: 450, MaxOut: 800);

    private static readonly SpikeSample AnalyticsSpike = new(
        "Here is our full warehouse schema, how many orders did we take yesterday?\n\n"
        + string.Join("\n", Enumerable.Range(1, 120).Select(i =>
            $"CREATE TABLE shard_{i:D3}_orders (id BIGSERIAL PRIMARY KEY, user_id BIGINT NOT NULL, total NUMERIC(10,2) NOT NULL, placed_at TIMESTAMPTZ NOT NULL DEFAULT now());")),
        "Your question only needs the orders tables, not the full 120-table schema — pasting it on every request is what makes "
        + "these calls expensive. Here's the query I'd run:\n```sql\nSELECT COUNT(*) FROM all_orders\nWHERE placed_at::date = (now() - INTERVAL '1 day')::date;\n```\nWant me to execute it?",
        MinIn: 7000, MaxIn: 12000, MinOut: 160, MaxOut: 320);

    private static readonly SpikeSample TriageSpike = new(
        "Subject: FWD: FWD: RE: unresolved ticket — forwarding our complete internal thread so you can see everything:\n\n"
        + string.Join("\n", Enumerable.Range(1, 80).Select(i =>
            $"> [reply {i}] RE: intermittent 502s on the reporting API since the last maintenance window; retried nightly import, same result.")),
        "Category: Bug. Priority: P2.",
        MinIn: 5200, MaxIn: 8000, MinOut: 18, MaxOut: 40);

    // Daily volumes and token shapes sized like a mid-size business running these agents in
    // production: support and triage churn through hundreds of interactions a day, analytics is a
    // steady internal tool, code review sees fewer but much heavier calls. Together (~1,300–1,700
    // interactions/day at these token weights) the seeded window carries a daily LLM spend in the
    // tens of euros — dashboard numbers that read like a real deployment instead of a toy.
    // Tool round-trips are the showcase's bread and butter (ToolRate); Analytics runs at 1.0 — its
    // prompt forbids invented numbers, so every successful answer is grounded in run_sql/get_schema.

    internal static readonly AgentTraffic Support = new(
        SupportPool,
        SupportSpike,
        SupportToolStories,
        ToolRate: 0.35,
        Text: new TokenShape(MinIn: 1400, MaxIn: 3600, MinOut: 180, MaxOut: 600),
        ToolTurn: new TokenShape(MinIn: 1000, MaxIn: 1500, MinOut: 26, MaxOut: 60),
        MinCallsPerDay: 420,
        MaxCallsPerDay: 560,
        EndpointMix: [new(DemoEndpointKey.Gpt54, 0.70), new(DemoEndpointKey.ClaudeSonnet, 0.30)]);

    internal static readonly AgentTraffic CodeReview = new(
        CodeReviewPool,
        CodeReviewSpike,
        ToolStories: [],
        ToolRate: 0,
        Text: new TokenShape(MinIn: 2200, MaxIn: 4800, MinOut: 350, MaxOut: 900),
        ToolTurn: new TokenShape(MinIn: 2200, MaxIn: 4800, MinOut: 350, MaxOut: 900),
        MinCallsPerDay: 100,
        MaxCallsPerDay: 150,
        EndpointMix: [new(DemoEndpointKey.ClaudeSonnet, 0.80), new(DemoEndpointKey.Gpt54Mini, 0.20)]);

    internal static readonly AgentTraffic Analytics = new(
        AnalyticsPool,
        AnalyticsSpike,
        AnalyticsToolStories,
        ToolRate: 1.0,
        Text: new TokenShape(MinIn: 900, MaxIn: 1600, MinOut: 60, MaxOut: 160),
        ToolTurn: new TokenShape(MinIn: 900, MaxIn: 1600, MinOut: 40, MaxOut: 90),
        MinCallsPerDay: 190,
        MaxCallsPerDay: 260,
        EndpointMix: [new(DemoEndpointKey.Gpt54, 0.60), new(DemoEndpointKey.Gpt54Mini, 0.40)]);

    internal static readonly AgentTraffic Triage = new(
        TriagePool,
        TriageSpike,
        TriageToolStories,
        ToolRate: 0.35,
        Text: new TokenShape(MinIn: 420, MaxIn: 980, MinOut: 30, MaxOut: 120),
        ToolTurn: new TokenShape(MinIn: 380, MaxIn: 700, MinOut: 22, MaxOut: 48),
        MinCallsPerDay: 560,
        MaxCallsPerDay: 760,
        EndpointMix: [new(DemoEndpointKey.Gpt54Mini, 1.00)]);
}
