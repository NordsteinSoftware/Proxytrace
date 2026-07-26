# Cost Controls

Agent cost tracking and alerting: the **Costs page** (a management summary of spend development)
plus per-project and per-agent **monthly spend budgets** with soft/hard thresholds, notifications,
and proxy-level hard enforcement.

## The derived-cost invariant (read this first)

**Cost is never persisted per call.** An `AgentCall` stores token counts; the EUR figure is always
re-derived at read time from those counts × the endpoint's *current* per-million prices
(`IModelEndpoint.CalculateCost`). Everything in this feature preserves that:

- Correcting a wrong price reprices history, and every budget follows automatically.
- There is no cost column to migrate, backfill, or keep consistent.
- The trade-off is that spend must be **recomputed** rather than read — hence the periodic guard
  below rather than a check on the ingest hot path.

The only cost figure that *is* persisted is the spend measured at the moment a threshold was
crossed (`ICostLimitBreach.SpendEur`), which is a historical fact about the crossing, not a
per-call cost.

## Entities

| Entity | Purpose |
|---|---|
| `ICostLimit` | The configuration: `Project`, optional `Agent` (null = project-wide), optional `SoftLimitEur`/`HardLimitEur`, `Enabled`. |
| `ICostLimitBreach` | The *state*: one row per (limit, month, threshold) that has fired. |

`CostLimit` carries **two partial unique indexes** rather than one composite: PostgreSQL treats
NULLs as distinct, so a plain unique `(Project, Agent)` would happily accept several project-wide
rows. Split by scope — `(ProjectId) WHERE "Agent" IS NULL` and `(ProjectId, AgentId) WHERE "Agent"
IS NOT NULL` — and each side gets a real guarantee.

Breach state lives in **its own entity**, not as a flag on the config row, so the background
guard's writes never race a user editing the thresholds. Its unique index
`(CostLimitId, MonthStart, Threshold)` makes a concurrent double-fire impossible at the database
level, so "has this alert already fired?" never depends on read-then-write timing.

**Row presence is the state**: a `Soft` row means the warning has already fired this month; a
`Hard` row means the proxy is blocking that scope for the rest of the month.

## Period and reset semantics

Budgets run on the **UTC calendar month** (`CostMonth.StartOf`, in `Proxytrace.Domain.CostLimit` so
the lean proxy — which never loads `Proxytrace.Application` — derives exactly the same key as the
guard that writes the rows).

**Nothing is cleaned up on the 1st.** The guard, the proxy's block lookup and the Costs page all
query *only the current month*, so on rollover the new month simply has no breach rows: alerts
re-arm and blocks lift by themselves. Old rows are kept as history.

Breach rows also deliberately survive retention pruning that drops month-to-date spend back below a
fired threshold — a breach is a fact about what happened, and is never un-fired.

## The guard

`Proxytrace.Application/CostControl/Internal/CostBudgetGuard.cs` — a `BackgroundService` mirroring
`TraceQuotaGuard`, ticking every `CostControl:GuardIntervalSeconds` (default 300). Per tick:

1. Unlicensed (`LicenseFeature.CostControls`) → return. Configuration is preserved; nothing fires.
2. `GetAllEnabledAsync()`; empty → return. This fast path means an install with no budgets never
   runs the spend query at all.
3. Month-to-date spend via `ICostStatistics.GetMonthToDateSpendAsync` → `IAgentCallStatsReader.GetCostByProjectAndAgentAsync`.
4. Effective spend per limit = the project total (sum of its agents) or the single agent's total.
5. Against the month's existing breach rows: crossing soft → insert a `Soft` breach + a **Warning**
   `NotificationKind.CostBudget` + `AuditAction.CostBudgetSoftLimitReached`; crossing hard → a
   `Hard` breach + a **Critical** notification + `CostBudgetHardLimitReached`.

Soft is evaluated before hard, so a single tick that vaults past both still tells the whole story.

**Budget notifications deliberately omit `TargetKind`/`TargetId`.** `NotificationService`
de-duplication is target-scoped but *kind-insensitive*, so an unacknowledged soft alert would
swallow the later hard alert for the same limit. The breach row already guarantees one alert per
threshold per month, so the de-dup is not needed and would only do harm.

## Spend queries

