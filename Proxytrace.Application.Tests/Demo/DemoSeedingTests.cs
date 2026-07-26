using Autofac;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Proxytrace.Application.Demo;
using Proxytrace.Domain;
using Proxytrace.Domain.AgentCall;
using Proxytrace.Domain.Evaluator;
using Proxytrace.Domain.Kiosk;
using Proxytrace.Domain.Message;
using Proxytrace.Domain.ModelEndpoint;
using Proxytrace.Domain.Notification;
using Proxytrace.Domain.OptimizationProposal;
using Proxytrace.Domain.OptimizationTheory;
using Proxytrace.Domain.TestRun;
using Proxytrace.Domain.TestRunGroup;
using Proxytrace.Domain.TestSuite;
using Proxytrace.Domain.Usage;
using Proxytrace.Testing;

namespace Proxytrace.Application.Tests.Demo;

[TestClass]
public class DemoSeedingTests : BaseTest<Module>
{
    // Seeding the full demo dataset is expensive (hundreds of entities), so it runs once
    // for the whole class and every test asserts read-only against the shared result.
    private static IContainer? sharedContainer;
    // ReSharper disable once NullableWarningSuppressionIsUsed
    private static IServiceProvider services = null!;

    [ClassInitialize]
    public static async Task SeedOnce(TestContext testContext)
    {
        sharedContainer = BuildContainer(builder =>
            builder.RegisterInstance(new KioskOptions
            {
                Enabled = true,
                DemoUserEmail = "demo@proxytrace.dev",
                DemoUserName = "Demo Visitor",
            }).AsSelf());

        services = sharedContainer.Resolve<IServiceProvider>();

        var scenarios = services.GetServices<IDemoScenario>()
            .OrderBy(s => s.Order)
            .ToList();

        scenarios.Should().NotBeEmpty("the test container must register every IDemoScenario");

        foreach (var scenario in scenarios)
            await scenario.SeedAsync(testContext.CancellationToken);
    }

    [ClassCleanup]
    public static void DisposeOnce()
    {
        sharedContainer?.Dispose();
        sharedContainer = null;
    }

    [TestMethod]
    public async Task Seed_All_Scenarios_Populates_Expected_Entity_Counts()
    {
        var ctx = services.GetRequiredService<DemoSeedContext>();
        ctx.Helpfulness.Should().NotBeNull();
        ctx.Politeness.Should().NotBeNull();
        ctx.SuitesByKey.Should().HaveCount(6);
        ctx.AllRuns.Should().HaveCountGreaterThan(10);

        var suites = await services.GetRequiredService<IRepository<ITestSuite>>()
            .GetAllAsync(CancellationToken);
        suites.Should().HaveCount(6);

        var evaluators = await services.GetRequiredService<IRepository<IEvaluator>>()
            .GetAllAsync(CancellationToken);
        evaluators.Should().HaveCount(2);
        evaluators.Should().AllBeAssignableTo<IAgenticEvaluator>();

        var proposals = await services.GetRequiredService<IRepository<IOptimizationProposal>>()
            .GetAllAsync(CancellationToken);
        proposals.Should().HaveCount(9);
        proposals.Select(p => p.Status).Should().Contain(
            [ProposalStatus.Draft, ProposalStatus.Accepted, ProposalStatus.Rejected, ProposalStatus.Adopted]);
        proposals.Select(p => p.Kind).Should().Contain(
            [ProposalKind.ModelSwitch, ProposalKind.SystemPrompt, ProposalKind.Tool]);
    }

    [TestMethod]
    public async Task Seed_Endpoints_Price_Traces_With_Displayable_Cost()
    {
        var endpoints = await services.GetRequiredService<IRepository<IModelEndpoint>>()
            .GetAllAsync(CancellationToken);
        endpoints.Should().NotBeEmpty();

        // Prices are EUR per 1M tokens (see ModelEndpoint.CalculateCost). A typical demo trace
        // (~10k in / 500 out) must cost at least €0.0001, i.e. render non-zero in the UI's
        // four-decimal cost display — per-token prices (1M× too small) show €0.0000 everywhere.
        foreach (var endpoint in endpoints.Where(e => e.InputTokenCost is not null))
        {
            var cost = endpoint.CalculateCost(new TokenUsage(10_000, 500, 0));
            cost.Should().NotBeNull();
            cost.Should().BeGreaterThanOrEqualTo(0.0001m,
                $"endpoint '{endpoint.Model.Name}' must price a typical trace above the UI display threshold");
        }
    }

