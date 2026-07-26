# Optimization Theories

An **Optimization Theory** is an *unproven hypothesis* that a specific change to an
[agent](/guide/agents) will improve it — a new system prompt, a different model endpoint,
or updated tool definitions. A theory is just an idea until Proxytrace validates it.

Theories are the front half of the optimization loop; validated theories become
[Optimization Proposals](/guide/optimization-proposals).

## Where theories come from

Any producer can submit a theory into the same validation pipeline:

- **Built-in optimizers** — analyse a completed [test-run group](/guide/running-tests) and
  hypothesise prompt, tool, or model-switch changes.
- **You, Tracey AI, and external callers** — submit theories through the API
  (`POST /api/theories`), naming the target agent, the suite to validate against, and the
  proposed change.

You watch every theory move through validation on the **Proposals** page — a review desk
whose queue groups theories by what they need from you (see
[The review desk](#the-review-desk)).

## What a theory contains

- **Kind** — the type of change (system prompt, tool update, or model switch).
- **Source** — who proposed it (optimizer, you, Tracey AI, or external).
- **Suite** — the [test suite](/guide/test-suites-and-cases) it will be validated against.
- **Rationale** — why the change is expected to help.
- **Status** — its position in the validation lifecycle.
- **A/B metrics** — once tested, the theory records the **baseline → projected pass rate**
  and a **p-value** (a two-proportion test of whether the difference is real or just sampling
  noise). A high p-value (≈ ≥ 0.05) means the change is *inside the noise*.

::: tip How much evidence the test has
Each side of the A/B is run **three times** and the results are pooled, so an eleven-case suite is
judged on 33 observations per arm rather than 11. That matters: on a single run, a genuine
improvement from 5/11 to 8/11 comes out at p ≈ 0.19 and would be dismissed as luck. The trade is
runtime — validation takes three suite runs per side.

An operator can lower the sample count, or drop the significance requirement entirely, for
environments where speed matters more than proof (the built-in product demo does exactly this).
Where the requirement is off, a theory can validate on the improvement alone; its p-value is still
recorded and shown, labelled **improvement only** instead of *significant*, so a win is never
presented as more certain than it is.
:::

## The validation lifecycle

```
Proposed → Validating → Validated   → becomes an Optimization Proposal
                     ├→ Invalidated  (no improvement; kept for the record)
                     └→ Failed       (the A/B test could not run; retry or dismiss)
```

1. **Proposed** — the theory has been accepted into the queue.
2. **Validating** — Proxytrace runs the suite with the proposed change applied (an
   ephemeral A/B run) and compares it against the current agent. These internal A/B runs
   are hidden from the [test run](/guide/running-tests) list by default so they don't clutter
   your own results — they live with the theory and its resulting proposal instead, and can be
   revealed with the **A/B runs** toggle.
3. **Validated** — the change improved the agent beyond sampling noise (p-value ≤ 0.05, unless
   your operator has relaxed that requirement — see above).
   A Draft **proposal** is created automatically, carrying the comparison as evidence.
4. **Invalidated** — the A/B test ran, and the change did not improve the agent (or the
   improvement was within the noise). The theory is kept so the same idea is not tried
   again needlessly.
5. **Failed** — the A/B test itself **could not run**: the provider was unreachable or
   unauthorized, the upstream timed out, or a run errored partway. A failed theory was
   *never actually tested*, so it is treated very differently from an invalidated one — it
   does **not** count against your win rate, it does not block resubmitting the same idea,
   and you can **retry** it once the underlying problem is fixed (or dismiss it).

You never lose work: every hypothesis — including the ones that did not pan out — is
recorded, which is also what powers deduplication.

## The review desk

![The Proposals review desk: the queue rail grouped by urgency on the left, the loop strip on top, and a selected theory's dossier with its gain, diff, and decision bar on the right.](/screenshots/theories/board.png)

Open **Proposals** in the sidebar. The page is a two-column **review desk**: a queue on the
left, and the selected item's full **dossier** on the right.

The queue groups theories by what they need from you, most urgent first:

- **Needs decision** — validated theories whose proposal awaits your Promote/Dismiss call.
- **Needs attention** — theories whose A/B validation **failed to run**. Each row offers a
  retry (once the provider issue is fixed) or a dismissal; until you act, the row stays
  visible instead of vanishing into History.
- **Awaiting adoption** — promoted proposals Proxytrace is watching live traffic for.
- **In flight** — theories that are queued (*Proposed*) or mid-A/B-test (*Validating*, with a
  live progress bar).
- **History** — everything decided (adopted, dismissed, or disproven), collapsed by default,
  with the **win rate** (share of tested theories that validated) in its header. Failed
  theories are excluded from the win rate — an outage is not a lost experiment.

Above the queue, the **loop strip** shows the whole optimization pipeline at a glance —
*testing → need decision → awaiting adoption → decided* — closing with the total **proven
gain** in percentage points. Click any node to jump to its group. A red **could not test**
node appears in the strip only when failed validations need your attention.

Each queue row shows the kind, rationale, and target agent; rows in *Needs decision* add the
measured pass-rate jump and the p-value verdict. Selecting a row opens its dossier:

- An **unproven** theory's dossier shows where validation stands, the **planned change**, the
  rationale, and the evidence runs that motivated it.
- A **validated** theory's dossier leads with what matters for the decision: the **effective
  gain** (the measured pass-rate jump, with the p-value verdict) as the headline, the
  **concrete change to apply** — the prompt diff, tool definition diff, or model swap — in the
  wide column, and the evidence (the full A/B result, source runs, and rationale) alongside.
  **Promote** and **Dismiss** sit in the pinned decision bar at the bottom.
- A **disproven or dismissed** theory keeps the same dossier with its (non-)improvement and
  verdict, so history stays inspectable.
- A **failed** theory's dossier states that the A/B validation could not run and that nothing
  was measured. Its decision bar offers **Retry validation** (re-queues the theory for a fresh
  A/B run) and **Dismiss** (files it away in History). If a run was already started before the
  failure, the **View A/B run** link stays available so you can diagnose what went wrong.

The page updates itself as theories move through the pipeline — it polls live while any theory
is still validating, so rows move between groups and recorded metrics appear without a manual
refresh.

The A/B validation detail carries a **View A/B run** link to the exact run that decided the
theory. The link is attached **as soon as the run starts**, so it is already present while the
theory is still *Validating*, and it stays on **rejected** theories too, so you can inspect the
worse run that caused the rejection. Following it opens the [runs](/guide/running-tests) view with
that A/B run revealed and selected.

## Dismissing a theory

You don't have to wait for the A/B test on every theory. Two actions in the dossier's decision
bar let you take a theory out of the pipeline yourself:

- **Reject** (on a *Proposed* theory) — dismisses it **without running A/B validation**. Use it for
  hypotheses you've already judged not worth a validation run.
- **Cancel validation** (on a *Validating* theory) — **aborts the in-flight A/B run** and dismisses
  the theory. Use it to stop a run you no longer need.

Either action moves the theory to **History**. Because it was never measured, the dossier shows
that it was "dismissed without running an A/B validation" rather than a pass-rate verdict — and
you can still **Reset to Proposed** later to validate it after all.

Theories validate **one at a time**: only a single A/B run is ever in flight, and the rest wait in
the queue. Rejecting queued theories or cancelling the running one is how you keep that backlog under
control.

## Re-validating a theory

A validated, dismissed, or failed theory's dossier carries a **Reset to Proposed** button
(labelled **Retry validation** on a failed theory). It
returns the theory to the start of the lifecycle — deleting any draft/dismissed proposal it
spawned, clearing the recorded A/B metrics, and re-queuing it for a fresh validation run. Use it
to retry a theory after the agent, suite, or model has changed — or after fixing the provider
problem that made a validation fail.

Reset is **not** offered once a proposal has been **promoted** (accepted): the change is already
applied to the agent, and resetting would not revert it. Dismiss-then-reset is fine; promoted
proposals are not resettable.

## Submitting your own theory

Send a `POST /api/theories` request naming the agent, the suite to validate against, your
rationale, and the proposed change (a new system prompt, a replacement endpoint, or updated
tools). Proxytrace deduplicates and validates it exactly like an optimizer-produced theory;
if it wins, it appears under **Needs decision** ready to promote, and you can follow it through
the queue as it moves through validation.

## Deduplication

A theory that is byte-for-byte identical to one already **Proposed**, **Validating**, or
**Validated** for the same agent is suppressed rather than re-run. Theories identical to an
already **Approved** or **Rejected** proposal are suppressed until **3 more completed
test-run groups** have run against that agent — the same "fresh evidence" threshold used for
proposals. A **Failed** prior does *not* suppress resubmission: its validation never ran, so
the idea remains untested.
