using System.ComponentModel;
using ModelContextProtocol.Server;

namespace Proxytrace.Api.Mcp.Prompts;

/// <summary>
/// MCP prompts — reusable Proxytrace workflows an MCP client surfaces (e.g. as slash commands). Each
/// returns a playbook that drives the model through the MCP tools in the right order. They mirror the
/// Tracey assistant's skills, retargeted to the MCP tool surface (no cards/confirmation/await — the
/// model just calls tools and reads results). All work is scoped to the connecting key's project.
/// </summary>
[McpServerPromptType]
internal static class ProxytracePrompts
{
    private static string Target(string? subject, string label)
        => string.IsNullOrWhiteSpace(subject) ? string.Empty : $"\n\n{label}: {subject}";

    /// <summary>
    /// Optimize agent.
    /// </summary>
    [McpServerPrompt(Name = "optimize_agent")]
    [Description("Guided loop: gather evidence from runs and traces, then submit ONE A/B-tested optimization theory for an agent.")]
    public static string OptimizeAgent(
        [Description("Optional agent name or id to optimize.")] string? agent = null) =>
        $$"""
        # Workflow: optimize an agent

        Theorize ONE concrete, evidence-backed change to an agent and submit it as an optimization
        theory. The backend runs a baseline-vs-candidate A/B test in the background and either promotes
        it to a reviewable proposal (pass rate improved) or invalidates it. You do not run the A/B test.

        ## 1. Resolve the agent and a suite
        - `list_agents`, match the named agent, then `get_agent` by id. Never pass a name where an id is
          expected. If nothing matches, say so and stop.
        - `list_suites` — a theory is validated against a test suite, so one is required. If the agent has
          no suite, explain that and stop (or use the `curate_suite` workflow to build one first).

        ## 2. Check what was already tried
        - `list_theories` — never resubmit an **Invalidated** idea; a **Validated** one shows what kind of
          change works here. Cite relevant prior attempts in your rationale.

        ## 3. Ground the theory in evidence (read first, never guess)
        - `list_test_runs` → `get_test_run` on the latest group → pick a run id → `get_run_failures`: the
          failing cases with each evaluator's reasoning. Name the failure pattern (wrong format? ignored
          constraint? missing knowledge? tone?).
        - `compare_runs` when there are two runs — see which cases moved; a regression cluster is evidence too.
        - `list_traces` / `get_trace` — inspect real captured calls (by text or httpStatus) when the suite
          alone doesn't explain a failure.
        - `get_agent_overview` — token/cost/latency trends, to motivate a model switch.

        ## 4. Pick exactly ONE change kind
        - **SystemPrompt** — failures look like missing instructions / wrong format / ignored constraints.
          Author a full rewritten system message (read the current one from `get_agent`, never retype it).
        - **ModelSwitch** — quality ceiling, or cost/latency. Provide another endpoint id (from `get_agent`).
        - **ToolUpdate** — the agent's tool definitions are wrong/missing. Provide the full proposed tools.
        Prefer the smallest change that fits the evidence. One theory = one change.

        ## 5. Submit and report
        - `submit_theory` with `agentId`, `suiteId`, `kind`, a one-sentence evidence-grounded `rationale`,
          `priority`, and the field for the chosen kind (`proposedSystemMessage` / `proposedEndpointId` /
          `proposedToolsJson`).
        - Then poll `get_theory` until it is **Validated** (links a new proposal) or **Invalidated**. Report
          the outcome plainly. If submission returns a duplicate or quota error, explain it; do not retry.

        Guardrails: ONE theory per request, never invent ids/models/numbers, never submit without evidence
        and a suite.{{Target(agent, "Target agent")}}
        """;