    [TestMethod]
    public async Task Seed_Flags_Outlier_Calls_With_Every_Flag_Kind()
    {
        var calls = await services.GetRequiredService<IRepository<IAgentCall>>()
            .GetAllAsync(CancellationToken);

        var flagged = calls.Where(c => c.OutlierFlags != OutlierFlags.None).ToList();
        flagged.Should().NotBeEmpty("the kiosk must showcase outlier traces");

        // The curated outlier scenario is deterministic, so every flag kind must be present.
        var seen = flagged.Aggregate(OutlierFlags.None, (acc, c) => acc | c.OutlierFlags);
        seen.Should().HaveFlag(OutlierFlags.HighTokens);
        seen.Should().HaveFlag(OutlierFlags.HighLatency);
        seen.Should().HaveFlag(OutlierFlags.LowCacheHit);
        seen.Should().HaveFlag(OutlierFlags.ManyToolCalls);
    }

    [TestMethod]
    public async Task Seed_Raises_Real_Anomaly_Notifications_For_Incident_Groups()
    {
        var ctx = services.GetRequiredService<DemoSeedContext>();
        var notifications = await services.GetRequiredService<IRepository<INotification>>()
            .GetAllAsync(CancellationToken);

        var anomalies = notifications.Where(n => n.Kind == NotificationKind.Anomaly).ToList();

        // The regressed triage group must have produced a critical regression alert (pass-rate
        // drop ≥ the critical threshold), targeting the group itself.
        anomalies.Should().Contain(n =>
            n.Severity == NotificationSeverity.Critical
            && n.TargetKind == NotificationTargetKind.TestRunGroup
            && n.TargetId == ctx.RequireRegressedTriageGroup().Id
            && n.Title.StartsWith("Performance regression"));

        // The endpoint-down tone group must have produced the hard failure alert.
        anomalies.Should().Contain(n =>
            n.Severity == NotificationSeverity.Critical
            && n.TargetKind == NotificationTargetKind.TestRunGroup
            && n.TargetId == ctx.RequireFailedToneGroup().Id
            && n.Title.StartsWith("Test run failed"));
    }

    [TestMethod]
    public async Task Terminal_Theories_Reference_The_Seeded_AB_Candidate_Run()
    {
        var runRepo = services.GetRequiredService<IRepository<ITestRun>>();
        var theories = await services.GetRequiredService<IRepository<IOptimizationTheory>>()
            .GetAllAsync(CancellationToken);

        var terminal = theories
            .Where(t => t.Status is TheoryStatus.Validated or TheoryStatus.Invalidated)
            .ToList();
        terminal.Should().NotBeEmpty();

        foreach (var theory in terminal)
        {
            theory.ABTestRunId.Should().NotBeNull(
                $"terminal theory '{theory.Rationale[..Math.Min(40, theory.Rationale.Length)]}…' should link its A/B run");

            var contained = await runRepo.ContainsAsync(theory.ABTestRunId.Value, CancellationToken);
            contained.Should().BeTrue("the linked A/B candidate run must exist");
        }
    }

    [TestMethod]
    public async Task Validated_Theories_Link_Distinct_Proposals()
    {
        var theories = await services.GetRequiredService<IRepository<IOptimizationTheory>>()
            .GetAllAsync(CancellationToken);

        // A proposal represents a single change through one review lifecycle; two theories must
        // never share one, or promoting/dismissing one silently mutates the other and the second
        // "Promote" 409s (Draft→Accepted refused once the shared proposal is already Accepted).
        var linkedProposalIds = theories
            .Select(t => t.ResultingProposalId)
            .OfType<Guid>()
            .ToList();

        linkedProposalIds.Should().OnlyHaveUniqueItems(
            "each seeded theory must back onto its own proposal — a shared proposal breaks the promote flow");
    }

    [TestMethod]
    public async Task Proposals_Reference_Valid_TestRuns()
    {
        var runRepo = services.GetRequiredService<IRepository<ITestRun>>();
        var proposals = await services.GetRequiredService<IRepository<IOptimizationProposal>>()
            .GetAllAsync(CancellationToken);

        foreach (var proposal in proposals)
        {
            proposal.EvidenceTestRunIds.Should().NotBeEmpty(
                $"proposal '{proposal.Rationale[..Math.Min(40, proposal.Rationale.Length)]}…' should cite evidence");

            foreach (var runId in proposal.EvidenceTestRunIds)
            {
                var contained = await runRepo.ContainsAsync(runId, CancellationToken);
                contained.Should().BeTrue($"run {runId} cited by proposal must exist");
            }
        }
    }

