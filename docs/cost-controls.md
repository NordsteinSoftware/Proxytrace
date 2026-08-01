# Cost Controls

Agent cost tracking and alerting: the **Costs page** (a management summary of spend development)
plus per-project, per-agent and per-API-key **monthly spend budgets** with soft/hard thresholds,
notifications, and proxy-level hard enforcement.

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
| `ICostLimit` | The configuration: `Project`, optional `Agent`, optional `ApiKey` (both null = project-wide), optional `SoftLimitEur`/`HardLimitEur`, `Enabled`. |
| `ICostLimitBreach` | The *state*: one row per (limit, month, threshold) that has fired. |

A limit's scope is **exactly one of three** — project-wide, agent, or inbound API key. `Agent` and
`ApiKey` are never both set: a domain validation rule rejects it and the controller answers 400.
"Agent X *via* key Y" is a cross-product nobody asked for, and allowing it would break the
uniqueness guarantee below (such a row satisfies the agent-scope index while escaping the key one).

`CostLimit` carries **three partial unique indexes** rather than one composite: PostgreSQL treats
NULLs as distinct, so a plain unique `(Project, Agent, ApiKey)` would happily accept several
project-wide rows. Split by scope and each side gets a real guarantee:

| Index | Filter |
|---|---|
| `(ProjectId)` | `"Agent" IS NULL AND "ApiKey" IS NULL` |
| `(ProjectId, AgentId)` | `"Agent" IS NOT NULL` |
| `(ProjectId, ApiKeyId)` | `"ApiKey" IS NOT NULL` |

Note the project-scope filter names **both** columns — with the original `"Agent" IS NULL` alone, a
key-scoped row would have collided with the project-wide budget.

The `ApiKey` FK cascades: revoking a key takes its budget with it. The key's *traces* are
unaffected — `AgentCall.ApiKeyId` is deliberately FK-free (below) — so history survives.

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
   The per-key aggregate (`GetMonthToDateSpendByApiKeyAsync`) is fetched **only when at least one
   key-scoped limit exists**, so an install with none keeps exactly the tick cost it had before key
   scope existed — it is a second scan of the highest-volume table.
4. Effective spend per limit = the project total (sum of its agents), the single agent's total, or
   the single key's total.
5. Against the month's existing breach rows: crossing soft → insert a `Soft` breach + a **Warning**
   `NotificationKind.CostBudget` + `AuditAction.CostBudgetSoftLimitReached`; crossing hard → a
   `Hard` breach + a **Critical** notification + `CostBudgetHardLimitReached`.

Soft is evaluated before hard, so a single tick that vaults past both still tells the whole story.

**Budget notifications deliberately omit `TargetKind`/`TargetId`.** `NotificationService`
de-duplication is target-scoped but *kind-insensitive*, so an unacknowledged soft alert would
swallow the later hard alert for the same limit. The breach row already guarantees one alert per
threshold per month, so the de-dup is not needed and would only do harm.

## Spend queries

Four reader methods on `IAgentCallStatsReader` (implemented in `AgentCallStatsQueries`):

- `GetCostByProjectAndAgentAsync(filter)` — the guard's input and the page's agent breakdown.
- `GetCostSeriesByAgentAsync(filter, bucket)` — the cost-over-time chart, by agent.
- `GetCostByApiKeyAsync(filter)` — key-scoped budget input and the page's per-key breakdown.
- `GetCostSeriesByApiKeyAsync(filter, bucket)` — the same chart, cut by key.

All group by `(…, EndpointId)` in SQL and price the token sums in C#, so the wire carries
`O(projects × agents × endpoints)` / `O(buckets × agents × endpoints)` rows and their per-key
equivalents — never `O(calls)`. The three that need a project join `AgentVersionEntity` (an
`AgentCall` carries no project of its own); `GetCostSeriesByApiKeyAsync` needs **no join at all**,
since both its grouping keys (bucket, key id) are columns of the call itself.

The per-key aggregates are kept **separate** from the per-agent ones rather than folded in as an
extra grouping key: a combined aggregate would return the (agent × key) cross product, multiplying
the rows the guard reads every tick for two figures each wanted on its own.

`StatsQueryTranslationTests` guards that all four stay server-side `GROUP BY`s — including that
grouping by the *nullable* `ApiKeyId` does not push the aggregate client-side — and `perf/` measures
them (`statsCostByAgent`, `statsCostSeriesByAgent`, `statsCostByApiKey`, `statsCostSeriesByApiKey`).

### Telemetry and budget status are two reads, not one

`ICostStatistics` exposes them separately because they cost wildly different amounts and change for
wildly different reasons:

| Read | Endpoint | Aggregate scans | Invalidated by |
|---|---|---|---|
| `GetCostOverviewAsync` | `GET /api/statistics/cost-overview` | 7 | the window/bucket changing |
| `GetBudgetStatusAsync` | `GET /api/cost-limits/status?projectId=` | 1–2, **0** with no budgets | any budget create/edit/delete |