Two reader methods on `IAgentCallStatsReader` (implemented in `AgentCallStatsQueries`):

- `GetCostByProjectAndAgentAsync(filter)` — the guard's input and the page's agent breakdown.
- `GetCostSeriesByAgentAsync(filter, bucket)` — the cost-over-time chart.

Both join `AgentVersionEntity` (an `AgentCall` carries no project of its own — the project and
agent hang off the version row), group by `(…, EndpointId)` in SQL, and price the token sums in C#.
The wire therefore carries `O(projects × agents × endpoints)` and `O(buckets × agents × endpoints)`
rows respectively — never `O(calls)`. `StatsQueryTranslationTests` guards that both stay
server-side `GROUP BY`s, and `perf/` measures them (`statsCostByAgent`, `statsCostSeriesByAgent`).

`HasUnpricedEndpointsAsync` reports whether the window touched an endpoint with no configured
price. Those calls contribute nothing to any figure, so the page says the estimate is *incomplete*
rather than presenting it as a total.

## Proxy enforcement

New in `Proxytrace.Proxy/Internal/`, registered in `Proxytrace.Proxy.Module`:

- `IBudgetBlockProvider` / `CachedBudgetBlockProvider` — a clone of `CachedBlockingRuleProvider`:
  per-project TTL cache (`BudgetBlockCache:TtlSeconds`, default 30 s) including **negative
  caching**, wrapping `GetActiveHardBlocksAsync(projectId, currentMonthStartUtc)`. The month is
  part of the cache key so a rollover inside the TTL cannot keep serving last month's blocks.
- `IBudgetBlocker` / `BudgetBlocker` — pure scope matching, mirroring `RequestBlocker`.

The check is hooked into `OpenAiProxyController.Proxy` only (never `Passthrough`), **before** the
detector-blocking check — it needs no body inspection, and if the budget is spent no upstream
contact is wanted at any price. On a match the call gets a 403 with an OpenAI-shaped body
(`type: invalid_request_error`, `code: proxytrace_budget_exceeded`) and is still recorded as a
trace via `IngestMessage.BlockedByBudget` → `OutlierFlags.Blocked`. The 403 JSON doubles as the
`ResponseBody`, the same trick the detector path uses. A budget-blocked call never reaches the
provider, so it adds ~no tokens to the very spend that blocked it.

**Fail-open.** A database error in the provider logs and returns no blocks, uncached. A budget is a
cost control, not a security control — failing closed would take an organisation's LLM traffic down
on a transient database blip.

**Agent-scoped blocking matches the `x-proxytrace-agent` header only** (the same precedent as
`RequestBlocker.AppliesTo`) — it is the only attribution signal available before ingestion's
fingerprint matching. Unattributed traffic is therefore caught by **project-level** limits alone,
which makes the project budget the reliable backstop. Document that wherever agent budgets are
offered.

## Licensing and permissions

| Surface | Gate |
|---|---|
| Costs page, `GET /api/statistics/cost-overview`, `GET /api/cost-limits` | free, any project member |
| `POST`/`PUT`/`DELETE /api/cost-limits` | `Admin` role **and** `RequiresFeature(CostControls)` (402) |
| The guard and the proxy block | degrade silently when unlicensed |

The degrade is at **use time**, not entry time: an install that loses its license keeps its budget
configuration and restores enforcement the moment it is re-licensed. See
[`licensing.md`](licensing.md).

`PUT` clears the limit's breach state (`DeleteForLimitAsync`) so the next guard tick re-evaluates
against the new thresholds — without it, a limit raised after a hard breach would keep blocking,
because the breach row is what the proxy reads.

## Accepted trade-offs

- **Up to ~5.5 min of overshoot** past a hard limit (guard interval + proxy cache TTL) — inherent
  to recomputing spend periodically rather than on the hot path.
- **Endpoints without a configured price silently undercount spend** — surfaced on the page via
  `hasUnpricedEndpoints`, not guessed at.
- **An agent rename** propagates to the proxy's block list within one cache TTL.

## Related

[`licensing.md`](licensing.md) · [`notifications.md`](notifications.md) ·
[`audit-log.md`](audit-log.md) (actions 71–75) · [`performance-testing.md`](performance-testing.md)
· [`database.md`](database.md) (the `AddCostLimits` migration)
