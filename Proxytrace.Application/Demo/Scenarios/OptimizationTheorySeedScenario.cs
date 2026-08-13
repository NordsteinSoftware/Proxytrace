using Proxytrace.Domain;
using Proxytrace.Domain.Agent;
using Nordstein.Core.Domain.Exceptions;
using Proxytrace.Domain.ModelEndpoint;
using Proxytrace.Domain.OptimizationProposal;
using Proxytrace.Domain.OptimizationTheory;
using Proxytrace.Domain.Proposal;
using Proxytrace.Domain.TestSuite;
using Proxytrace.Domain.Tools;

namespace Proxytrace.Application.Demo.Scenarios;

/// <summary>
/// Seeds the optimization-theory pipeline so the proposals board shows hypotheses spread across
/// every lifecycle column. Validated theories link to the Draft proposals seeded just before
/// (Order 40) so the board's "Promote" action has a real target.
/// <para>
/// No theory is seeded <see cref="TheoryStatus.Validating"/>: the kiosk skips the validation
/// queue's restart recovery (see <c>TheoryValidationService.RecoverInFlightTheoriesAsync</c>), so
/// such a theory would sit mid-A/B forever — a pulsing "A/B in flight" row and a progress bar that
/// never resolves. Every theory here is either queued (Proposed) or already decided.
/// </para>
/// </summary>
internal sealed class OptimizationTheorySeedScenario : IDemoScenario
{
    private readonly DemoSeedContext ctx;
    private readonly ISystemPromptTheory.CreateNew createSystemPrompt;
    private readonly IModelSwitchTheory.CreateNew createModelSwitch;
    private readonly IToolUpdateTheory.CreateNew createToolUpdate;
    private readonly IRepository<IOptimizationTheory> repo;
    private readonly IOptimizationProposalRepository proposals;
    // Proposal ids already handed to an earlier validated theory this seed run. A proposal is a
    // single change through one review lifecycle, so two theories must never share one — otherwise
    // promoting/dismissing one silently mutates the other and the second "Promote" 409s.
    private readonly HashSet<Guid> claimedProposalIds = [];

    public OptimizationTheorySeedScenario(
        DemoSeedContext ctx,
        ISystemPromptTheory.CreateNew createSystemPrompt,
        IModelSwitchTheory.CreateNew createModelSwitch,
        IToolUpdateTheory.CreateNew createToolUpdate,
        IRepository<IOptimizationTheory> repo,
        IOptimizationProposalRepository proposals)
    {
        this.ctx = ctx;
        this.createSystemPrompt = createSystemPrompt;
        this.createModelSwitch = createModelSwitch;
        this.createToolUpdate = createToolUpdate;
        this.repo = repo;
        this.proposals = proposals;
    }

    // After OptimizationProposalSeedScenario (40) so validated theories can reference a proposal.
    public int Order => 45;

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        var support = ctx.RequireCustomerSupportAgent();
        var codeReview = ctx.RequireCodeReviewAgent();
        var analytics = ctx.RequireDataAnalyticsAgent();
        var triage = ctx.RequireEmailTriageAgent();

        // ── Proposed: hypotheses awaiting a test ──────────────────────────────────────────
        await SeedAsync(createModelSwitch(
            support, SuiteFor(support), TheorySource.Optimizer, Priority.High,
            "Swapping gpt-5.4 → claude-sonnet-4.5 will raise bug-localisation recall enough to justify the cost.",
            ctx.RequireClaudeEndpoint(), EvidenceFor(support)), TheoryStatus.Proposed, cancellationToken);

        await SeedAsync(createSystemPrompt(
            codeReview, SuiteFor(codeReview), TheorySource.Optimizer, Priority.Medium,
            "Adding 4 worked stack-trace examples will fix localisation without paying for a bigger model.",
            "You are a senior engineer. When localising a bug, walk the stack trace frame by frame "
            + "and cite the exact file and line before proposing a fix.",
            EvidenceFor(codeReview)), TheoryStatus.Proposed, cancellationToken);