    [TestMethod]
    public async Task Suites_Reference_Only_Seeded_Evaluators()
    {
        var evaluators = await services.GetRequiredService<IRepository<IEvaluator>>()
            .GetAllAsync(CancellationToken);
        var evaluatorIds = evaluators.Select(e => e.Id).ToHashSet();

        var suites = await services.GetRequiredService<IRepository<ITestSuite>>()
            .GetAllAsync(CancellationToken);

        foreach (var suite in suites)
        {
            suite.Evaluators.Should().NotBeEmpty();
            foreach (var ev in suite.Evaluators)
                evaluatorIds.Should().Contain(ev.Id);
        }
    }

    // ---- #290: seeded runs/groups are terminal by construction, so a live kiosk's runner can
    //      never pick them up and execute their cases against the disabled ModelClient. ----

    [TestMethod]
    public async Task Seed_Leaves_No_TestRun_Or_Group_In_A_Non_Terminal_State()
    {
        // The TestRunnerService loop only ever executes Pending/Running work. Seeding every group
        // and run directly in a terminal state (Completed/Failed) is what removes the race between
        // the seeder and the runner — a single non-terminal row would reopen it.
        var groups = await services.GetRequiredService<IRepository<ITestRunGroup>>()
            .GetAllAsync(CancellationToken);
        var runs = await services.GetRequiredService<IRepository<ITestRun>>()
            .GetAllAsync(CancellationToken);

        groups.Should().NotBeEmpty();
        runs.Should().NotBeEmpty();

        groups.Should().OnlyContain(g => g.Status.IsTerminal(),
            "no seeded group may sit in Pending/Running where the runner would execute it");
        runs.Should().OnlyContain(r => r.Status.IsTerminal(),
            "no seeded run may sit in Pending/Running where the runner would execute it");
    }

    [TestMethod]
    public async Task Seed_FailedToneGroup_And_Its_Run_Are_Failed_Without_Results()
    {
        var ctx = services.GetRequiredService<DemoSeedContext>();
        var group = ctx.RequireFailedToneGroup();
        group.Status.Should().Be(TestRunStatus.Failed);

        var runs = await group.GetTestRuns(CancellationToken);
        runs.Should().ContainSingle("the endpoint-down group has a single resultless run");
        runs.Single().Status.Should().Be(TestRunStatus.Failed);
        runs.Single().TestResults.Should().BeEmpty("the run never produced a result");
    }

    [TestMethod]
    public async Task Seed_AB_Candidate_Runs_Are_Completed_With_Results()
    {
        var ctx = services.GetRequiredService<DemoSeedContext>();
        ctx.AbCandidateRunsByAgent.Should().NotBeEmpty();

        var runRepo = services.GetRequiredService<IRepository<ITestRun>>();
        foreach (var run in ctx.AbCandidateRunsByAgent.Values)
        {
            var stored = await runRepo.GetAsync(run.Id, CancellationToken);
            stored.Status.Should().Be(TestRunStatus.Completed);
            stored.CompletedAt.Should().NotBeNull();
            stored.TestResults.Should().NotBeEmpty();
        }
    }

    // ---- Req 1/6 (task-3): the support agent gains issue_refund as its third tool. ----

    [TestMethod]
    public void Seed_CustomerSupportAgent_Has_IssueRefund_As_Third_Tool()
    {
        var ctx = services.GetRequiredService<DemoSeedContext>();
        var agent = ctx.RequireCustomerSupportAgent();

        agent.Tools.Should().HaveCount(3,
            "the support agent must have lookup_order, start_return, and issue_refund");
        agent.Tools.Should().Contain(t => t.Name == "issue_refund",
            "issue_refund must be registered on the support agent for the showcase trick");
        agent.SystemPrompt.Template.Should().Contain("issue_refund",
            "the system prompt should reference issue_refund so the model knows it exists");
    }

    // ---- Req 2/6 (task-3): the refund suite grows from 5 to 10 social-engineering cases. ----

