# Running Tests

A **Test Run** executes a [test suite](/guide/test-suites-and-cases) against a specific
[agent](/guide/agents) version, producing per-case **evaluations** and aggregate metrics.

## Starting a run

1. Choose the suite to run.
2. Choose the agent (and version) to run it against.
3. Pick the model endpoints to run against — select **one to three** to compare models side by
   side in a single run.
4. Choose a **sample count (1–5)** — how many times to run *each* endpoint.
5. Start the run.

Each case in the suite is executed against the agent, and every attached
[evaluator](/guide/evaluators) scores the result.

::: tip Up to three endpoints
A suite runs against **at most three model endpoints** at once. Once three are selected the
remaining endpoints are disabled — deselect one to choose a different model.
:::

::: tip Sampling: catch flaky cases
Models are non-deterministic — a case that passes once can fail the next time. Set the **sample
count** above 1 to run each endpoint several times and **average the results per endpoint**, so a
case that only sometimes passes shows up as such instead of looking solidly green or red. The form
shows the total number of runs it will start (endpoints × samples, up to 15). Leave it at **1** for
a single run per endpoint — exactly the classic behaviour.
:::

## Watching progress

Runs stream their progress live via Server-Sent Events, without refreshing the page. A run
that is still in progress is highlighted in the **run list** on the left with an animated
accent ring and a pulsing **Running** tag, and settles to its final state on its own the moment
the run finishes — no refresh needed. The list shows the most recent runs first; use **Load
more** at the bottom to page through older ones.

The **run header** shows a progress bar with three pieces of information at a glance:
`cases done / total · overall % · estimated time remaining`. The ETA updates continuously
as judgements arrive, so you always know roughly when the run will settle.

The per-model cards also track **latency, cost, and tokens** live as each case lands, so you
can watch a run's spend accrue rather than waiting for the final totals. The latency shown is the
model's average per-case inference time — the actual time spent waiting on the model — not a
wall-clock timer over the whole run (which would also count queue wait and evaluator time).

In the **test case matrix**, each cell contains one slot per evaluator attached to the suite.
Slots start grey (queued) and fill with color as each evaluator reports back — green for pass,
red for fail, amber for error. You can watch individual judgements land in real time rather
than waiting for every evaluator in a case to finish before anything appears.

Pass rate is always shown over the cases **judged so far**, so the live number tracks
smoothly toward the final value — it never starts near zero and jumps at the end. Related
runs can be grouped together (a **test run group**) so you can compare, for example, the
same suite across several agent versions.

An **amber (errored) slot does not count against the case**. A judge that failed to answer says
nothing about your agent, so the case is decided by the evaluators that did return a verdict; only
when *every* evaluator on a case errored is the case left unjudged. Likewise, a run that could not
execute every one of its cases — an upstream outage part-way through, say — finishes as **Failed**
rather than Completed, so a partial run is never mistaken for a full one.

### A/B validation runs

When the optimizer validates an [optimization theory](/guide/optimization-theories), it
executes ephemeral **A/B runs** (baseline vs. candidate). These are hidden from the run list
by default to keep it focused on the runs you started. Toggle **A/B runs** above the list to
reveal them; revealed groups are tagged with an **A/B** badge. Opening a proposal's or
theory's *View run* link also reveals the linked A/B run automatically and selects it.

## Schedule periodic runs

::: tip Enterprise feature
Scheduling is part of the Enterprise tier. On the Free tier the scheduling controls are
unavailable; existing schedules stay listable but do not run.
:::

Instead of starting every run by hand, you can have a suite run **automatically on a recurring
interval** against a fixed set of model endpoints — for example, run your regression suite every
6 hours, or once a day. Each scheduled run behaves exactly like a manual one: it produces a test
run group, scores every case, and feeds the optimization loop.

<!-- TODO: add screenshot of the Scheduled tab (needs Docker kiosk) -->

Schedules live on the **Scheduled** tab of the Runs page:

