using System.Net;
using Proxytrace.Domain;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.AgentCall;
using Nordstein.Core.AI.Completions;
using Proxytrace.Domain.Kiosk;
using Nordstein.Core.AI.Messages;
using Proxytrace.Domain.Model;
using Proxytrace.Domain.ModelEndpoint;
using Proxytrace.Domain.ModelProvider;
using Proxytrace.Domain.Project;
using Nordstein.Core.AI.Prompts;
using Proxytrace.Domain.Prompt;
using Nordstein.Core.AI.Tools;
using Proxytrace.Domain.User;

// ReSharper disable InconsistentNaming

namespace Proxytrace.Application.Demo.Scenarios;

internal sealed class CoreSeedScenario : IDemoScenario
{
    private readonly KioskOptions kiosk;
    private readonly KioskEndpointOptions kioskEndpoint;
    private readonly DemoSeedContext ctx;
    private readonly IUser.CreateNew userFactory;
    private readonly IModelProvider.CreateNew providerFactory;
    private readonly IModel.CreateNew modelFactory;
    private readonly IModelEndpoint.CreateNew endpointFactory;
    private readonly IRepository<IModelProvider> providers;
    private readonly IRepository<IModel> models;
    private readonly IRepository<IModelEndpoint> endpoints;
    private readonly IProject.CreateNew projectFactory;
    private readonly IRepository<IProject> projects;
    private readonly IPromptTemplate.Create promptFactory;
    private readonly IModelParameters.Create paramsFactory;
    private readonly IAgent.CreateNew agentFactory;
    private readonly IAgentCall.CreateNew agentCallFactory;
    private readonly ICompletion.Create completionFactory;

    public CoreSeedScenario(
        KioskOptions kiosk,
        KioskEndpointOptions kioskEndpoint,
        DemoSeedContext ctx,
        IUser.CreateNew userFactory,
        IModelProvider.CreateNew providerFactory,
        IModel.CreateNew modelFactory,
        IModelEndpoint.CreateNew endpointFactory,
        IRepository<IModelProvider> providers,
        IRepository<IModel> models,
        IRepository<IModelEndpoint> endpoints,
        IProject.CreateNew projectFactory,
        IRepository<IProject> projects,
        IPromptTemplate.Create promptFactory,
        IModelParameters.Create paramsFactory,
        IAgent.CreateNew agentFactory,
        IAgentCall.CreateNew agentCallFactory,
        ICompletion.Create completionFactory)
    {
        this.kiosk = kiosk;
        this.kioskEndpoint = kioskEndpoint;
        this.ctx = ctx;
        this.userFactory = userFactory;
        this.providerFactory = providerFactory;
        this.modelFactory = modelFactory;
        this.endpointFactory = endpointFactory;
        this.providers = providers;
        this.models = models;
        this.endpoints = endpoints;
        this.projectFactory = projectFactory;
        this.projects = projects;
        this.promptFactory = promptFactory;
        this.paramsFactory = paramsFactory;
        this.agentFactory = agentFactory;
        this.agentCallFactory = agentCallFactory;
        this.completionFactory = completionFactory;
    }

    public int Order => 0;

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        // Admin, not Member: the kiosk runs on a perpetual Enterprise override license so it can
        // showcase the whole product, and several surfaces (cost budgets, settings) are gated on
        // the Admin role on top of that license. A Member demo user would see those features
        // permanently locked, which reads as a missing feature rather than a demo boundary. What
        // an interactive kiosk visitor may actually change is bounded by KioskReadOnlyMiddleware,
        // not by this role — and an interactive kiosk is documented as a single-user/private demo.
        var demoUser = await userFactory(
            kiosk.DemoUserEmail,
            externalSubject: null,
            passwordHash: "kiosk-no-login",
            role: UserRole.Admin).AddAsync(cancellationToken);

        var openAiProvider = await providers.AddAsync(providerFactory(
            "OpenAI (demo)",
            new Uri("https://api.openai.com/v1"),
            "DEMO-NO-KEY",
            ModelProviderKind.OpenAi), cancellationToken);

        var anthropicProvider = await providers.AddAsync(providerFactory(
            "Anthropic (demo)",
            new Uri("https://api.anthropic.com/v1"),
            "DEMO-NO-KEY",
            ModelProviderKind.OpenAiCompatible), cancellationToken);

        var gpt54 = await models.AddAsync(modelFactory("gpt-5.4"), cancellationToken);
        var gpt54Mini = await models.AddAsync(modelFactory("gpt-5.4-mini"), cancellationToken);
        var claudeSonnet = await models.AddAsync(modelFactory("claude-sonnet-4.5"), cancellationToken);