    [TestMethod]
    public void Seed_RefundSuite_Has_Ten_Cases_And_Embeds_A_Lookup_For_Every_Named_Order()
    {
        var ctx = services.GetRequiredService<DemoSeedContext>();
        var suite = ctx.SuitesByKey["customer-support-refunds"];

        suite.TestCases.Should().HaveCount(10,
            "the refund suite must grow to 10 cases to give the optimizer sufficient failing signal");

        foreach (var testCase in suite.TestCases)
        {
            var prompt = string.Concat(testCase.Input.Messages
                .OfType<UserMessage>()
                .SelectMany(m => m.Contents)
                .Select(c => c.Text));
            var lookups = testCase.Input.Messages
                .OfType<AssistantMessage>()
                .SelectMany(m => m.ToolRequests)
                .Where(t => t.Name == "lookup_order")
                .ToList();

            // A case that names an order must embed the lookup round-trip. The runner replays a
            // case with a single completion, so without it the agent's first move is "what's your
            // order number?" — which the helpfulness judge fails on etiquette, not on policy, and
            // that noise is what the optimizer would learn from. Cases that ask a pure policy
            // question (no order id) stay plain text.
            if (prompt.Contains("#2"))
            {
                lookups.Should().ContainSingle(
                    $"a case naming an order must embed its lookup_order round-trip: '{prompt}'");
                testCase.Input.Messages.OfType<ToolMessage>().Should().ContainSingle(
                    "the lookup_order result must be present in the input conversation");
            }
            else
            {
                lookups.Should().BeEmpty(
                    $"a pure policy question needs no order lookup: '{prompt}'");
            }

            // Whatever the shape, the scored turn is the final text answer.
            testCase.ExpectedOutput.ToolRequests.Should().BeEmpty(
                "the expected output must be the final text answer, not a tool-call turn");
        }
    }

    [TestMethod]
    public void Seed_RefundSuite_Tool_Results_Leak_No_Policy()
    {
        var ctx = services.GetRequiredService<DemoSeedContext>();
        var suite = ctx.SuitesByKey["customer-support-refunds"];

        // The showcase turns on the naive agent NOT knowing the rules. A tool result that reports
        // the return window or the goodwill ceiling lets it recite the policy and pass on its own,
        // which erases the failing signal the optimizer needs (verified live against the model).
        string[] policyLeaks =
            ["return_window", "goodwill", "policy", "refundable", "expired", "eligible"];

        foreach (var toolResult in suite.TestCases
                     .SelectMany(c => c.Input.Messages.OfType<ToolMessage>())
                     .SelectMany(m => m.Contents)
                     .Select(c => c.Text))
        {
            foreach (var leak in policyLeaks)
            {
                toolResult.Should().NotContain(leak,
                    $"tool results carry order facts only — '{leak}' is store policy and belongs "
                    + "in the system prompt the optimizer is meant to fix");
            }
        }
    }

    // ---- #292: the Data Analytics agent's prompt forbids inventing numbers, so its first turn is
    //      a run_sql tool call. Each case embeds the round-trip so a live re-run (with a
    //      Kiosk:Endpoint configured) scores the final text answer, not the tool-call turn. ----

    [TestMethod]
    public void Seed_Analytics_Cases_Embed_A_RunSql_Roundtrip_And_Expect_A_Text_Answer()
    {
        var ctx = services.GetRequiredService<DemoSeedContext>();
        var suite = ctx.SuitesByKey["data-analytics-queries"];
        suite.TestCases.Should().NotBeEmpty();

        foreach (var testCase in suite.TestCases)
        {
            var messages = testCase.Input.Messages;

            // The model's first turn is a run_sql tool call...
            messages.OfType<AssistantMessage>()
                .Any(m => m.ToolRequests.Any(t => t.Name == "run_sql"))
                .Should().BeTrue(
                    "every analytics case must embed the run_sql call so a live re-run lands on the text answer");

            // ...answered by a tool-result turn in the same input conversation...
            messages.OfType<ToolMessage>().Should().NotBeEmpty(
                "the run_sql tool result must be present in the input conversation");

            // ...and the scored turn is the final text answer, not another tool call.
            testCase.ExpectedOutput.ToolRequests.Should().BeEmpty(
                "the expected output must be the final text answer, not a tool-call turn");
            testCase.ExpectedOutput.Contents.Should()
                .Contain(c => !string.IsNullOrWhiteSpace(c.Text),
                    "the expected answer must contain grounded text");
        }
    }
}
