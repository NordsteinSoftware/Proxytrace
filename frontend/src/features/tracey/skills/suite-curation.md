---
name: suite-curation
description: Build and edit benchmark test suites from captured traces. Load when the user wants to create a suite, turn traces into test cases, or add/remove/edit a suite's cases.
tools: list_suites, get_suite, find_traces, get_trace, create_suite, add_to_suite, remove_test_case, update_expected_output
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

- `create_suite` — a NEW suite for an agent, seeded from `agentCallIds`. Each trace becomes a case
  whose expected output is its own recorded response; a default exact-match evaluator is attached,
  so the suite runs immediately.
- `add_to_suite` — add traces to an EXISTING suite as cases (`list_suites` / `get_suite` to find it).

## Refine the cases

A trace's recorded response is rarely the *ideal* answer, so refine the cases that matter:

- `update_expected_output` — set what a case is scored against. Pass the `caseId` (from `get_suite`,
  which lists each case's id) and the corrected assistant text.
- `remove_test_case` — drop a case that isn't useful, by `caseId`.

A typical flow: `find_traces` → `create_suite` / `add_to_suite` → `update_expected_output` on the key
cases. To then run the suite, load the `test-suites-and-runs` skill.