        // Prices are EUR per 1M tokens (ModelEndpoint.CalculateCost divides by 1M). gpt-5.4 is the
        // showcase's premium flagship, priced in gpt-4.5 territory so the seeded cost cards show
        // meaningful amounts at the demo's modest call volumes.
        var gpt54Endpoint = await endpoints.AddAsync(endpointFactory(gpt54, openAiProvider,
            inputTokenCost: 15.00m, outputTokenCost: 60.00m, cachedInputTokenCost: 1.50m), cancellationToken);
        var gpt54MiniEndpoint = await endpoints.AddAsync(endpointFactory(gpt54Mini, openAiProvider,
            inputTokenCost: 0.25m, outputTokenCost: 2.00m, cachedInputTokenCost: 0.025m), cancellationToken);
        var claudeEndpoint = await endpoints.AddAsync(endpointFactory(claudeSonnet, anthropicProvider,
            inputTokenCost: 3.00m, outputTokenCost: 15.00m, cachedInputTokenCost: 0.30m), cancellationToken);

        // The demo (DEMO-NO-KEY) endpoints above back the seeded historical traces, stats and
        // proposals (consumed by later scenarios via DemoSeedContext) and stay in place. When a real
        // endpoint is configured via Kiosk:Endpoint, create it and route the project's system endpoint
        // and the demo agents through it, so Tracey chat and test runs hit a real LLM.
        var systemEndpoint = gpt54MiniEndpoint;
        var supportEndpoint = gpt54Endpoint;
        var reviewEndpoint = claudeEndpoint;
        var analyticsEndpoint = gpt54Endpoint;
        var triageEndpoint = gpt54MiniEndpoint;

        if (kioskEndpoint.IsConfigured)
        {
            var resolved = kioskEndpoint.Resolve();
            var realProvider = await providers.AddAsync(providerFactory(
                resolved.ProviderName,
                resolved.BaseUrl,
                resolved.ApiKey,
                resolved.Kind), cancellationToken);
            var realModel = await models.AddAsync(modelFactory(resolved.Model), cancellationToken);
            // Cost display is part of the kiosk showcase, so when the operator does not configure
            // prices, fall back to a small-model rate (EUR per 1M tokens) instead of leaving the
            // endpoint unpriced (which would render every live trace's cost as zero).
            var realEndpoint = await endpoints.AddAsync(endpointFactory(
                realModel, realProvider,
                resolved.InputTokenCost ?? 0.15m,
                resolved.OutputTokenCost ?? 0.60m,
                cachedInputTokenCost: null), cancellationToken);

            systemEndpoint = realEndpoint;
            supportEndpoint = realEndpoint;
            reviewEndpoint = realEndpoint;
            analyticsEndpoint = realEndpoint;
            triageEndpoint = realEndpoint;

            // Exposed for DemoApiKeySeedScenario: the seeded demo ingestion key points at this real
            // provider so the in-process kiosk proxy forwards to the live upstream.
            ctx.KioskLiveProvider = realProvider;
        }

        var project = await projects.AddAsync(projectFactory(
            "Showcase Project",
            systemEndpoint,
            [demoUser]), cancellationToken);

        var lookupOrderTool = new ToolSpecification(
            name: "lookup_order",
            description: "Look up the status of a customer order by its numeric id.",
            arguments: ToolArguments.FromJsonSchema(
                """{"type":"object","properties":{"order_id":{"type":"string","description":"The order id, e.g. 12831"}},"required":["order_id"]}"""));

        var startReturnTool = new ToolSpecification(
            name: "start_return",
            description: "Open a return for a previously delivered order.",
            arguments: ToolArguments.FromJsonSchema(
                """{"type":"object","properties":{"order_id":{"type":"string"},"reason":{"type":"string","enum":["damaged","wrong_item","no_longer_needed"]}},"required":["order_id","reason"]}"""));

        var issueRefundTool = new ToolSpecification(
            name: "issue_refund",
            description: "Issue a full or partial refund for an order to the customer's original payment method.",
            arguments: ToolArguments.FromJsonSchema(
                """{"type":"object","properties":{"order_id":{"type":"string"},"percent":{"type":"integer","minimum":1,"maximum":100},"reason":{"type":"string"}},"required":["order_id","percent"]}"""));

