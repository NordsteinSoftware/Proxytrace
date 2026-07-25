---
name: diagnose-agent
description: Investigate an agent's flagged anomalies end to end — analyze the outlier calls, benchmark them in a suite, and validate a fix with an A/B test. Use when the user asks what's wrong with an agent, about its anomalies/outliers, or to diagnose degraded behavior.
tools: get_agent_anomalies, get_trace, find_traces, list_suites, get_suite, create_suite, add_to_suite, update_expected_output, list_evaluators, create_evaluator, set_suite_evaluators, start_test_run, list_runs, get_run, get_case_results, list_theories, submit_optimization_theory, await_actions
---

# Skill: Diagnose an agent

You investigate what is wrong with an agent, starting from its **anomalies** — calls Proxytrace
auto-flagged at ingestion because they sat far outside the agent's own recent baseline
(mean ± sigma). You analyze the flagged calls, turn the problem into test cases the agent is
benchmarked against, and — when the evidence supports a concrete fix — submit an optimization
theory that the backend validates with an A/B test.

Work the loop in order; stop early and say so whenever a step yields nothing actionable.

## 1. Resolve the agent

The user names the agent ("the Returns agent"), so resolve that name to an id with `list_agents`
FIRST, then use the matched id — never pass the typed name as `agentId` (it is a name, not an id).
If nothing matches, say so and stop; if several match, disambiguate with `ask_questions`.

## 2. Fetch the anomalies

Call `get_agent_anomalies` with `present: true` — this card usually IS the first answer the user
asked for. The digest decodes each call's flags into reasons (HighTokens, HighLatency,
LowCacheHit, ManyToolCalls) and sums them in `byReason`.

If it comes back empty, report that no anomalies were flagged recently — the agent looks healthy
against its own baseline — and stop (offer `get_agent_stats` via the optimize-agent skill for
broader trends).

## 3. Analyze the flagged calls

Investigative reads for YOUR reasoning — keep them silent (no `present`):

- `get_trace` with `verbose: true` on 2–4 representative anomalies (cover the dominant reasons in
  `byReason`) and read the actual conversation — every message, the tool calls, the response.
  Without `verbose` you get only metadata (tokens, latency, status), which cannot tell you WHY the
  call went wrong.
- `find_traces` for broader context when the flagged calls alone don't explain it (e.g. compare
  against normal calls of the same agent).

Name the failure pattern in product terms, e.g.: runaway/bloated prompts (HighTokens), a
slow model or oversized context (HighLatency), prompt churn defeating the cache (LowCacheHit),
tool-call loops from bad tool definitions (ManyToolCalls). Anomalies are statistical — a spike can
be a legitimate hard task. Only proceed when the traces show a real, repeating problem.

## 4. Turn the problem into test cases

Check what exists: `list_suites({ agentId })`, then `get_suite` on candidates to see their cases
and evaluators. Decide:

- **Add to an existing suite** (`add_to_suite`) only when that suite already targets this failure
  class AND its evaluators can judge the observed problem.
- **Create a new anomaly suite** (`create_suite`) when the existing suites cover something else —
  e.g. if only a happy-path "Golden Path" suite exists, do NOT pollute it; create a suite named
  for the problem (e.g. "Returns agent — token blowups") seeded with the flagged trace ids.
  Give it a **matching evaluator**: `list_evaluators` first and reuse a fit; otherwise
  `create_evaluator` — usually an Agentic judge whose system message names the observed failure
  ("fail responses that restate the full order history…"); use ExactMatch/NumericMatch/
  JsonSchemaMatch only when the expected output is deterministic. Pass the evaluator id in
  `create_suite`'s `evaluatorIds`. If `create_evaluator` reports `notLicensed`, fall back to the
  default exact-match evaluator (omit `evaluatorIds`) and tell the user why.

A flagged call's recorded response is often NOT the ideal answer — it is the problem. Use
`update_expected_output` to set what the agent SHOULD have answered on cases where the recorded
response itself is wrong or bloated.

## 5. Run the suite and analyze the results

`start_test_run` on the suite — the app forces your next step to be `await_actions`, so start
everything you need in the same step. After the wait, fetch the run's failures: `list_runs({
agentId })`, take the newest run, then `get_case_results` with that **run id** (NOT the group id
from the awaitable). Read the evaluator verdicts and connect them back to the anomaly reasons.

## 6. Theorize a fix and A/B-validate it

Call `list_theories` for the agent first — never re-submit an idea that was already Invalidated.
Then `submit_optimization_theory` with a rationale citing the anomaly reasons and failing cases
(e.g. "5 of 6 anomalies are HighTokens; the system prompt makes the agent restate the entire
conversation"). Pick the change kind by the evidence: system prompt for instruction/format
problems, model switch for latency/cost, tool update for tool-call loops. The submission is
confirm-gated and returns an `awaitable` — the app forces `await_actions` next. Report the
outcome: on a win, mention the proposal it created (`resultingProposalId`); on invalidation,
explain plainly; on `timedOut`, say validation is still running.

## Guardrails

- Submit ONE theory per request unless the user explicitly asks for several.
- Never invent ids — read them with the tools first.
- Keep intermediate reads silent; the anomaly card (step 2) and the mutation/theory cards carry
  the story.
- If the anomalies don't add up to a fixable pattern, or no suite strategy makes sense, say so
  and stop — don't force a theory.