Folding the budget list into the overview meant a change to one ~200-byte configuration row
re-derived a payload that was ~90% telemetry (#491). The budget read needs only the month-to-date
per-agent aggregate, the per-key one **when a key-scoped limit exists** (the same conditional the
guard uses), and the month's fired thresholds.

`CostOverview.Bucket` reports the granularity the series was *actually* aggregated at:
`StatisticsTime.CoarsenToFit` widens the requested bucket until the window fits
`CostStatistics.MaxSeriesBuckets` (400, mirroring `MAX_BUCKETS` in `costSeries.ts`). It only ever
coarsens — an explicit choice that fits is honoured — so the wire never carries cells the chart is
guaranteed to discard: a month at the 5-minute bucket is 8,640 of them to draw 400 bars (#493).

### Breach state is read as a scalar

`ICostLimitBreachRepository.GetFiredThresholdsAsync(monthStart, projectId?)` returns
`FiredThreshold(CostLimitId, Threshold)` — the only two fields either caller ever reads. It is
deliberately **not** a mapped `ICostLimitBreach`: mapping resolves the full `ICostLimit` per row and
`CostLimitEntity` is not `[Cacheable]`, so it cost one serial round trip per fired threshold to
recover an id the caller already had (#492). Same reasoning, and same shape, as
`GetActiveHardBlocksAsync`.

`projectId` is optional and the distinction is load-bearing: the Costs page passes it (another
tenant's threshold crossings are neither its business nor its cost), the guard omits it because one
tick evaluates every project.

### Key attribution on the trace

`AgentCallEntity.ApiKeyId` is a nullable `uuid` with **no FK and no index**, both deliberate:

- **No FK** — same rule as `SessionId`/`ConversationId`. Revoking a key must never cascade away the
  irreplaceable telemetry it produced.
- **No index** — it is only ever a `GROUP BY` key over a window already bounded by
  `(project via AgentVersionId, CreatedAt)`, so an index buys nothing on the read side and costs a
  write on every ingested call. An index would only pay off for a traces-list *filter* on the key,
  which does not exist.

It is populated by `ResolvedApiKey.ApiKeyId` → `IngestMessage.ApiKeyId` → `AgentCallProcessor`.
There is **no backfill**: spend recorded before this shipped is unattributable, exactly like the
session precedent. The Costs page reports that remainder as an explicit **Unattributed** row rather
than dropping it, so the per-key figures always reconcile with the project total.

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

**Key-scoped blocking is the one scope that cannot be evaded**: every proxied request authenticates
with a key, so there is no header to omit. `BudgetBlocker` matches `ResolvedApiKey.ApiKeyId`
against the block's `ApiKeyId` by **id**, so no name lookup is needed on the hot path (contrast the
agent arm, which resolves the agent's name for header comparison).

The exception is the **upstream-key auth path**, where the caller presents the provider's own
credentials and no `IApiKey` exists — that traffic carries a null key id. The matcher requires the
block's key id to be non-null and compare equal, so two nulls deliberately do **not** match:
treating them as equal would silently turn one key's budget into a block on all unattributed
traffic. Such traffic falls to the project budget, the same backstop as header-less traffic.

## The budget editor (frontend)

`frontend/src/features/costs/` — the page is orchestration only; the rules live in three pure,
unit-tested modules.

**Scope is two decisions, not one.** `LimitDraft.scope` is a `DraftScope`
(`{ kind, elementId }`, `limitDraft.ts`) precisely because a half-filled scope — "agent chosen,
agent not yet named" — is a state the form passes through. `toLimitScope` narrows it back to the
saved `LimitScope` union, returning null while incomplete, which is what disables Save. The kind is
a `Select`; the element is a **`Combobox`** (searchable — an install can have hundreds of agents).

**Every scope holds at most one budget, including the project one.** `scopeAvailability.ts` is the
single source of that truth: `defaultScopeKind` opens the dialog on a scope that is actually free,
`isScopeAvailable` gates Save — kind *and* element, because the roster is live query data and the
picked agent can be deleted or budgeted in another tab while the dialog is open — `canCreateAny`
disables the "New budget" CTA when nothing is left, and `emptyReason` distinguishes *no agents exist
yet* from *every agent already has one*. All three scope kinds stay **selectable** in the picker on
purpose: the interesting question is *why* a scope is unavailable, and a disabled option withholds
exactly that answer.

**One create action, in the budgets card header** (`BudgetActionButton`, rendered as
`Card.Header action`). It is gated on `limitsLoading` as well: an empty limits list reads as "every
scope free", and `LimitEditor` seeds its scope once when it opens, so a click landing before
`/api/cost-limits` resolves could otherwise freeze the dialog on an already-taken scope.

**Editing shows the scope read-only, read off the saved `CostLimitDto`** (`agentName`/`apiKeyName`)
rather than looked up in the roster. The roster excludes budgeted agents, so a lookup could not
resolve the value being edited — which is how the picker once rendered a raw `agent:<uuid>`.

**Mutations patch the cached budget status before invalidating it** (`budgetPatch.ts`,
`useCostLimits.ts`). The meters render from `useBudgetStatus()`, and `upsertBudget`/`dropBudget`
fold the saved limit into that cached list so the row moves in the same tick as the toast — a
dialog closing, a success toast and an unchanged list is a response indistinguishable from "nothing
happened". The patch runs *before* the invalidation, since `refetchQueries` cancels an in-flight
request and a response already on the wire must not land on top of the optimistic row.

The **cost overview is not touched by a budget mutation at all** any more. That is the point of the
split above: the telemetry does not depend on which budgets exist.

A created budget's `monthToDateSpendEur` is left **null** — the meter's `measuring` state — until
the status refetch measures it. That is one cheap round trip, so nothing is gained by guessing, and
a fabricated €0 would read as "the full limit is still available" for a scope that may already be
over it. (The old code derived it from the charted window when that window happened to be the
calendar month; the read is now cheap enough that the special case earns nothing.)

**The window picker says "This month", not "All time"** (`CostToolbar`, via the picker's
`unboundedLabel` prop). An unbounded range is bounded to the current UTC month here
(`resolveCostWindow`) so budgets and chart agree on the period — deliberate, but a trigger reading
"All time" promised the full history and quietly showed one month (#493).

## Licensing and permissions

| Surface | Gate |
|---|---|
| Costs page, `GET /api/statistics/cost-overview`, `GET /api/cost-limits`, `GET /api/cost-limits/status` | free, any project member |
| `POST`/`PUT`/`DELETE /api/cost-limits` | `Admin` role **and** `RequiresFeature(CostControls)` (402) |
| The guard and the proxy block | degrade silently when unlicensed |

The degrade is at **use time**, not entry time: an install that loses its license keeps its budget
configuration and restores enforcement the moment it is re-licensed. See
[`licensing.md`](licensing.md).

**"Free for any project member" is a constraint on what the page may fetch.** Every query the Costs
page issues must itself be member-readable, and `app/queryClient.ts` sets `throwOnError: true`
globally — so a single admin-gated request does not degrade one card, it rethrows during render and
replaces the whole route with the error boundary. Two rules follow, both load-bearing:

- **Key names in the chart legend come from `CostOverviewDto.ApiKeyTotals`** (`apiKeyNames` in
  `costSeries.ts`), never from the Admin-only `GET /api/providers/overview`. The totals and the
  series are derived from the *same* window filter in `CostStatistics.GetCostOverviewAsync`, so
  every key the chart can plot is named there — and the payload already carries `ApiKeyName` and
  `KeyPrefix` for the per-key breakdown. A key revoked since is named by its raw id server-side;
  `apiKeyNames` drops those so the legend's short-id fallback runs instead of printing a GUID.
- **`useProjectApiKeys(isAdmin)` is admin-gated at the call site.** It reads the Admin-only
  providers overview, and the only thing that needs it is the budget **scope picker** — which lists
  every key including ones with no traffic yet, and which is admin-only anyway
  (`BudgetActionButton` renders nothing for a non-admin). It also sets `throwOnError: false`, so an
  unrelated failure degrades the picker instead of the page. See #490: the hook used to fire
  unconditionally, and a non-admin member got a 403 rethrown in render on a page documented as
  theirs to read.

`PUT` clears the limit's breach state (`DeleteForLimitAsync`) so the next guard tick re-evaluates
against the new thresholds — without it, a limit raised after a hard breach would keep blocking,
because the breach row is what the proxy reads.

## Accepted trade-offs

- **Up to ~5.5 min of overshoot** past a hard limit (guard interval + proxy cache TTL) — inherent
  to recomputing spend periodically rather than on the hot path.
- **Endpoints without a configured price silently undercount spend** — surfaced on the page via
  `hasUnpricedEndpoints`, not guessed at.
- **An agent rename** propagates to the proxy's block list within one cache TTL.
- **No per-key history before the feature shipped** — `ApiKeyId` is not backfillable (the
  information was never captured), so a key budget starts counting from deploy and older spend
  shows as Unattributed.
- **Key rotation resets a key budget.** `IApiKey` has no update path — rotating means deleting and
  re-creating, i.e. a new id — so the budget cascades away with the old key and the spend it
  accrued becomes unattributed mid-month. Deliberate: key lineage would be a new concept existing
  only to serve this, and the project budget still holds the total.

## Related

[`licensing.md`](licensing.md) · [`notifications.md`](notifications.md) ·
[`audit-log.md`](audit-log.md) (actions 71–75) · [`performance-testing.md`](performance-testing.md)
· [`database.md`](database.md) (the `AddCostLimits` migration)