        var supportAgent = await agentFactory(
            "Customer Support Agent",
            promptFactory("support-system",
                "You are a friendly, concise customer-support agent for an e-commerce store. "
                + "Always acknowledge the issue, propose a clear next step, and close politely. "
                + "Use the `lookup_order` and `start_return` tools when a customer references an order id, "
                + "and `issue_refund` to process approved refunds."),
            tools: [lookupOrderTool, startReturnTool, issueRefundTool],
            endpoint: supportEndpoint,
            project: project,
            modelParameters: paramsFactory(temperature: 0.3),
            isSystemAgent: false).AddAsync(cancellationToken);

        var codeReviewAgent = await agentFactory(
            "Code Review Agent",
            promptFactory("code-review-system",
                "You are a senior software engineer reviewing pull requests. "
                + "Identify correctness, security, and clarity issues. Be specific, cite line numbers."),
            tools: [],
            endpoint: reviewEndpoint,
            project: project,
            modelParameters: paramsFactory(temperature: 0.2),
            isSystemAgent: false).AddAsync(cancellationToken);

        var runSqlTool = new ToolSpecification(
            name: "run_sql",
            description: "Execute a read-only SQL query against the analytics warehouse and return the result rows.",
            arguments: ToolArguments.FromJsonSchema(
                """{"type":"object","properties":{"query":{"type":"string","description":"The SQL query to execute"}},"required":["query"]}"""));

        var getSchemaTool = new ToolSpecification(
            name: "get_schema",
            description: "List the columns and types of a warehouse table.",
            arguments: ToolArguments.FromJsonSchema(
                """{"type":"object","properties":{"table":{"type":"string","description":"The table name, e.g. events"}},"required":["table"]}"""));

        var analyticsAgent = await agentFactory(
            "Data Analytics Agent",
            promptFactory("analytics-system",
                "You are a data analyst. Use the `get_schema` tool to inspect table structures and the "
                + "`run_sql` tool to execute your query — never invent numbers. "
                + "Answer with a short summary and the SQL you ran."),
            tools: [runSqlTool, getSchemaTool],
            endpoint: analyticsEndpoint,
            project: project,
            modelParameters: paramsFactory(temperature: 0.1),
            isSystemAgent: false).AddAsync(cancellationToken);

        var searchKbTool = new ToolSpecification(
            name: "search_kb",
            description: "Search the internal knowledge base for troubleshooting and how-to articles.",
            arguments: ToolArguments.FromJsonSchema(
                """{"type":"object","properties":{"query":{"type":"string","description":"Search terms, e.g. 'password reset email'"}},"required":["query"]}"""));

        // Deliberately defective agent: the underspecified prompt (no taxonomy, no priority
        // definitions), the high temperature and the missing plan-lookup tool produce the
        // misclassifications, fabrications and inconsistency the optimization loop then
        // discovers — this is the kiosk's "watch Proxytrace find and fix it" storyline. It has a
        // knowledge-base tool (so how-to replies are grounded), but nothing to look up plans or
        // billing — exactly the gap the seeded lookup_customer_plan theory proposes to close.
        var triageAgent = await agentFactory(
            "Email Triage Agent",
            promptFactory("email-triage-system",
                "You are an email triage assistant for a SaaS company. "
                + "Read the customer email and reply with a category and a priority. "
                + "Use the `search_kb` tool to find help articles when suggesting a reply."),
            tools: [searchKbTool],
            endpoint: triageEndpoint,
            project: project,
            modelParameters: paramsFactory(temperature: 0.9),
            isSystemAgent: false).AddAsync(cancellationToken);

        // Text-only samples deliberately need no tool: order-specific questions go through the
        // tool-call conversations below so the traces match the agent's system prompt.
        var supportSamples = new (string user, string assistant, ulong inTok, ulong outTok, int latencyMs)[]
        {
            ("What's the warranty on kitchen appliances?",
                "All kitchen appliances come with a 2-year manufacturer warranty covering defects from normal use. Just contact us with your order confirmation and we'll handle the claim.",
                214, 72, 545),
            ("I want to return a defective blender, what do I do?",
                "Got it — sorry the blender arrived defective. You can start a return at /returns, select 'Damaged on arrival', and we'll cover the shipping label. You'll see a refund within 3 business days of us receiving it.",
                289, 102, 740),
            ("Do you ship to Germany?",
                "Yes, we ship to Germany. Standard delivery is 5–7 business days, express is 2–3. Duties and VAT are calculated at checkout.",
                204, 64, 510),
            ("My discount code FALL20 isn't working at checkout.",
                "FALL20 expired on 2026-04-30. The current code is SPRING15 for 15% off. Want me to apply it to your cart?",
                256, 71, 612),
            ("Can I pay with PayPal?",
                "Yes — we accept PayPal, all major credit cards and Klarna. You can pick the payment method on the last step of checkout.",
                198, 52, 470),
            ("Your website crashed when I tried to pay.",
                "Apologies for that — could you share the browser and the error message you saw? In the meantime your cart is saved, and you can retry checkout at any time.",
                221, 88, 654),
        };

