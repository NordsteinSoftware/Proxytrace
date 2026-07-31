---
name: test-driven-improvement
description: Turn a defect the user reports in a specific trace into a failing regression test, then an A/B-validated fix proven against that test. Use when the user says an agent did something wrong — approved what it should have refused, ignored a policy or a limit, answered in the wrong format — especially when they name a trace.
tools: get_trace, find_traces, propose_test_cases, list_suites, get_suite, create_suite, add_to_suite, update_expected_output, list_evaluators, create_evaluator, set_suite_evaluators, start_test_run, get_case_results, compare_runs, list_theories, submit_optimization_theory, await_actions
---

# Skill: Test-driven agent improvement

The user has seen the agent do something wrong and told you about it. You turn that report into a
test that FAILS, then a change that makes it pass. Never the other way round: a fix proposed before
a failing test is a guess, and you will have no way to tell whether it worked.

Work the steps in order. Stop and say so the moment a step yields nothing real.

## 1. Reproduce before you believe it

A GUID the user pasted is authoritative — hand it straight to `get_trace` with `verbose: true` and
read the actual conversation. The default digest is metadata only and cannot show you what the agent
said. Take `agentId` from the result; you do not need `list_agents`.

Say back in one sentence what the agent actually did, quoting the transcript. **If the trace does not
show the reported behavior, say so and stop.** Do not invent a defect to be helpful.

## 2. Turn the complaint into a rule

Write the rule the agent broke as one sentence — "a refund must be refused when the request falls
outside the return window". Everything downstream is that sentence twice: as the answer the agent
should have given, and as the criterion the judge scores against. A vague rule makes a useless test.

## 3. Find or create the suite

`list_suites({ agentId })`, then `get_suite` on the plausible ones — its digest carries the existing
cases and the attached evaluator ids. Reuse a suite that already targets this class of behavior;
create one only when none fits. The reused suite will go red until the fix ships. That is correct:
the agent IS broken, and a suite that stays green while the user is reporting a bug is lying.

## 4. Pick the call that decided

The case must correct the turn where the agent **made** the wrong move, not the one that reports it.
Getting this wrong is the classic failure of this loop, so do not choose by eye — ask:

`propose_test_cases({ traceId, suiteId, instruction })`, passing the user's own complaint as
`instruction` ("it approved a refund outside the return window") and the suite from step 3. It reads
the whole conversation and returns ranked candidates; take the `Correction` whose rationale matches
the rule from step 2. It writes nothing, and its `expected` is a starting point you may sharpen.

**A candidate flagged `Unpassable` is a trap, not a warning to note and proceed past.** That call's
input already contains the completed tool calls *and* their results, and a run scores only the one
message written after those results came back — so no prompt change can ever make it pass. It stays
red forever and reads exactly like a fix that did not work. Take the earlier candidate whose own
response contains the wrong call. If every candidate is flagged, say so and stop.

A trace with no wider conversation is its own single turn: there is nothing to choose, and you can
correct it directly.

## 5. Write the failing case

Add the chosen `agentCallId` as a case whose `expectedOutput` is what the agent SHOULD have said — a
correction:

- existing suite → `add_to_suite({ suiteId, cases: [{ agentCallId, expectedOutput }] })`
- new suite → `create_suite({ name, agentId, cases: [{ agentCallId, expectedOutput }], evaluatorIds })`

**Never omit `expectedOutput` here.** Without it the case is seeded with the response the agent
actually gave — the bug itself — so the test passes on its first run and proves nothing. Keep the
`caseId` from `addedCases`; every later step needs it.

## 6. Give the suite a judge that can score the rule

`list_evaluators` first — the digest carries each agentic judge's instructions, so you can tell
whether one already covers this behavior. Reuse a fit. When step 4 returned an `evaluatorSuggestion`,
it has already named a judge that can score what it proposed — start from that rather than inventing
one. Otherwise `create_evaluator` with kind
`Agentic` and a system message naming the rule from step 2 ("fail any response that approves a refund
when the request falls outside the return window"). Use ExactMatch / NumericMatch / JsonSchemaMatch
only when the correct answer is genuinely deterministic.

Then attach it with `set_suite_evaluators({ suiteId, evaluatorIds })`. It REPLACES the set, so pass
every id you want, reading the current ones from `get_suite`. **A case passes only when every
attached evaluator passes**, so a leftover exact-match evaluator will keep failing a prose answer
that is completely correct — on a suite whose only judge is the default exact-match one, replace it
rather than adding beside it.

## 7. Red — run it, and require the failure

`start_test_run` on the suite. The app forces `await_actions` as your next step, so start everything
you need in the same step. The wait names each `runId`; use it. Do not go hunting for the newest run
in a list.

Then `get_case_results({ runId, caseIds: [yourCaseId], expect: 'fail' })` and read the verdict:

- **`fail`**, and the evaluator's reasoning describes the reported defect → red confirmed. Continue.
- **`pass`** → your test does not capture the bug. Fix the TEST, not the agent: sharpen the expected
  output or the judge's instructions, then run again. Do not submit a theory.
- **`evaluator-error`** → a judge crashed. That is not evidence the agent is wrong. Fix the evaluator
  and run again.
- **`unjudged`** or **`not-in-run`** → the case is not wired into the suite correctly; re-check with
  `get_suite`.

Present this card. The failing case is the finding the user came for.

## 8. Green — one change, A/B-validated, proven on your case

`list_theories({ agentId })` first; never resubmit an idea already invalidated. Then
`submit_optimization_theory` with a rationale citing the case and the evaluator's reasoning. Pick the
change kind from the evidence — a missing rule in the instructions is a system-prompt change.

The app forces `await_actions` next. When it returns, take `abTestRunId` — the A/B candidate run —
and call `get_case_results({ runId: abTestRunId, caseIds: [yourCaseId], expect: 'pass' })`. That
verdict is your green. Reach for `compare_runs(redRunId, abTestRunId)` when the user wants the wider
picture of what else moved.

**Read the theory status honestly, and never confuse it with your test.** A theory becomes a proposal
only when the whole suite improves beyond sampling noise, and one case moving can never clear that
bar — not on any suite size. So a theory that genuinely fixed the reported defect will usually come
back **Invalidated**. Say exactly that: the case now passes on the candidate run, and the
suite-level improvement was too small to spawn a proposal. If a proposal WAS created, name it.

Either way, remember Proxytrace only observes: it cannot change the real agent, so a human still has
to apply the change. Say so rather than implying the bug is fixed.

## Guardrails

- No theory without a confirmed red. The order is the whole point of this skill.
- One defect per request.
- The expected output is what the agent SHOULD have said. Never paste back the recorded response.
- Never correct a call flagged `Unpassable`. A case that cannot go green is worse than no case.
- The step numbers are your checklist, not the user's vocabulary. Offer "propose a fix and A/B-test
  it", never "move to step 8".
- Never invent ids — read them from the tools.
- Keep intermediate reads silent. Two cards carry this story: the red verdict and the green verdict.