    /// <summary>
    /// Curate suite.
    /// </summary>
    [McpServerPrompt(Name = "curate_suite")]
    [Description("Build or grow a benchmark test suite from captured traces for an agent.")]
    public static string CurateSuite(
        [Description("Optional agent name or id to curate a suite for.")] string? agent = null) =>
        $$"""
        # Workflow: curate a test suite

        Turn real captured traffic into a benchmark suite. Each promoted trace becomes a test case whose
        expected output is the response that was actually recorded for that call.

        1. Resolve the agent: `list_agents` → match → `get_agent`.
        2. Find good traces with `list_traces` (filter by `agentId`, a `query`, or `httpStatus`). Inspect
           candidates with `get_trace` — pick representative, *correct* responses worth locking in as the
           expected behavior. Avoid error traces unless you are curating a regression case on purpose.
        3. Create or grow the suite:
           - New suite: `create_suite_from_traces` with a name, the `agentId`, and the chosen trace ids.
           - Existing suite: `list_suites` / `get_suite` to find it, then `add_trace_to_suite` per trace.
        4. Confirm with `get_suite` and summarize what the suite now covers.

        Guardrails: only promote traces from the current project; never invent trace ids.{{Target(agent, "Target agent")}}
        """;

    /// <summary>
    /// Runs the tests.
    /// </summary>
    [McpServerPrompt(Name = "run_tests")]
    [Description("Run a test suite against its agent and review the results.")]
    public static string RunTests(
        [Description("Optional suite name or id to run.")] string? suite = null) =>
        $$"""
        # Workflow: run a suite and review results

        1. Resolve the suite: `list_suites` → `get_suite` (check it has test cases and evaluators).
        2. `start_test_run` with the `suiteId`. NOTE: this makes real LLM calls against the agent's
           endpoint and incurs cost. It returns a run group; the run executes in the background.
        3. Poll `get_test_run` on the returned group id until its status is terminal
           (Completed / Failed / Cancelled).
        4. Review: for each run in the group, `get_run_failures` to see failing cases and evaluator
           reasoning; `compare_runs` against an earlier run to see what changed.
        5. Summarize the pass rate and the dominant failure patterns; if the agent underperforms, suggest
           the `optimize_agent` workflow.{{Target(suite, "Target suite")}}
        """;

    /// <summary>
    /// Review proposals.
    /// </summary>
    [McpServerPrompt(Name = "review_proposals")]
    [Description("Review open optimization proposals and approve, reject, or mark them adopted.")]
    public static string ReviewProposals() =>
        """
        # Workflow: review optimization proposals

        A proposal is an A/B-validated change waiting for a human decision. Proxytrace only *observes*
        traffic — it cannot apply a change to your agent's real implementation; the proposal's artifact is
        the handoff package for you to apply it.

        1. `list_proposals` — focus on **Draft** (open) ones.
        2. For each: `get_proposal` (kind, rationale, expected pass-rate delta), `get_proposal_artifact`
           (the concrete change to apply), and `get_theory` if you want the underlying A/B evidence.
        3. Decide with `set_proposal_status`:
           - **Accepted** — you intend to apply the change.
           - **Rejected** — not worth applying (say why).
           - **Adopted** — only after you have actually applied the change to the live agent.
        4. Summarize the decisions and, for accepted ones, what still needs to be applied from the artifact.

        Guardrails: act only on proposals in the current project; explain each decision in one line.
        """;

    /// <summary>
    /// Project insights.
    /// </summary>
    [McpServerPrompt(Name = "project_insights")]
    [Description("Survey the project's health: usage, cost, pass rates, and notable traces.")]
    public static string ProjectInsights() =>
        """
        # Workflow: project insights

        1. `get_dashboard` — project-wide totals: calls, token usage, average latency, overall pass rate,
           and a per-model breakdown. This is your headline.
        2. `list_agents`, then `get_agent_overview` on the busy/underperforming ones — token/cost/latency
           trends and the pass-rate trend over the last 30 days.
        3. `list_traces` (filter by `httpStatus` for errors, or a `query`) and `get_trace` to inspect
           specific calls behind an anomaly.
        4. Synthesize: what's healthy, what's degrading, what's expensive. Recommend concrete next steps —
           e.g. `curate_suite` to start measuring an agent, or `optimize_agent` where the pass rate is low.

        Read-only — never mutate anything in this workflow.
        """;
}