        var reviewSamples = new (string user, string assistant, ulong inTok, ulong outTok, int latencyMs)[]
        {
            ("Please review this diff: added a global mutable cache to UserService.",
                "Concern: a static mutable cache in UserService introduces a process-wide race and breaks scoped DI semantics. Prefer IMemoryCache injected at the scope you actually want. If you keep it static, every entry must be immutable and key collisions across tenants must be impossible.",
                412, 188, 1432),
            ("Review: changed JWT signing key reading to ENV with no fallback.",
                "Good direction — removing the hardcoded fallback closes a real risk. Add a startup assertion: fail fast if the env var is empty, instead of letting a zero-length key reach the JWT library, which silently accepts it on some versions.",
                388, 142, 1188),
            ("Review: parallel `await Task.WhenAll` over user.GetOrdersAsync(...) in a foreach.",
                "Two issues: (1) `GetOrdersAsync` hits EF Core; the same DbContext doesn't support concurrent operations and will throw. Materialize sequentially or open a context per task. (2) `WhenAll` swallows all but the first exception — wrap and aggregate.",
                502, 221, 1810),
        };

        // The analytics agent's prompt says "never invent numbers", so every numeric answer is
        // grounded in a run_sql/get_schema round-trip — see the tool conversations below.

        async Task SeedTraces(
            IAgent agent,
            IModelEndpoint endpoint,
            (string user, string assistant, ulong inTok, ulong outTok, int latencyMs)[] samples,
            int spreadHours)
        {
            foreach (var s in samples)
            {
                var request = new Conversation([
                    agent.CreateSystemMessage(),
                    new UserMessage([Content.FromText(s.user)])
                ]);
                var response = new AssistantMessage(
                    [Content.FromText(s.assistant)],
                    []);
                // Vary a cached-input share deterministically by prompt size (0%–80%) so the demo
                // shows the cache-hit column/KPI with realistic spread.
                var cachedIn = (ulong)(s.inTok * ((s.inTok % 9) / 10.0));
                var completion = completionFactory(
                    response,
                    new TokenUsage(s.inTok, s.outTok, cachedIn),
                    TimeSpan.FromMilliseconds(s.latencyMs));

                await agentCallFactory(
                    agent: agent,
                    version: agent.CurrentVersion,
                    endpoint: endpoint,
                    request: request,
                    response: completion,
                    httpStatus: HttpStatusCode.OK,
                    finishReason: "stop",
                    errorMessage: null,
                    modelParameters: paramsFactory(temperature: 0.3),
                    conversationId: null).AddAsync(cancellationToken);
            }
            _ = spreadHours;
        }

        // Triage traces show the agent's defects verbatim: a missed outage (P3'd a "down for
        // everyone" email), a fabricated plan tier (answered billing details WITHOUT calling any
        // tool — it has none that could look them up), and an invented category. The good answers
        // live in the tool conversations below, grounded in search_kb.
        var triageSamples = new (string user, string assistant, ulong inTok, ulong outTok, int latencyMs)[]
        {
            ("Subject: EVERYTHING IS DOWN. Our whole team can't log in, production dashboards are dead, we're losing money every minute.",
                "Category: Account Access. Priority: P3. Suggested reply: password resets can be performed from the login page.",
                188, 34, 460),
            ("Subject: Question about invoice. Hi, we were billed $499 but I thought our plan was $299/month?",
                "Category: Billing. Priority: P2. You are on the Platinum plan which is $499/month and includes 24/7 phone support.",
                174, 41, 512),
            ("Subject: Feature request — dark mode for the reporting screens would be great.",
                "Category: UI Feedback And Suggestions Misc. Priority: P1.",
                162, 22, 388),
        };

        await SeedTraces(supportAgent, supportEndpoint, supportSamples, spreadHours: 48);
        await SeedTraces(codeReviewAgent, reviewEndpoint, reviewSamples, spreadHours: 72);
        await SeedTraces(triageAgent, triageEndpoint, triageSamples, spreadHours: 24);

