---
name: suite-curation
description: Build and edit benchmark test suites from captured traces. Load when the user wants to create a suite, turn traces into test cases, or add/remove/edit a suite's cases.
tools: list_suites, get_suite, find_traces, get_trace, create_suite, add_to_suite, remove_test_case, update_expected_output, list_evaluators, create_evaluator, set_suite_evaluators
---

# Skill: Suite curation

Turn real captured traces into a benchmark suite — the product's core loop. These are
**confirmation-gated** writes; call the tool and surface the resulting card.

## Find the traces

A suite is seeded from captured traces. Use `find_traces` (search by agent, text, or status) to
locate the interactions worth capturing — typically failures or notable cases — and `get_trace`
with `verbose: true` to read one in full (its whole conversation and response), which is what tells
you whether it is worth locking in as a case. You need their agent-call ids for the write tools
below.

## Build or extend

Both writes take `cases: [{ agentCallId, expectedOutput? }]` and report the case id each trace
produced in `addedCases`.

- `create_suite` — a NEW suite for an agent. A default exact-match evaluator is attached unless you
  pass `evaluatorIds`, so the suite runs immediately.
- `add_to_suite` — add traces to an EXISTING suite as cases (`list_suites` / `get_suite` to find it).

**Omit `expectedOutput` to lock in the response the agent actually gave** — that is a plain
promotion, and the case passes from the start. **Set it to author a correction**: the trace's input
with the answer the agent *should* have given, which is a case that fails until the agent is fixed.
Corrections are how a reported defect becomes a regression test; for that whole loop load the
`test-driven-improvement` skill instead.

## Refine the cases

A trace's recorded response is rarely the *ideal* answer, so refine the cases that matter:

- `update_expected_output` — set what an EXISTING case is scored against. Pass the `caseId` (from
  `get_suite`, whose digest lists every case) and the corrected assistant text. For a case you are
  adding right now, pass `expectedOutput` in the write instead — one call, not three.
- `remove_test_case` — drop a case that isn't useful, by `caseId`.
- `set_suite_evaluators` — change which judges score the suite. It REPLACES the set, and a case
  passes only when EVERY attached evaluator passes, so read the current ids from `get_suite` and
  pass the full set you want.

A typical flow: `find_traces` → `create_suite` / `add_to_suite`. To then run the suite, load the
`test-suites-and-runs` skill.
