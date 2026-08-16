using Proxytrace.Domain;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.OptimizationProposal;
using Proxytrace.Domain.Proposal;
using Proxytrace.Domain.TestRun;
using Nordstein.Core.AI.Tools;

namespace Proxytrace.Application.Demo.Scenarios;

internal sealed class OptimizationProposalSeedScenario : IDemoScenario
{
    private readonly DemoSeedContext ctx;
    private readonly IModelSwitchProposal.CreateNew createModelSwitch;
    private readonly ISystemPromptProposal.CreateNew createSystemPrompt;
    private readonly IToolUpdateProposal.CreateNew createToolUpdate;
    private readonly IRepository<IOptimizationProposal> repo;

    /// <summary>
    /// Initializes a new instance of the <see cref="OptimizationProposalSeedScenario"/> class.
    /// </summary>
    public OptimizationProposalSeedScenario(
        DemoSeedContext ctx,
        IModelSwitchProposal.CreateNew createModelSwitch,
        ISystemPromptProposal.CreateNew createSystemPrompt,
        IToolUpdateProposal.CreateNew createToolUpdate,
        IRepository<IOptimizationProposal> repo)
    {
        this.ctx = ctx;
        this.createModelSwitch = createModelSwitch;
        this.createSystemPrompt = createSystemPrompt;
        this.createToolUpdate = createToolUpdate;
        this.repo = repo;
    }

    /// <summary>
    /// Gets the order.
    /// </summary>
    public int Order => 40;

    private sealed record ProposalSpec(
        Func<DemoSeedContext, IAgent> SelectAgent,
        ProposalStatus Status,
        Func<DemoSeedContext, IAgent, IReadOnlyCollection<Guid>, ITestRun, IOptimizationProposal> BuildDraft);