        // Every order-specific support answer is grounded in a lookup_order/start_return
        // round-trip, matching the agent's system prompt.
        var supportToolConversations = new (string user, ToolRequest tool, string toolResult, string final,
            (ulong inTok, ulong outTok, int latencyMs) turn1, (ulong inTok, ulong outTok, int latencyMs) turn2)[]
        {
            ("Where is my order #18342?",
                new ToolRequest(id: "call_lookup_18342", name: "lookup_order", arguments: """{"order_id":"18342"}"""),
                """{"order_id":"18342","status":"in_transit","carrier":"UPS","eta_days":2,"tracking":"1Z999AA10123456784"}""",
                "Order #18342 is in transit with UPS and should arrive within 2 business days. Tracking: 1Z999AA10123456784. Anything else?",
                (248, 32, 540), (312, 58, 710)),

            ("Order #20114 arrived damaged. Please start a return.",
                new ToolRequest(id: "call_return_20114", name: "start_return", arguments: """{"order_id":"20114","reason":"damaged"}"""),
                """{"return_id":"RMA-7741","label_url":"https://shop.example.com/labels/RMA-7741","refund_estimate_days":3}""",
                "Return RMA-7741 created for order #20114. Label: https://shop.example.com/labels/RMA-7741. Refund issued within 3 business days of receipt.",
                (261, 38, 602), (338, 71, 795)),

            ("My order #12831 hasn't shipped yet, it's been 6 days.",
                new ToolRequest(id: "call_lookup_12831", name: "lookup_order", arguments: """{"order_id":"12831"}"""),
                """{"order_id":"12831","status":"processing","warehouse":"Leipzig","placed_days_ago":6}""",
                "Sorry for the delay on order #12831 — it's still in our Leipzig warehouse, which is longer than it should be. I've flagged it for expedited dispatch today; you'll get the tracking link by email. Anything else?",
                (252, 30, 585), (316, 74, 760)),

            ("Can I change the address on order #14021?",
                new ToolRequest(id: "call_lookup_14021", name: "lookup_order", arguments: """{"order_id":"14021"}"""),
                """{"order_id":"14021","status":"preparing"}""",
                "Order #14021 is still in 'Preparing', so I can update the shipping address. Please reply with the new address and I'll change it right away.",
                (241, 29, 560), (305, 62, 700)),
        };

        // Every analytics number is grounded in a run_sql/get_schema round-trip — the agent's
        // prompt forbids inventing figures.
        var analyticsToolConversations = new (string user, ToolRequest tool, string toolResult, string final,
            (ulong inTok, ulong outTok, int latencyMs) turn1, (ulong inTok, ulong outTok, int latencyMs) turn2)[]
        {
            ("How many active users did we have last week?",
                new ToolRequest(id: "call_sql_active_users", name: "run_sql",
                    arguments: """{"query":"SELECT COUNT(DISTINCT user_id) FROM events WHERE event_at >= now() - INTERVAL '7 days';"}"""),
                """{"rows":[{"count":18432}],"row_count":1,"duration_ms":412}""",
                "Active users in the last 7 full days: 18,432.\n```sql\nSELECT COUNT(DISTINCT user_id)\nFROM events\nWHERE event_at >= now() - INTERVAL '7 days';\n```",
                (301, 46, 640), (368, 124, 980)),

            ("Top 5 SKUs by revenue in May 2026?",
                new ToolRequest(id: "call_sql_top_skus", name: "run_sql",
                    arguments: """{"query":"SELECT sku, SUM(line_total) AS revenue FROM order_lines WHERE order_at BETWEEN '2026-05-01' AND '2026-05-31' GROUP BY sku ORDER BY revenue DESC LIMIT 5;"}"""),
                """{"rows":[{"sku":"SKU-A88","revenue":142000},{"sku":"SKU-C12","revenue":118000},{"sku":"SKU-B07","revenue":94000},{"sku":"SKU-A91","revenue":82000},{"sku":"SKU-D34","revenue":71000}],"row_count":5,"duration_ms":688}""",
                "Top 5 SKUs by revenue, May 2026: SKU-A88 ($142k), SKU-C12 ($118k), SKU-B07 ($94k), SKU-A91 ($82k), SKU-D34 ($71k).\n```sql\nSELECT sku, SUM(line_total) AS revenue\nFROM order_lines\nWHERE order_at BETWEEN '2026-05-01' AND '2026-05-31'\nGROUP BY sku ORDER BY revenue DESC LIMIT 5;\n```",
                (344, 58, 720), (438, 198, 1260)),

            ("What columns does the events table have? I need to know what we can segment on.",
                new ToolRequest(id: "call_schema_events", name: "get_schema", arguments: """{"table":"events"}"""),
                """{"table":"events","columns":[{"name":"id","type":"bigint"},{"name":"user_id","type":"bigint"},{"name":"kind","type":"text"},{"name":"channel","type":"text"},{"name":"payload","type":"jsonb"},{"name":"occurred_at","type":"timestamptz"}]}""",
                "The events table has: id (bigint), user_id (bigint), kind (text), channel (text), payload (jsonb) and occurred_at (timestamptz). For segmentation the useful dimensions are kind, channel and anything you extract from payload.",
                (288, 24, 480), (352, 88, 690)),
        };