        await SeedAsync(createSystemPrompt(
            analytics, SuiteFor(analytics), TheorySource.User, Priority.Medium,
            "Six curated few-shot examples for the edge categories will recover the misclassified long tail.",
            "You are a data analyst. Classify each row using the provided taxonomy. "
            + "Study the worked examples below before answering.",
            EvidenceFor(analytics)), TheoryStatus.Proposed, cancellationToken);

        // The triage agent is the deliberately defective one: its regression is what the anomaly
        // alerts point at, and these hypotheses are the loop's first answers to it.
        await SeedAsync(createToolUpdate(
            triage, SuiteFor(triage), TheorySource.TraceyAi, Priority.High,
            "The agent fabricates plan and billing details it cannot know. A typed lookup_customer_plan "
            + "tool grounds those answers in the account record instead.",
            [new ToolSpecification(
                name: "lookup_customer_plan",
                description: "Fetch the customer's current plan, seat count and billing status by account id.",
                arguments: ToolArguments.None)],
            EvidenceFor(triage)), TheoryStatus.Proposed, cancellationToken);

        // ── Validated: confirmed, ready to ship ───────────────────────────────────────────
        await SeedAsync(createSystemPrompt(
            support, SuiteFor(support), TheorySource.Optimizer, Priority.High,
            "Requiring an explicit order-ID confirmation step before any refund will stop the agent acting on assumed context.",
            "You are a customer-support agent. Before issuing a refund, confirm the order id with the "
            + "customer and never act on an assumed order.",
            EvidenceFor(support)),
            TheoryStatus.Validated, cancellationToken, baseline: 0.78, projected: 0.90, pValue: 0.008);

        await SeedAsync(createSystemPrompt(
            support, SuiteFor(support), TheorySource.Optimizer, Priority.Medium,
            "Tightening the priority-threshold wording will reduce mis-routed P1 tickets.",
            "You are a support triage agent. Treat outages and data loss as P1; everything else is P2 or lower. "
            + "State the priority explicitly in every reply.",
            EvidenceFor(support)),
            TheoryStatus.Validated, cancellationToken, baseline: 0.71, projected: 0.78, pValue: 0.03);

        // The defective triage agent's fix, measured: it backs the Draft triage prompt proposal
        // seeded at Order 40 and is the board's headline "needs decision" row.
        await SeedAsync(createSystemPrompt(
            triage, SuiteFor(triage), TheorySource.Optimizer, Priority.Critical,
            "The prompt defines no category taxonomy and no priority rules, so the model invents both. "
            + "Pinning an explicit taxonomy and P1–P4 definitions should recover the failing cases.",
            "You are an email triage assistant for a SaaS company. Classify every email into exactly one "
            + "of: Outage, Bug, Billing, Compliance, Account Access, Feature Request, How-To. Assign "
            + "priority P1 (outage, data loss, legal deadline) to P4 (nice-to-have). Escalate P1 immediately. "
            + "Never state plan or billing details you have not looked up.",
            EvidenceFor(triage)),
            TheoryStatus.Validated, cancellationToken, baseline: 0.25, projected: 0.62, pValue: 0.01);

        // ── Rejected: disproven by A/B ────────────────────────────────────────────────────
        await SeedAsync(createModelSwitch(
            analytics, SuiteFor(analytics), TheorySource.Optimizer, Priority.Low,
            "Lowering temperature 0.7 → 0.2 will make repeated classifications consistent.",
            ctx.RequireGpt54MiniEndpoint(), EvidenceFor(analytics)),
            TheoryStatus.Invalidated, cancellationToken, baseline: 0.89, projected: 0.90, pValue: 0.41);