    /// <summary>
    /// Seeds asynchronously.
    /// </summary>
    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        var specs = new ProposalSpec[]
        {
            new(
                SelectAgent: c => c.RequireCustomerSupportAgent(),
                Status: ProposalStatus.Draft,
                BuildDraft: (c, agent, evidence, ab) => createModelSwitch(
                    agent,
                    Priority.High,
                    "Claude consistently outperforms gpt-5.4 on the tone suite (+17 percentage points pass rate). "
                    + "Latency increase is negligible and per-call cost drops to roughly a quarter.",
                    c.RequireClaudeEndpoint(),
                    0.71,
                    0.88,
                    -0.0095m,
                    TimeSpan.FromMilliseconds(50),
                    evidence,
                    ab)),

            new(
                SelectAgent: c => c.RequireCustomerSupportAgent(),
                Status: ProposalStatus.Accepted,
                BuildDraft: (_, agent, evidence, ab) => createSystemPrompt(
                    agent,
                    Priority.Medium,
                    "Adding explicit empathy guidance to the system prompt raised pass rate from 50 % to 67 % "
                    + "between the first two runs of the tone suite.",
                    "You are a friendly, concise customer-support agent for an e-commerce store. "
                    + "Open with an empathetic acknowledgement of the customer's situation. "
                    + "Propose a clear next step, and close politely. Never blame the customer.",
                    0.50,
                    0.67,
                    evidence,
                    ab)),

            new(
                SelectAgent: c => c.RequireCodeReviewAgent(),
                Status: ProposalStatus.Draft,
                BuildDraft: (_, agent, evidence, ab) => createToolUpdate(
                    agent,
                    Priority.High,
                    "Adding a `lookup_symbol` tool would let the reviewer cite definitions instead of guessing "
                    + "when reviewing diffs that reference unfamiliar identifiers.",
                    [
                        new ToolSpecification(
                            name: "lookup_symbol",
                            description: "Look up the definition of a symbol (function, class, constant) in the repository.",
                            arguments: ToolArguments.None),
                    ],
                    0.55,
                    0.74,
                    evidence,
                    ab)),

            new(
                SelectAgent: c => c.RequireCodeReviewAgent(),
                Status: ProposalStatus.Rejected,
                BuildDraft: (_, agent, evidence, ab) => createSystemPrompt(
                    agent,
                    Priority.Low,
                    "Earlier attempt to soften review tone via prompt rewrite did not move the politeness pass rate "
                    + "(40 % both runs). Rejected in favour of a follow-up tool or fine-tune.",
                    "You are a senior software engineer reviewing pull requests. "
                    + "Be encouraging. Identify correctness, security, and clarity issues with concrete suggestions. "
                    + "Cite line numbers and offer to pair if a fix is non-trivial.",
                    0.40,
                    0.40,
                    evidence,
                    ab)),

            new(
                SelectAgent: c => c.RequireDataAnalyticsAgent(),
                Status: ProposalStatus.Draft,
                BuildDraft: (c, agent, evidence, ab) => createModelSwitch(
                    agent,
                    Priority.Critical,
                    "gpt-5.4-mini outperformed gpt-5.4 on the analytics suite at a fraction of the cost. "
                    + "Switching saves ~95 % of inference spend with no quality loss.",
                    c.RequireGpt54MiniEndpoint(),
                    0.69,
                    0.82,
                    -0.0120m,
                    TimeSpan.FromMilliseconds(-200),
                    evidence,
                    ab)),

            new(
                SelectAgent: c => c.RequireCustomerSupportAgent(),
                Status: ProposalStatus.Draft,
                BuildDraft: (_, agent, evidence, ab) => createSystemPrompt(
                    agent,
                    Priority.High,
                    "Tool-call traces show the agent often replies in prose before invoking `lookup_order`, "
                    + "wasting tokens and latency. Instruct it to call the tool first when an order id is present.",
                    "You are a friendly, concise customer-support agent for an e-commerce store. "
                    + "When a customer mentions an order id, call `lookup_order` BEFORE composing a reply. "
                    + "Acknowledge the issue, propose a clear next step, and close politely. Never blame the customer.",
                    0.62,
                    0.79,
                    evidence,
                    ab)),

            new(
                SelectAgent: c => c.RequireDataAnalyticsAgent(),
                Status: ProposalStatus.Adopted,
                BuildDraft: (_, agent, evidence, ab) => createSystemPrompt(
                    agent,
                    Priority.Medium,
                    "Evaluator flagged 22 % of answers missing the SQL block. Making the SQL section mandatory "
                    + "in the system prompt closed the gap in a pilot run.",
                    "You are a data analyst. Given a question and a table description, answer with: "
                    + "(1) a one-sentence numeric summary, (2) a fenced ```sql``` block with the exact query, "
                    + "(3) any assumptions you made. Never omit the SQL block.",
                    0.71,
                    0.89,
                    evidence,
                    ab)),

            // The defective triage agent: the cheap model misses sarcasm, mixed languages and
            // implicit urgency — the quality floor a prompt tweak alone hasn't recovered.
            new(
                SelectAgent: c => c.RequireEmailTriageAgent(),
                Status: ProposalStatus.Draft,
                BuildDraft: (c, agent, evidence, ab) => createModelSwitch(
                    agent,
                    Priority.High,
                    "gpt-5.4-mini persistently fails the ambiguous triage cases (sarcasm, mixed-language, implicit "
                    + "urgency) that stay red across every run. gpt-5.4 resolves most of them in offline replay; "
                    + "the cost increase is bounded by triage's low volume.",
                    c.RequireGpt54Endpoint(),
                    0.25,
                    0.75,
                    0.0110m,
                    TimeSpan.FromMilliseconds(320),
                    evidence,
                    ab)),

            // Backs the validated triage taxonomy theory (OptimizationTheorySeedScenario): the
            // prompt fix the loop measured for the defective agent, waiting on a promote decision.
            new(
                SelectAgent: c => c.RequireEmailTriageAgent(),
                Status: ProposalStatus.Draft,
                BuildDraft: (_, agent, evidence, ab) => createSystemPrompt(
                    agent,
                    Priority.Critical,
                    "The prompt defines no category taxonomy and no priority rules, so the model invents both. "
                    + "Pinning an explicit taxonomy and P1–P4 definitions recovers most of the failing cases.",
                    "You are an email triage assistant for a SaaS company. Classify every email into exactly one "
                    + "of: Outage, Bug, Billing, Compliance, Account Access, Feature Request, How-To. Assign "
                    + "priority P1 (outage, data loss, legal deadline) to P4 (nice-to-have). Escalate P1 immediately. "
                    + "Never state plan or billing details you have not looked up.",
                    0.25,
                    0.62,
                    evidence,
                    ab)),

            new(
                SelectAgent: c => c.RequireCodeReviewAgent(),
                Status: ProposalStatus.Draft,
                BuildDraft: (_, agent, evidence, ab) => createSystemPrompt(
                    agent,
                    Priority.Medium,
                    "Reviewer skips severity tagging on ~35 % of findings, hurting downstream triage. "
                    + "Requiring an explicit severity per comment raises consistency in offline trials.",
                    "You are a senior software engineer reviewing pull requests. For each finding, prefix it "
                    + "with `[Critical]`, `[Major]`, or `[Minor]`. Identify correctness, security, and clarity issues. "
                    + "Be specific, cite line numbers, and suggest the smallest viable fix.",
                    0.58,
                    0.76,
                    evidence,
                    ab)),
        };

        foreach (var spec in specs)
        {
            var agent = spec.SelectAgent(ctx);
            var runsForAgent = ctx.AllRuns
                .Where(r => r.Group.Suite.Agent.Id == agent.Id)
                .ToArray();
            if (runsForAgent.Length == 0)
            {
                continue;
            }
            var evidence = runsForAgent.Take(3).Select(r => r.Id).ToArray();
            // Prefer the hidden system A/B candidate run seeded for this agent; fall back to the
            // first user-facing run for agents without one.
            var abTestRun = ctx.AbCandidateRunsByAgent.GetValueOrDefault(agent.Id) ?? runsForAgent[0];

            var draft = spec.BuildDraft(ctx, agent, evidence, abTestRun);
            var saved = await repo.AddAsync(draft, cancellationToken);

            switch (spec.Status)
            {
                case ProposalStatus.Accepted:
                    await saved.Accept(cancellationToken);
                    break;
                case ProposalStatus.Rejected:
                    await saved.Reject(cancellationToken);
                    break;
                case ProposalStatus.Adopted:
                    await (await saved.Accept(cancellationToken)).MarkAdopted(null, manual: true, cancellationToken);
                    break;
            }
        }
    }
}