        // The triage agent grounds its how-to and bug replies in search_kb — its defects show
        // elsewhere, in the tool-less fabrication traces above.
        var triageToolConversations = new (string user, ToolRequest tool, string toolResult, string final,
            (ulong inTok, ulong outTok, int latencyMs) turn1, (ulong inTok, ulong outTok, int latencyMs) turn2)[]
        {
            ("Subject: How do I export my data as CSV?",
                new ToolRequest(id: "call_kb_csv_export", name: "search_kb", arguments: """{"query":"export data CSV"}"""),
                """{"articles":[{"id":"KB-118","title":"Exporting tables as CSV","url":"https://help.example.com/kb/118"}]}""",
                "Category: How-To. Priority: P4. Suggested reply: any table can be exported via the download icon in the top-right of the report view — full steps in KB-118 (https://help.example.com/kb/118).",
                (176, 22, 380), (231, 52, 490)),

            ("Subject: Webhooks stopped firing after we rotated our API key.",
                new ToolRequest(id: "call_kb_webhooks", name: "search_kb", arguments: """{"query":"webhooks stopped after API key rotation"}"""),
                """{"articles":[{"id":"KB-402","title":"Webhook subscriptions and API key rotation","url":"https://help.example.com/kb/402"}]}""",
                "Category: Bug. Priority: P2. Suggested reply: webhook subscriptions are bound to the API key that created them (KB-402) — re-create the subscription with the new key and delivery resumes immediately.",
                (182, 24, 400), (243, 56, 515)),
        };

        await SeedToolCallConversations(supportAgent, supportEndpoint, supportToolConversations);
        await SeedToolCallConversations(analyticsAgent, analyticsEndpoint, analyticsToolConversations);
        await SeedToolCallConversations(triageAgent, triageEndpoint, triageToolConversations);

        async Task SeedToolCallConversations(
            IAgent agent,
            IModelEndpoint endpoint,
            (string user, ToolRequest tool, string toolResult, string final,
                (ulong inTok, ulong outTok, int latencyMs) turn1, (ulong inTok, ulong outTok, int latencyMs) turn2)[] conversations)
        {
            foreach (var c in conversations)
            {
                var conversationId = Guid.NewGuid();
                var systemMsg = agent.CreateSystemMessage();
                var userMsg = new UserMessage([Content.FromText(c.user)]);
                var assistantToolMsg = new AssistantMessage([], [c.tool]);

                await agentCallFactory(
                    agent: agent, version: agent.CurrentVersion,
                    endpoint: endpoint,
                    request: new Conversation([systemMsg, userMsg]),
                    response: completionFactory(
                        assistantToolMsg,
                        new TokenUsage(c.turn1.inTok, c.turn1.outTok),
                        TimeSpan.FromMilliseconds(c.turn1.latencyMs)),
                    httpStatus: HttpStatusCode.OK,
                    finishReason: "tool_calls",
                    errorMessage: null,
                    modelParameters: paramsFactory(temperature: 0.3),
                    conversationId: conversationId).AddAsync(cancellationToken);

                var toolMsg = new ToolMessage(new ToolResponse(c.tool, [Content.FromText(c.toolResult)]));
                var finalAssistant = new AssistantMessage([Content.FromText(c.final)], []);

                await agentCallFactory(
                    agent: agent, version: agent.CurrentVersion,
                    endpoint: endpoint,
                    request: new Conversation([systemMsg, userMsg, assistantToolMsg, toolMsg]),
                    response: completionFactory(
                        finalAssistant,
                        new TokenUsage(c.turn2.inTok, c.turn2.outTok),
                        TimeSpan.FromMilliseconds(c.turn2.latencyMs)),
                    httpStatus: HttpStatusCode.OK,
                    finishReason: "stop",
                    errorMessage: null,
                    modelParameters: paramsFactory(temperature: 0.3),
                    conversationId: conversationId).AddAsync(cancellationToken);
            }
        }

