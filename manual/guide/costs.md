# Costs & Budgets

The **Costs** page is where you see what your agents are actually spending — and where you set
limits so a runaway agent can't quietly burn through a month's budget. Open it from **Costs** in
the sidebar, under Monitor.

It answers three questions:

- **What have we spent this month, and where is it heading?**
- **Which agent is spending it?**
- **What happens when we hit the limit?**

## Where the numbers come from

Proxytrace does **not** store a price on each captured call. It stores the token counts, and works
out the cost whenever you look, using the prices configured on the
[model endpoint](/admin/providers-and-api-keys) the call ran on.

That has one very practical consequence: **if you correct a price, your whole history is repriced.**
Set the right price today and last month's figures become right too — there is nothing to
recalculate or backfill.

::: warning Endpoints without a price
A call that ran on an endpoint with **no configured price** contributes **nothing** to any figure on
this page — it cannot, since Proxytrace does not know what it cost. When that happens the page says
so with a notice above the summary. The totals are then an *understatement*, not an estimate, and
budgets built on them will fire late. Set prices on every endpoint you actually use.
:::

All amounts are in **EUR**. Budget periods are **calendar months in UTC** and reset on the 1st.

## The summary

The four cards across the top are the management view:

- **Month to date** — spend since the 1st, with the change against the same figure for last month.
- **Projected month end** — a straight-line projection from what you have spent so far. It stays
  blank for the first day or two of a month, because extrapolating from a few hours would produce a
  wild number rather than a useful one.
- **Previous month** — the last full calendar month, for comparison.
- **Selected window** — the total for whatever range the time picker is set to, plus how many
  budgets are currently blocking calls.

Below that, **Spend over time** charts the selected window stacked per agent, so one agent's spike
stands out from the background instead of hiding in a single total line. Use the bucket control
(five minutes / hourly / daily) to zoom from "what just happened" out to "how has this month gone".

**Spend by agent** breaks the same window down as a share ring plus exact figures, largest first.

## Monthly budgets

::: info Enterprise feature
Everyone can **see** this page and any configured budgets on every plan. **Setting** a budget
requires an **Enterprise** license and an administrator account. Without a license the budgets you
already configured stay visible and intact — they simply stop firing and stop blocking until the
license is restored. See [Licensing](/admin/licensing).
:::

A budget sets up to two EUR amounts for one calendar month:

| Threshold | What happens when spend reaches it |
|---|---|
| **Soft limit** | A **warning** [notification](/guide/notifications). Nothing is blocked. |
| **Hard limit** | A **critical** notification, **and** Proxytrace starts rejecting proxied LLM calls for that scope until the month resets or you raise the limit. |

Both are optional — a soft-limit-only budget is a pure early-warning system that never interrupts
anything, which is a good way to start.

### Project budgets and agent budgets

You can set **one budget for the whole project**, and optionally **one budget per agent** as an
override. An agent's spend counts toward *both* its own budget and the project budget, so the
project figure is always the complete picture.

::: warning Agent budgets need the agent header
Blocking an *agent* before its call reaches the provider means Proxytrace has to know which agent
is calling — and the only thing that identifies it that early is the
`x-proxytrace-agent` header your client sends (see [Proxy Setup](/guide/proxy-setup)). Traffic that
does not send that header **cannot** be caught by an agent budget.

The **project budget is the reliable backstop**: it applies to every call regardless of headers. If
budget enforcement matters to you, always set one.
:::

### Setting a budget

1. Open **Costs** and click **New budget**.
2. Choose the **scope** — the whole project, or one agent.
3. Enter a **soft limit**, a **hard limit**, or both. If you set both, the soft limit must not be
   above the hard one (it could never fire — the hard limit would block first).
4. Leave **Enabled** on and save.

Each budget renders as a **consumption meter**: a bar filled against the hard limit (or the soft
one, if that is all you set), a tick marking where the soft threshold sits, the exact spend so far,
and how much is left this month.

Editing a budget **clears its alert state**, so the next check re-evaluates against your new
numbers. That is what makes raising a hard limit actually unblock things — and it also means a
lowered soft limit can warn again in the same month.

Deleting a budget removes it and lifts any block it was applying. Switching a budget to
**disabled** does the same thing without losing the configuration.

## What a blocked call looks like

Once a hard limit is reached, Proxytrace rejects further proxied calls for that scope with an
HTTP **403** and an OpenAI-shaped error body:

```json
{
  "error": {
    "message": "Request blocked: the monthly cost budget for this project has been reached. Contact your Proxytrace administrator.",
    "type": "invalid_request_error",
    "code": "proxytrace_budget_exceeded"
  }
}
```

Most OpenAI-compatible SDKs surface this as an ordinary API error, so your application sees a
failed call rather than a hang.

Two things worth knowing:

- **The message never contains amounts.** An application holding an ingestion key is not
  necessarily entitled to know your organisation's spend, so the numbers stay on this page.
- **Blocked calls are still recorded** as traces, flagged as blocked, so you can see exactly what
  was refused and when. They never reach the provider, so they cost nothing.

## Timing: how quickly limits take effect

Spend is recomputed **periodically** (every five minutes by default), and the proxy caches the
block list briefly. In practice:

- A limit can be **overshot by a few minutes' worth of traffic** before calls actually stop. Set
  the hard limit slightly below the number you truly cannot exceed.
- **Raising a limit, disabling a budget, or deleting one** takes effect within about half a minute.
- **Renaming an agent** propagates to agent-scoped blocking within about half a minute too.

## On the 1st of the month

Nothing to do. Budgets are measured per calendar month in UTC, so at midnight on the 1st the new
month starts at zero: warnings re-arm and any blocks lift automatically. Last month's history stays
on the page.
