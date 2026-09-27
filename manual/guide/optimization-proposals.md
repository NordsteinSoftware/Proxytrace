# Optimization Proposals

An **Optimization Proposal** is a data-driven recommendation to improve an
[agent](/guide/agents) — for example, switching its model or updating its system prompt.
Each proposal is grounded in evidence from [test runs](/guide/running-tests).

> A proposal is only ever created by **validating an
> [optimization theory](/guide/optimization-theories)** with an A/B test run. The proposal
> records the baseline vs. candidate comparison (pass-rate, cost, latency) as its evidence.
> Theories are the unproven hypotheses; proposals are the ones that earned a measurable win.

## What a proposal contains

![A validated theory's dossier: the effective pass-rate gain with its p-value as the headline, the proposed system-prompt change shown as a diff beside the evidence, and the Approve for implementation / Dismiss decision bar.](/screenshots/proposals/detail.png)

- **Kind** — the type of change (e.g. switch model, update system prompt).
- **Rationale** — why the change is suggested.
- **Priority** — how impactful it is expected to be.
- **Details** — the concrete, typed change (e.g. the new model, or the proposed prompt
  text).
- **Evidence** — the specific test runs that justify the proposal.
- **Status** — `Draft` (awaiting review), `Accepted` (approved for implementation, awaiting adoption),
  `Adopted` (the change is live in the agent), or `Rejected` (dismissed).

## Reviewing proposals

Open **Proposals** in the sidebar. The page is a **review desk**: every reviewable proposal
belongs to a validated theory, queued under **Needs decision** in the left rail (see
[The review desk](/guide/optimization-theories#the-review-desk)). For each one:

1. Select the item — the first *Needs decision* entry is opened for you.
2. In the dossier, read the measured gain and its significance, inspect the A/B results and
   linked evidence runs, and compare the proposed change against the current agent definition.
3. **Approve for implementation** to accept it, or **Dismiss** to reject it — both in the pinned decision bar. Approving prepares the handoff; it does not change your agent.

## Approving = handoff, not auto-apply

Proxytrace sits between your agent and the model provider as an observing proxy — your
agent's actual system prompt, tool definitions, and model live **in your code**, and
Proxytrace cannot change them. Approving a proposal therefore does not modify your agent.
It marks the change as approved and gives you everything needed to apply it yourself:

- **Copy buttons** — the proposed system prompt verbatim, the proposed tool definitions as
  JSON, or the target model name, depending on the proposal kind.
- **Handoff doc** — a generated markdown document (copy or download) with the before/after
  change, the rationale, and the A/B evidence; ready to paste into a ticket or PR
  description.
- **Artifact API** — `GET /api/proposals/{id}/artifact` returns the same package as
  machine-readable JSON, for scripted workflows (e.g. a CI job that regenerates your agent
  config).

## Adoption tracking

After you approve a proposal, Proxytrace watches the agent's live traffic for the change:

- A **prompt or tool** proposal flips to **Adopted** when a request arrives whose system
  prompt / tool set matches the proposed change **exactly** (a new agent version is detected
  and linked — the queue shows *"Adopted in v{N}"*).
- A **model switch** proposal flips to **Adopted** when the agent's calls start arriving on
  the proposed model endpoint.

Detection is exact on purpose — if you applied a tweaked variant of the change (or
Proxytrace cannot see it, e.g. traffic got attributed to a different agent), use the
**Mark adopted** button on the approved proposal instead.

::: tip
Send the `X-Proxytrace-Agent` header with your agent's calls so traffic is attributed to the
right agent and adoption is detected reliably.
:::

## Deduplication

If you **Dismiss** or **Approve** a proposal, the optimizer remembers the exact change it
suggested. The next time it would surface an identical proposal (same agent + same
proposed change), it suppresses it instead of asking you again. The proposal is only
re-surfaced after **3 more completed test-run groups** have run against that agent — the
threshold acts as a "fresh evidence" check so you do not keep dismissing the same
suggestion every cycle.

Proposals with a *different* proposed change are not affected; only byte-for-byte
identical re-suggestions are suppressed.

## Closing the loop

Adoption is the final step of the Proxytrace loop: traffic was captured, curated into
benchmarks, evaluated, the results turned into a validated improvement, and Proxytrace
confirmed the improvement is now live in your agent.
