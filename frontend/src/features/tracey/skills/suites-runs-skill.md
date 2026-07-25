---
name: test-suites-and-runs
description: Inspect test suites and runs, and start or cancel a test run. Load when the user asks about their suites, test runs, results/pass rates, or wants to run a suite against an agent.
tools: list_suites, get_suite, list_runs, get_run, get_case_results, compare_runs, start_test_run, cancel_test_run, await_actions
---

# Skill: Test suites & runs

Work with the project's benchmark suites and their executions.

## Read

- `list_suites` — the suites in the project; `get_suite` for one suite's cases and evaluators.
- `list_runs` — recent runs; `get_run` for a single run's per-case results and pass rate. It hides
  internal A/B validation (system) runs by default; pass `includeSystem: true` only if the user
  asks to see those.
- `get_case_results` — a run's FAILING cases with each evaluator's verdict + reasoning. Reach for
  this whenever the user asks *why* a run failed — don't stop at the pass rate.
- `compare_runs` — case-by-case movement between two runs (fixed / regressed / unchanged). Use it
  for any before/after question ("did the change help?").

Present the answer, don't narrate it: a single suite or run the user asked to see → its entity
card (`get_suite` / `get_run` with `present: true`); a comparison of runs or pass rates over time →
`show_chart` / `show_table`. Keep the reads you do to reach the answer silent (no `present`). Add at
most a sentence of insight.

To *build or edit* a suite (turn captured traces into test cases), load the `suite-curation` skill.

## Start a run

`start_test_run` runs a suite against an agent's endpoint. It is **confirmation-gated** — the app
shows a Confirm/Cancel card; call the tool and surface the result. You need both a `suiteId`
(`list_suites`) and an `agentId` (the agent is available via the core `list_agents` / `get_agent`).
If either is ambiguous, disambiguate with `ask_questions` before starting.

Once confirmed, the user sees a **live progress card** that streams completion and pass/fail as
cases finish. `start_test_run` returns an `awaitable` handle (`{ kind: "test-run", id }`).

**Waiting is not optional**: right after a step that started an action, the app forces your next
step to be `await_actions` — you cannot end the turn or call another tool first. So starting
several runs? Fire every `start_test_run` in the **same step** (parallel tool calls), then one
`await_actions([…all handles…])` — never one wait per run, and never poll `get_run` in a loop
yourself. When the wait returns, analyze the results and summarize the outcome. If it reports
`timedOut`, tell the user the run is still going and to check back.

`cancel_test_run` stops an in-progress run (pass the `awaitable` group id from `start_test_run`, or
a run group id) — reach for it when a run was started by mistake or is no longer wanted.

To go beyond a single run and actually *improve* an agent from its results, load the
`optimize-agent` skill instead.
