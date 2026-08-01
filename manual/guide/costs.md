# Costs & Budgets

The **Costs** page is where you see what your agents are actually spending — and where you set
limits so a runaway agent can't quietly burn through a month's budget. Open it from **Costs** in
the sidebar, under Monitor.

It answers four questions:

- **What have we spent this month, and where is it heading?**
- **Which agent is spending it?**
- **Which API key is spending it?**
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

The page opens on **This month**, the period budgets are measured over, so the meters and the chart
agree until you deliberately widen the range with the time picker. The budget meters always read the
calendar month regardless of what the chart is showing — changing the range never moves them.

## The summary

The four cards across the top are the management view:

- **Month to date** — spend since the 1st, with the change against the same figure for last month.
- **Projected month end** — a straight-line projection from what you have spent so far. It stays
  blank for the first day or two of a month, because extrapolating from a few hours would produce a
  wild number rather than a useful one.
- **Previous month** — the last full calendar month, for comparison.
- **Selected window** — the total for whatever range the time picker is set to, plus how many
  budgets are currently blocking calls.

Below that, **Spend over time** charts the selected window as a stack, so one spike stands out from
the background instead of hiding in a single total line. Use the bucket control (five minutes /
hourly / daily) to zoom from "what just happened" out to "how has this month gone", and the
**By agent / By API key** toggle to change what the stack is cut by. Both views come from the same
window, so switching is instant.

::: tip The chart widens the bucket when it has to
A fine bucket over a wide window would be thousands of bars in a chart that draws a few hundred, so
the chart falls back to the next granularity that fits and says so above it — *"This window is too
wide for 5-minute buckets — showing daily spend instead."* Narrow the time range to get the finer
view back. Your bucket choice is remembered; it is not silently rewritten.
:::

**Spend by agent** breaks the same window down as a share ring plus exact figures, largest first —
*who* spent the money.

**Spend by API key** answers the neighbouring question: *which credential* spent it. That is
usually the more useful one when several applications, environments or customers share a project,
since each normally has its own key.

::: info Unattributed spend
The per-key list may end with an **Unattributed** row. That is spend Proxytrace cannot tie to one of
your keys, for either of two reasons:

- the caller authenticated with the **provider's own API key** rather than a Proxytrace-issued one
  (see [Proxy Setup](/guide/proxy-setup)), so there was no key of yours to record; or
- the call was captured **before per-key tracking existed** — older traces cannot be backfilled,
  because the information was never recorded.

It is shown rather than hidden so the per-key figures always add up to the project total. A key
budget cannot cap unattributed spend; the project budget is what holds it.
:::

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

### Choosing a scope

A budget covers exactly one of three things:

| Scope | Covers | Best for |
|---|---|---|
| **Whole project** | Every call in the project. | The backstop. Always set one. |
| **One agent** | Calls that identify themselves as that agent. | Capping a specific workload. |
| **One API key** | Calls authenticated with that key. | Capping one application, environment or customer. |

An agent's or key's spend counts toward *both* its own budget and the project budget, so the
project figure is always the complete picture. A budget cannot be scoped to an agent *and* a key at
once — pick the one that matches how you want to divide the money up.

::: warning Agent budgets need the agent header
Blocking an *agent* before its call reaches the provider means Proxytrace has to know which agent
is calling — and the only thing that identifies it that early is the
`x-proxytrace-agent` header your client sends (see [Proxy Setup](/guide/proxy-setup)). Traffic that
does not send that header **cannot** be caught by an agent budget.

The **project budget is the reliable backstop**: it applies to every call regardless of headers. If
budget enforcement matters to you, always set one.
:::

::: tip API key budgets cannot be bypassed
A key budget does not have that weakness. **Every** proxied call has to authenticate with a key, so
there is no header a client can omit to slip past it. If you need a cap that genuinely holds for one
application, scope it to that application's key.

The one gap: callers who authenticate with the **provider's own** API key instead of a
Proxytrace-issued one carry no key of yours to match against. That traffic is caught by the project
budget — and it is the same traffic that shows up as *Unattributed* in the breakdown above.
:::

::: warning Rotating a key resets its budget
Keys cannot be edited in place — rotating one means deleting it and creating a new one. The new key
is a **different** key as far as Proxytrace is concerned, so its budget does not carry over: the old
budget is removed with the old key, and the spend it had accrued becomes unattributed for the rest
of the month. Re-create the budget against the new key, and lean on the project budget in the
meantime.
:::

### Setting a budget

1. Open **Costs** and click **New budget**, top right of the **Monthly budgets** card.
2. Pick the **scope** — *Whole project*, *Agent* or *API key*. The dialog opens on a scope that is
   still free.
3. For an agent or key budget, pick **which one** in the second field. It is searchable, so type a
   few letters rather than scrolling a long list.
4. Enter a **soft limit**, a **hard limit**, or both, in EUR. If you set both, the soft limit must
   not be above the hard one (it could never fire — the hard limit would block first).
5. Leave **Enabled** on and save.

Each scope holds **at most one** budget. If you pick a scope that is already spoken for, the dialog
says so and Save stays disabled — edit the existing budget instead of adding a second one. The same
line tells you when a scope has nothing to point at yet, e.g. a project with no agents.

Each budget renders as a **consumption meter**: a bar filled against the hard limit (or the soft
one, if that is all you set), a tick marking where the soft threshold sits, the exact spend so far,
and how much is left this month. A budget you have just created shows **Measuring spend** until the
next reading of this month's figures arrives — a moment later, not a sign anything is wrong.

A budget's **scope is fixed** once created. To point a budget at a different agent or key, delete it
and create a new one; the editor shows the scope read-only.

Editing a budget **clears its alert state**, so the next check re-evaluates against your new
numbers. That is what makes raising a hard limit actually unblock things — and it also means a
lowered soft limit can warn again in the same month.

Deleting a budget (the bin icon on its row, with a confirmation) removes it and lifts any block it
was applying; the spend itself keeps being tracked. Switching a budget to **disabled** does the same
thing without losing the configuration.

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

One more thing worth knowing about per-key figures: they start from the day this feature was
installed. Spend captured before then is real and counts toward your project totals, but Proxytrace
cannot say which key produced it, so it appears as *Unattributed* rather than being guessed at.

## On the 1st of the month

Nothing to do. Budgets are measured per calendar month in UTC, so at midnight on the 1st the new
month starts at zero: warnings re-arm and any blocks lift automatically. Last month's history stays
on the page.