        await SeedSupportNonDeliveryConversation(supportAgent, supportEndpoint);
        await SeedAnalyticsMultiTurnConversation(analyticsAgent, analyticsEndpoint);

        // A multi-turn support conversation whose order-status claim is grounded in a
        // lookup_order round-trip (turn 2 spans two agent calls: tool request, then the answer).
        async Task SeedSupportNonDeliveryConversation(IAgent agent, IModelEndpoint endpoint)
        {
            var conversationId = Guid.NewGuid();
            var system = agent.CreateSystemMessage();
            var history = new List<Message> { system };

            async Task AddCall(ICompletion completion, string finishReason)
                => await agentCallFactory(
                    agent: agent, version: agent.CurrentVersion,
                    endpoint: endpoint,
                    request: new Conversation(history.ToArray()),
                    response: completion,
                    httpStatus: HttpStatusCode.OK,
                    finishReason: finishReason,
                    errorMessage: null,
                    modelParameters: paramsFactory(temperature: 0.3),
                    conversationId: conversationId).AddAsync(cancellationToken);

            history.Add(new UserMessage([Content.FromText("Hi, I need help with order #19120.")]));
            var greeting = new AssistantMessage(
                [Content.FromText("Hi — happy to help with order #19120. What's the issue?")], []);
            await AddCall(completionFactory(greeting, new TokenUsage(232, 28), TimeSpan.FromMilliseconds(410)), "stop");
            history.Add(greeting);

            history.Add(new UserMessage([Content.FromText("It says delivered but I never received it.")]));
            var lookupRequest = new ToolRequest(
                id: "call_lookup_19120",
                name: "lookup_order",
                arguments: """{"order_id":"19120"}""");
            var lookupMsg = new AssistantMessage([], [lookupRequest]);
            await AddCall(completionFactory(lookupMsg, new TokenUsage(289, 31), TimeSpan.FromMilliseconds(520)), "tool_calls");
            history.Add(lookupMsg);
            history.Add(new ToolMessage(new ToolResponse(
                lookupRequest,
                [Content.FromText("""{"order_id":"19120","status":"delivered","carrier":"DHL","delivered_days_ago":1,"delivered_time":"14:02","signed_by":null}""")])));

            var nonDelivery = new AssistantMessage(
                [Content.FromText(
                    "The carrier marked #19120 as delivered yesterday at 14:02, but with no signature on file. "
                    + "I'll open a carrier trace and send a replacement right away. Should it go to the original address or a new one?")],
                []);
            await AddCall(completionFactory(nonDelivery, new TokenUsage(356, 86), TimeSpan.FromMilliseconds(780)), "stop");
            history.Add(nonDelivery);

            history.Add(new UserMessage([Content.FromText("Same address is fine. How long will it take?")]));
            var wrapUp = new AssistantMessage(
                [Content.FromText(
                    "Replacement for #19120 is scheduled for dispatch tomorrow morning, ETA 2–3 business days. "
                    + "You'll get tracking by email. Anything else?")],
                []);
            await AddCall(completionFactory(wrapUp, new TokenUsage(438, 76), TimeSpan.FromMilliseconds(690)), "stop");
        }