        await SeedAsync(createToolUpdate(
            support, SuiteFor(support), TheorySource.TraceyAi, Priority.High,
            "Giving the agent a typed lookup_shipping_carrier tool will eliminate fabricated carrier tracking instructions.",
            [new ToolSpecification(
                name: "lookup_shipping_carrier",
                description: "Resolve the carrier and tracking URL for a shipment id.",
                arguments: ToolArguments.None)],
            EvidenceFor(support)),
            TheoryStatus.Invalidated, cancellationToken, baseline: 0.83, projected: 0.84, pValue: 0.52);
    }

    private ITestSuite SuiteFor(IAgent agent)
    {
        var run = ctx.AllRuns.FirstOrDefault(r => r.Group.Suite.Agent.Id == agent.Id);
        return run?.Group.Suite ?? throw new InvalidOperationException($"No seeded suite for agent {agent.Name}.");
    }

    private IReadOnlyCollection<Guid> EvidenceFor(IAgent agent)
        => ctx.AllRuns
            .Where(r => r.Group.Suite.Agent.Id == agent.Id)
            .Take(3)
            .Select(r => r.Id)
            .ToArray();

    private async Task SeedAsync(
        IOptimizationTheory theory,
        TheoryStatus target,
        CancellationToken cancellationToken,
        double? baseline = null,
        double? projected = null,
        double? pValue = null)
    {
        await repo.AddAsync(theory, cancellationToken);
        if (target == TheoryStatus.Proposed)
            return;

        var proposalId = target == TheoryStatus.Validated
            ? await FindProposalIdAsync(theory.Agent.Id, theory.Kind, cancellationToken)
            : (Guid?)null;

        // Each transition is a separate persisted write; reload + retry so a transient concurrency
        // conflict (e.g. a background reader bumping the row) doesn't abort the seed. Resumable from
        // whatever state actually committed.
        const int maxAttempts = 5;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var saved = await repo.GetAsync(theory.Id, cancellationToken);
                if (saved.Status == TheoryStatus.Proposed)
                    saved = await saved.SetValidating(cancellationToken);

                if (saved.Status == TheoryStatus.Validating)
                {
                    // Point terminal theories at the seeded hidden system A/B candidate run, so the
                    // board's A/B card resolves to a real run entity.
                    Guid? abTestRunId = ctx.AbCandidateRunsByAgent.TryGetValue(theory.Agent.Id, out var abRun)
                        ? abRun.Id
                        : null;

                    if (target == TheoryStatus.Validated && proposalId is { } id)
                        await saved.SetValidated(id, baseline, projected, pValue, abTestRunId, cancellationToken);
                    else if (target == TheoryStatus.Invalidated)
                        await saved.SetInvalidated(baseline, projected, pValue, abTestRunId, cancellationToken);
                }
                return;
            }
            catch (OptimisticConcurrencyException) when (attempt < maxAttempts)
            {
                await Task.Delay(25 * attempt, cancellationToken);
            }
        }
    }

    private async Task<Guid> FindProposalIdAsync(Guid agentId, ProposalKind kind, CancellationToken cancellationToken)
    {
        var agentProposals = (await proposals.GetByAgentAsync(agentId, cancellationToken))
            // Never hand the same proposal to two validated theories (see claimedProposalIds).
            .Where(p => !claimedProposalIds.Contains(p.Id))
            .ToArray();
        // Prefer a Draft proposal of the same kind so the drawer shows a coherent change for the
        // validated theory; fall back to any Draft, then any proposal.
        var match = agentProposals.FirstOrDefault(p => p.Status == ProposalStatus.Draft && p.Kind == kind)
            ?? agentProposals.FirstOrDefault(p => p.Status == ProposalStatus.Draft)
            ?? agentProposals.FirstOrDefault();
        if (match is null)
            throw new InvalidOperationException($"No unclaimed seeded proposal to back a validated theory for agent {agentId}.");
        claimedProposalIds.Add(match.Id);
        return match.Id;
    }
}