1. Open the Runs page and switch to the **Scheduled** tab.
2. Click **New schedule** and fill in:
   - a **name** for the schedule,
   - the **suite** to run,
   - the **endpoints** (models) to run it against — **one to three**, the same cap as a manual run,
   - the **frequency** — **Hourly** at a chosen minute, **Daily** or **Weekly** at a chosen time of
     day, or a **Custom** "every N minutes/hours/days" interval. Times are **UTC**, and the dialog
     **previews the exact next run** date and time as you choose so there are no surprises,
   - whether it starts **enabled**.
3. Save. The schedule's card shows its cadence and the **next run** date and time.

Each schedule appears as a card with:

- a **toggle** to pause or resume it — pausing stops future runs without deleting the schedule, so
  you can resume it later with the same configuration;
- a **run now** action to trigger an immediate run without waiting for the next tick;
- a **recent-runs** strip summarizing the schedule's latest runs at a glance, so you can spot a
  schedule that has started failing without opening each run.

If a scheduled run is still in progress when the next interval comes due, that tick is skipped —
a schedule never stacks overlapping runs of the same suite.

## Reading results

### Test case matrix

![A completed test run: the per-model comparison cards, the evaluator breakdown, and the test-case matrix with per-model pass/fail results.](/screenshots/runs/results.png)

The matrix shows one row per case and one column per model (endpoint). Each cell contains
one slot per evaluator — the same layout is used whether the run targets a single model or
several. Click any cell to open the **comparison drawer**: it shows the case input, each
model's full output, and the per-evaluator verdicts side by side, so divergent behaviour is
easy to spot without leaving the page.

Each model column also has a **Request** button. It shows the exact request that run sends to
the model — the resolved model name, the full message list (with the agent's system prompt
merged in), and the **tool definitions** the model receives. Use it to confirm the agent is
offering the tools a case expects; if a tool is missing here, the model can't call it. The
request is rebuilt on demand from the agent's current configuration, so it reflects what a
re-run would send.

### Sampled runs

When you run with a **sample count above 1**, each endpoint still gets a single matrix column —
its sample runs are **averaged together**. The column header shows a **×N** badge, and each cell
shows a **pass fraction** (for example `4/5`) with a small dot per sample instead of one
pass/fail mark, so a flaky case stands out immediately. The column's footer pass rate is the mean
across cases, and the model-comparison cards read *"avg of N samples."*

A new **Flaky** filter (shown only for sampled runs) narrows the matrix to cases whose samples
**disagree** — the ones worth investigating. Clicking a sampled cell opens the comparison drawer
grouped by endpoint, with each sample labelled **sample i/N**, so you can read the individual runs
that produced the average.

Sampling stays out of the way of the rest of the loop: [anomaly detection](/guide/notifications)
and the [optimization](/guide/optimization-theories) loop treat each endpoint's samples as **one
representative run**, so a sampled run won't raise duplicate alerts or skew a proposal.

### Model comparison

![The model comparison: an in-production champion card with its headline pass rate, a candidate card reading as green/red deltas against production, and award medals for the highest pass rate, fastest, and cheapest model.](/screenshots/runs/per-model.png)

Every run — single-model and multi-model alike — opens with a **model comparison** at the top
of the results. The model you currently have **in production** (the agent's deployed endpoint)
is the **baseline**: a highlighted champion card showing its headline pass rate plus latency,
cost, and token totals.

Every other model in the run is a **candidate**, shown as a set of deltas measured *against
production* — pass-rate points, faster or slower, and cheaper or pricier — coloured green when
the candidate wins that metric and red when it loses. That makes it obvious at a glance whether a
candidate is worth switching to. Beneath the cards, three **award medals** call out the **highest
pass rate**, the **fastest**, and the **cheapest** model.

The pass rates update live while a run is in progress, but the deltas and medals are conclusions,
so they appear only once the entire run group has finished — during an active run the cards stay
neutral and no model is crowned. When a run doesn't include your deployed model (a
candidates-only comparison), the best performer stands in as the baseline, labelled **Baseline**
rather than *In production*.

## What runs feed into

Test runs are the evidence behind
[optimization proposals](/guide/optimization-proposals): a proposal references the specific
runs that justify it.