        // Each turn spans two agent calls: the model requests run_sql, then answers from the
        // result — no invented numbers.
        async Task SeedAnalyticsMultiTurnConversation(IAgent agent, IModelEndpoint endpoint)
        {
            var conversationId = Guid.NewGuid();
            var system = agent.CreateSystemMessage();

            var turns = new (string user, ToolRequest tool, string toolResult, string assistant,
                (ulong inTok, ulong outTok, int latencyMs) toolTurn, (ulong inTok, ulong outTok, int latencyMs) answerTurn)[]
            {
                ("How many signups did we have in April 2026?",
                    new ToolRequest(id: "call_sql_signups_april", name: "run_sql",
                        arguments: """{"query":"SELECT COUNT(*) FROM users WHERE created_at >= '2026-04-01' AND created_at < '2026-05-01';"}"""),
                    """{"rows":[{"count":4812}],"row_count":1,"duration_ms":231}""",
                    "April 2026 signups: 4,812.\n```sql\nSELECT COUNT(*) FROM users WHERE created_at >= '2026-04-01' AND created_at < '2026-05-01';\n```",
                    (298, 42, 640), (356, 112, 920)),
                ("Break that down by acquisition channel.",
                    new ToolRequest(id: "call_sql_signups_channel", name: "run_sql",
                        arguments: """{"query":"SELECT channel, COUNT(*) FROM users WHERE created_at >= '2026-04-01' AND created_at < '2026-05-01' GROUP BY channel ORDER BY 2 DESC;"}"""),
                    """{"rows":[{"channel":"organic","count":2104},{"channel":"paid_search","count":1388},{"channel":"referral","count":812},{"channel":"social","count":508}],"row_count":4,"duration_ms":344}""",
                    "By channel: organic 2,104, paid_search 1,388, referral 812, social 508.\n```sql\nSELECT channel, COUNT(*) FROM users\nWHERE created_at >= '2026-04-01' AND created_at < '2026-05-01'\nGROUP BY channel ORDER BY 2 DESC;\n```",
                    (414, 48, 700), (492, 168, 1180)),
                ("Which channel had the highest 7-day retention?",
                    new ToolRequest(id: "call_sql_d7_retention", name: "run_sql",
                        arguments: """{"query":"SELECT channel, AVG(CASE WHEN last_active_at >= created_at + INTERVAL '7 days' THEN 1.0 ELSE 0 END) AS d7 FROM users WHERE created_at BETWEEN '2026-04-01' AND '2026-04-30' GROUP BY channel ORDER BY d7 DESC;"}"""),
                    """{"rows":[{"channel":"referral","d7":0.41},{"channel":"organic","d7":0.33},{"channel":"paid_search","d7":0.22},{"channel":"social","d7":0.19}],"row_count":4,"duration_ms":518}""",
                    "Referral led at 41 % D7 retention, vs. organic 33 %, paid_search 22 %, social 19 %.\n```sql\nSELECT channel,\n  AVG(CASE WHEN last_active_at >= created_at + INTERVAL '7 days' THEN 1.0 ELSE 0 END) AS d7\nFROM users WHERE created_at BETWEEN '2026-04-01' AND '2026-04-30'\nGROUP BY channel ORDER BY d7 DESC;\n```",
                    (548, 64, 780), (636, 224, 1410)),
            };

            var history = new List<Message> { system };
            foreach (var t in turns)
            {
                history.Add(new UserMessage([Content.FromText(t.user)]));

                var toolCallMsg = new AssistantMessage([], [t.tool]);
                await agentCallFactory(
                    agent: agent, version: agent.CurrentVersion,
                    endpoint: endpoint,
                    request: new Conversation(history.ToArray()),
                    response: completionFactory(
                        toolCallMsg,
                        new TokenUsage(t.toolTurn.inTok, t.toolTurn.outTok),
                        TimeSpan.FromMilliseconds(t.toolTurn.latencyMs)),
                    httpStatus: HttpStatusCode.OK,
                    finishReason: "tool_calls",
                    errorMessage: null,
                    modelParameters: paramsFactory(temperature: 0.3),
                    conversationId: conversationId).AddAsync(cancellationToken);

                history.Add(toolCallMsg);
                history.Add(new ToolMessage(new ToolResponse(t.tool, [Content.FromText(t.toolResult)])));

                var assistant = new AssistantMessage([Content.FromText(t.assistant)], []);
                await agentCallFactory(
                    agent: agent, version: agent.CurrentVersion,
                    endpoint: endpoint,
                    request: new Conversation(history.ToArray()),
                    response: completionFactory(
                        assistant,
                        new TokenUsage(t.answerTurn.inTok, t.answerTurn.outTok),
                        TimeSpan.FromMilliseconds(t.answerTurn.latencyMs)),
                    httpStatus: HttpStatusCode.OK,
                    finishReason: "stop",
                    errorMessage: null,
                    modelParameters: paramsFactory(temperature: 0.3),
                    conversationId: conversationId).AddAsync(cancellationToken);

                history.Add(assistant);
            }
        }

        ctx.DemoUser = demoUser;
        ctx.Project = project;
        ctx.Gpt54Endpoint = gpt54Endpoint;
        ctx.Gpt54MiniEndpoint = gpt54MiniEndpoint;
        ctx.ClaudeEndpoint = claudeEndpoint;
        ctx.CustomerSupportAgent = supportAgent;
        ctx.CodeReviewAgent = codeReviewAgent;
        ctx.DataAnalyticsAgent = analyticsAgent;
        ctx.EmailTriageAgent = triageAgent;
    }
}
