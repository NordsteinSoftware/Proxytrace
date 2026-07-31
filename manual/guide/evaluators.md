# Evaluators

An **Evaluator** is a configurable scoring function. When a [test run](/guide/running-tests)
executes, each evaluator attached to the suite scores every test result and returns an
**evaluation**.

## Evaluator types

Proxytrace ships several evaluators, grouped into rule-based and LLM-based ("agentic")
families.

### Rule-based

| Evaluator | Scores by |
|---|---|
| **Exact Match** | The response equals the expected value exactly — text **and** tool calls. |
| **Numeric Match** | A numeric answer falls within tolerance of the expected number. |
| **JSON Schema Match** | The response conforms to an expected JSON schema. |
| **Tool Usage** | The agent called (or avoided) the expected tool(s). |

::: warning Exact Match now checks tool calls
Until version 1.11, **Exact Match** compared only the response *text*. An expected output that was
a tool call had no text, so it matched any response that made no tool call at all — the expectation
looked right in the UI but asserted nothing.

It now compares tool calls too: the tool name must match, and the arguments must be equivalent JSON
(key order and formatting don't matter, so `{"amount": 40}` and `{"amount": 40.0}` still match).
Several calls in one response are compared as a set, since parallel tool calls have no meaningful
order.

**If you already have cases with tool-call expectations, some may start failing after upgrading.**
That is the check finally running — look at the case before assuming the agent regressed. Past run
results are left as they were, so a suite's pass-rate trend can show a step at the upgrade.
:::

::: tip Generate the schema from an example
When creating a **JSON Schema Match** evaluator you don't have to write the schema by hand:
expand **Generate from an example JSON object** in the form, paste a sample of the JSON your
agent should return, and click **Generate schema**. Every key observed in the example becomes
a required property — loosen the generated schema by hand if some fields are optional.
:::

### LLM-based (agentic)

These use a model to judge qualities that rules can't capture:

| Evaluator | Scores by |
|---|---|
| **Helpfulness** | How well the response addresses the user's need. |
| **Safety Classifier** | Whether the response is safe / policy-compliant. |
| **Politeness** | Tone and courtesy of the response. |
| **Custom** | A criterion you define. |

LLM-based (agentic) evaluators require a **paid plan**. On the free tier they appear locked
(🔒) in the suite editor and can't be attached; if a suite already uses one when a plan lapses,
it is simply skipped during runs (it never errors a run).

## Default evaluators

Every project starts with a ready-to-use set of evaluators, created automatically — an
**Exact Match** evaluator plus the preset agentic judges (Helpfulness, Politeness, Safety
Classifier, Tool Usage). You don't need to set these up by hand; attach the ones you want to a
suite and start running. The agentic ones still require a paid plan to run (see above).

## Attaching evaluators to a suite

Evaluators are attached to [test suites](/guide/test-suites-and-cases) (many-to-many): a
suite can use several evaluators, and each evaluator can be reused across suites. Pick the
set that expresses what "correct" means for that benchmark. Agentic evaluators are locked on
the free tier.

## The evaluator workspace

Selecting an evaluator opens its workspace, which summarises how that evaluator has been
performing:

![The evaluator workspace: performance tiles and a pass-rate trend, the scoring definition, the score distribution, and the suites and agents it's attached to.](/screenshots/evaluators/workspace.png)

- **Performance** — pass rate (or average score for LLM judges), evaluation count, and a
  pass-rate trend over the chosen time range.
- **Score distribution** — a bar per score (Terrible → Excellent). **Click a bar to filter
  the recent-evaluations table** to just that score; click it again, or use the chip in the
  table header, to clear the filter.
- **Attached to** — the test suites and agents that use this evaluator. **Click any suite or
  agent to jump to it.**
- **Recent evaluations** — the latest results this evaluator scored. **Click a row to open
  that result in the run matrix**, with its detail drawer expanded.

**Deleting an evaluator** removes it from the workspace and from any test suites it was attached
to, so future runs no longer use it. Past test results keep their evaluations — the evaluator's
name and scores stay visible in the history you've already collected.

## The Evaluator Playground

The **Evaluator Playground** (in the sidebar under *Improve*) lets you probe a single
evaluator against a past test result and watch its score react to edits — without launching a
full run. It's a three-pane workspace:

![The Evaluator Playground: the selection rail of past evaluations, the bench with the editable candidate response, and the 1–5 verdict gauge with the judge's reasoning.](/screenshots/evaluators/playground.png)

1. **Selection rail (left).** Pick an evaluator, then one of its **past evaluations**. The
   list shows the evaluator's most recent scored cases; the search box reaches the rest of
   *this evaluator's* past evaluations (matching the case text or the judge's reasoning), with a
   preview that shows the logged verdict above the test case. On first open the first evaluator
   and its most recent past evaluation are selected for you, and your last selection is
   remembered so you land back on it when you return to the page.
2. **Bench (centre).** The picked case loads its input conversation (collapsed by default),
   the **expected / reference** answer, and the **candidate response** the agent produced. The
   candidate is editable — change it to ask "what would the judge say if the agent had answered
   like *this*?", then hit **Re-score** to run the evaluator live.
3. **Verdict (right).** A 1–5 gauge with the score band (Terrible → Excellent), the judge's
   written reasoning, and a **run history**. Selecting a past evaluation seeds the history with
   that case's **logged verdict** as the baseline, so you see its original score straight away.
   Each live re-score then stacks above it with the delta versus the previous run, so you can
   see exactly how an edit moved the score. Re-scoring an unedited response is a quick way to
   gauge a judge's consistency.

Rule-based evaluators (Exact Match, Numeric, JSON Schema) score here too — they return a score
without written reasoning.

## Reading evaluations

After a run, each test result carries one evaluation per evaluator. Use these to compare
agent versions and to feed [optimization proposals](/guide/optimization-proposals).
