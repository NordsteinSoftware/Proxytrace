# Performance testing

Proxytrace must stay fast as stored data grows — fast ingestion (write hot path) and fast
statistics/queries (read hot paths) even at millions of `AgentCall` rows. The unit suite cannot guard
this: it runs on the **in-memory EF provider**, whose query semantics differ from Postgres (no real
indexes, no `percentile_cont`, LINQ-sort fallbacks), so it never sees a real query plan. The perf suite
under [`perf/`](../perf/) fills that gap.

It is **opt-in and run-on-demand** — never on push/PR. Everything lives in `perf/`; the `.NET` pieces are
console apps deliberately **excluded from `dotnet test Proxytrace.sln`** (they boot the real graph
against real Postgres, which the in-memory unit suite must never do).

## The three scopes

| Scope | Measures | Tool |
|-------|----------|------|
| **DB-layer** | Statistics/list/histogram query latency (p95) + write-ingestion throughput, against ~1M seeded rows | `Proxytrace.PerfHarness` console |
| **HTTP load** | Read endpoints (`/api/statistics/dashboard`, `/api/agent-calls`, `/api/statistics/agents/{id}/distributions`) under concurrent VUs | `k6` (`perf/load/read-endpoints.js`) |
| **Micro-benchmarks** | Per-row JSON serialize/deserialize cost (the EF value-converter hot path), pure CPU | BenchmarkDotNet (`Proxytrace.Benchmarks`) |

## How the DB-layer harness reuses real code

`Proxytrace.PerfHarness/Bootstrap/PerfModule.cs` mirrors the proven `Proxytrace.Application.Tests`
container — a bare Autofac container with **no `IHost`**, so the ingestion worker and seeder
`IHostedService`s never auto-start — but points `StorageConfiguration.Postgres(...)` at the perf
database. It then resolves and times the **real** readers (`IAgentCallStatsReader`, `IAgentStatistics`,
`IAgentCallRepository`) and the real write path (`IAgentCallRepository.AddAsync`, one `SaveChanges` per
call — the per-envelope cost the ingestion worker pays). Infrastructure seams (model client, email,
search) are substituted because ingestion only parses captured bodies and persists them.

### Seeding (`Seeding/PerfDataSeeder.cs`)

A small fixed graph (1 project, ~10 endpoints from one provider + distinct models, ~50 agents each with
a distinct prompt fingerprint) is built through the production generators. The ~1M `AgentCall` rows are
built via the `IAgentCall.CreateExisting` factory — which lets the seeder stamp a controlled `CreatedAt`
(the mapper copies it verbatim) so rows spread over ~90 days — with realistic token/latency
distributions, ~5% errors, and ~30% multi-turn conversation grouping, then inserted in batches through
the real `AddRangeAsync`. (Do **not** use `IDomainEntityGenerator<IAgentCall>` for the bulk loop — it
creates a fresh agent/version/endpoint per call.)

The canned assistant-message pool includes three tool-calling shapes (fixed names like `web_search`,
`run_sql`), so ~3/8 of successful calls carry `AgentCallToolEntity` rows through the real mapper —
~600k tool rows at 1M calls. These back the traces table's tool-name filter perf paths:
`agentCallsListByToolName` (the `EXISTS` semi-join) and `agentCallToolNames` (the project-scoped
`DISTINCT` picker), plus the four whole-table sorted-list metrics (`agentCallsListSort*`) that guard
the column-sort whitelist. See `_comment_traceSort` in `perf/perf-budgets.json` for the measured
plans and the not-yet-paid `NULLS LAST` index lever.

The seeder also loads `TestRunStats` projection rows (default ~25k, scaled down for small `--size`)
spread across ~250 synthetic suites, for the suite-scoped query the test-suites controller runs (#253).
Because `TestRunStatsEntity.TestRunId` is a 1:1 FK to `TestRunEntity`, one real anchor suite/group is
built and a `TestRun` is inserted per stats row; the stats `SuiteId` is a plain indexed column (no FK),
so the suite spread is synthetic and needs no per-suite graph. The `TestRunStatsQueryScenario` then
times the scoped read (`WHERE SuiteId IN (...)`) for a single busy suite (`testRunStatsBySuite`, the
single-suite GET) and a 50-suite page (`testRunStatsBySuitePage`, the suites list), plus the
dashboard's server-side aggregates over the same table (#288): the pass-rate totals
(`testRunStatsPassTotals`, a single scalar `GROUP BY` row) and the sparkline cohorts
(`testRunStatsRecentCohorts`, a `(GroupId, EndpointId)` `GROUP BY` ordered by `max(RunCompletedAt)`,
capped at 50). All four budgets are **uncalibrated placeholders** — set conservatively — until a
full run lands. (`StatsQueryTranslationTests` in the unit suite additionally locks these aggregate
shapes to server-side Npgsql translation via `ToQueryString`, without a live database.)

### Filtered-set summary (`agentCallsSummary`, `agentCallsSummaryByTimeRange`)

The traces KPI band (traces / tokens / cost / avg latency / error rate) is a **server-side aggregate
over every trace matching the filter**, not over the rows on screen — the list scrolls continuously,
so there is no page to summarize and a slice-scoped figure would climb as the reader scrolled.

That makes it the one trace query with no `LIMIT`, so it earns its own budgets: the unfiltered case
(`agentCallsSummary`) is a full-table aggregate at any size, and the time-ranged case
(`agentCallsSummaryByTimeRange`) is the state the UI actually opens in.

Its shape is the thing to protect. Cost is priced **per endpoint** (`ModelEndpoint.CalculateCost`),
so it cannot be a flat SQL `SUM`; the query instead `GROUP BY`s `EndpointId` and the domain layer
prices each group and folds them (`AgentCallSummary.Fold`). That is exact rather than approximate
because `CalculateCost` is linear in each token count. Latency likewise comes back as
sum + sum-of-squares + count so the standard deviation is derived in the domain layer — EF cannot
translate `stddev_samp`.

The consequence worth remembering: **what crosses the wire is O(endpoints), never O(rows)**.
`EXPLAIN` shows a `HashAggregate` over the scan emitting one row per endpoint, with `width=42` on the
scan — only the narrow scalar columns are read, never the request/response JSON. Measured p95 on a 1M
dev seed (2026-07) was 276.9ms / 170.6ms; budgets sit ~45% above. A jump toward seconds means either
the per-endpoint fold started round-tripping or the planner lost its statistics (see #246 below).

### Retention's session reconciliation (`sessionRemovalDeltas`)

Trace retention has to give the denormalized session counters back what the traces it deletes
contributed, or a session header keeps claiming traces its timeline can no longer show (#436). The
deltas come from `IAgentCallRepository.GetSessionRemovalsOlderThanAsync`, read **before** the delete
— afterwards the rows are gone.

It is budgeted because it is a new aggregate over the highest-volume table: a `GROUP BY SessionId`
over the same indexed `CreatedAt` range the delete uses, with the null-session rows excluded. Like
the summary above, **what crosses the wire is O(sessions in the window), never O(rows)**. The probe
deliberately passes a cutoff covering the whole seed, which is the worst case — the nightly sweep
only ever sees the tail beyond the retention window. Regression signature: a climb toward seconds
means the grouping stopped translating and started materializing the doomed rows client-side.

### Proxy credential resolution (`Scenarios/ApiKeyResolutionScenario.cs`)

The proxy resolves inbound credentials from storage on **every** proxied request (no positive
credential cache — a cached snapshot would delay key rotation/revocation, #407), so resolution is a
per-request hot path. The scenario idempotently seeds one Proxytrace-issued key and one dedicated
provider with a known upstream key, then times both resolution paths (`proxyResolveProxytraceKey`,
`proxyResolveUpstreamKey`) mirroring `ApiKeyResolver.ResolveAsync`'s repository call sequence, each
iteration in a fresh lifetime scope because per-request scope/DbContext construction is part of the
cost the proxy pays. The point of the budget is flatness: resolution is a handful of indexed point
lookups independent of `AgentCall` volume, and a breach signals a lost index or an accidental join
to a high-volume table. See `_comment_proxyResolve` in `perf/perf-budgets.json`.

### Cost aggregates (`statsCostByAgent`, `statsCostSeriesByAgent`)

The cost-budget guard and the Costs page each add a windowed aggregate over `AgentCallEntity`.
Both are the `statsCostEstimate` shape plus an **INNER JOIN onto `AgentVersionEntity`** — an
`AgentCall` carries no project of its own, so the `(project, agent)` grouping keys only exist on the
version row. Grouping additionally by `EndpointId` keeps cost derived (it is never persisted per
call; `CalculateCost` folds the token sums in C#), so the wire carries
`O(projects × agents × endpoints)` and `O(buckets × agents × endpoints)` rows respectively — never
`O(calls)`. Their committed budgets are **placeholders in the `statsCostEstimate` class, marked
RECALIBRATE**: measure the real p95 on your hardware with `perf/run.sh --size 1000000` and set them
to that + ~30-45%. Regression signature: a climb toward seconds means the join stopped translating
and version rows are being materialised per call, or planner statistics went stale after a bulk
seed. See [`cost-controls.md`](cost-controls.md).

### Per-key cost aggregates (`statsCostByApiKey`, `statsCostSeriesByApiKey`)

Key-scoped budgets and the Costs page's per-key breakdown add two more windowed aggregates.
`statsCostByApiKey` mirrors `statsCostByAgent` (same `AgentVersionEntity` join, the key id instead
of the agent in the grouping); `statsCostSeriesByApiKey` needs **no join at all**, since both its
grouping keys — the time bucket and `ApiKeyId` — are columns of the call itself, so its budget is
set tighter than its per-agent sibling to make a regression that reintroduced a join visible rather
than absorbed.

These two exist mainly to keep an explicit design decision honest: **`AgentCallEntity.ApiKeyId`
carries no index**, because it is only ever a `GROUP BY` key over a window already bounded by
`(project, CreatedAt)` — an index would buy nothing on the read side and cost a write on every
ingested call. If that assumption ever stops holding, these are where it shows.

The seeder attributes `ApiKeyRate` (85%) of calls across an `ApiKeyPoolSize` (12) pool of synthetic
key ids, leaving the rest in the null/unattributed group, so result cardinality is realistic rather
than one giant null group. The ids are synthetic because `ApiKeyId` is FK-free — no `ApiKeyEntity`
rows are needed. Both budgets are **placeholders marked RECALIBRATE**, same as the per-agent pair.

## Budgets (`perf/perf-budgets.json`)

The single source of absolute budgets, shared by all three scopes (the DB-layer runner and benchmarks
read it directly; k6 maps `httpP95Ms` onto its `thresholds`, which set the process exit code). A scope
exits non-zero on any breach. The committed values are **placeholders** — calibrate on the first full
~1M run. A missing entry means "measure but never fail", so a new scenario runs before its budget exists.

### Sizing a budget (#372)

Budget above a metric's run-to-run **spread**, not above a single observed sample — and know what the
reported number already is. `PerfReport.MeasureLatencyAsync` runs `--iterations` timed reps (default
**10**) after `--warmup` (default 2), and `Percentile(…, 0.95)` takes rank `ceil(0.95 × 10) = 10`: at
the default iteration count **the reported "p95" is the slowest of the ten samples**, so it already
carries every scheduling hiccup and background autovacuum pass that run happened to hit. Pass
`--iterations 50` when you want a genuine percentile to calibrate against.

The right multiplier therefore depends on the *size* of the metric, because host jitter is roughly
absolute while the budget is relative:

| Metric size | Headroom over observed p95 | Why |
|-------------|----------------------------|-----|
| Heavy aggregates (hundreds of ms) | ~20–30% | Jitter is a few percent of the cost — `statsCallTrends` measured 729.6 / 735.2 ms on two runs, `statsLatencyPercentiles` 840.1 / 800.2 ms |
| Short queries (sub-100 ms) | **3–4×** | The same absolute hiccup is a large fraction of the cost — a ~50 ms query doubles on a busy host while an ~800 ms one moves 5% |
| Sub-millisecond index reads | fixed 25–40 ms floor | Pure small-number/CI-jitter headroom (`agentCallsListBySession`, `statsPulse`, `agentCallToolNamesByAgent`) |

`anomalyTimeline` is the cautionary case: it was sized by the percentage rule (85 ms ≈ 1.3× an observed
64.8 ms) despite being a ~50 ms query. Two runs on 2026-07-17 measured 101.4 / 101.1 ms and went red;
four runs on the **same host** on 2026-07-26 measured 50.3 / 61.3 / 55.2 ms (and 52.9 ms over 50
iterations), with p50 pinned at 47.7–52.0 ms throughout. The elevated state is run-session-scoped —
host load or cache state, stable within a session and gone by the next — rather than a permanently
slower machine: the heavy aggregates measured identically on both days and `agentOverview` sits back
inside its reference band. The plan never changed and the p50 never moved; only the budget was wrong.
Recalibrated 2026-07-26 to
150 ms, in family with `agentCallsHistogram` (same bucketed-`GROUP BY` shape, same cost band, same
budget). Widening it costs no detection power: the regression it guards against is the loss of the
partial outlier index, which turns a ~30k-row index scan into a 1M-row full scan and lands in
full-window-aggregate territory (~270 ms+), far above the budget either way.

## Running

```bash
perf/run.sh                                   # full suite, ~1M rows
perf/run.sh --size 100000 --scopes db-layer   # quick smoke
```

`run.sh` boots `docker-compose.perf.yml` (Postgres `:5433`, API `:5230`), seeds, runs the scopes, writes
`perf/results/*.json`, and tears down. The API and the in-process harness **share one database** — the
harness seeds the rows the API serves. The statistics endpoints are project filters, not tenant security
boundaries, so the k6-bootstrapped admin sees all seeded data. CI: the manual **Performance** workflow
(`.github/workflows/perf.yml`, `workflow_dispatch`).

See [`perf/README.md`](../perf/README.md) for the operator-facing quick reference.

## First finding (issue #246) — stale planner statistics

On its first 1M-row run the suite measured the project-wide statistics aggregations
(`GetSummaryAsync`, `GetTokenUsageAsync`, `GetModelBreakdownAsync`, `GetCostEstimateAsync`,
`GetCallTrendsAsync`, `GetLatencyAsync`) at **3.7–4.4 s**. The first diagnosis (client-side evaluation
of `ulong?→numeric` token `Sum()`s) was **wrong**: `ToQueryString` and `EXPLAIN ANALYZE` show every one
of these translates to a single server-side `GROUP BY` / `sum` / `percentile_cont` and never reads the
JSON payload columns. The real cause was **stale planner statistics**: the seeder bulk-loads 1M rows in
one shot and (before the fix) never ran `ANALYZE`, so Postgres had no stats, defaulted to a wildly low
row estimate, and chose a **nested-loop plan that random-read the whole table** (≈3.5 s) instead of the
parallel seq-scan aggregate the same SQL runs once analyzed (≈270–480 ms). The author's `<1 ms` raw-SQL
baseline was on a settled, analyzed table — hence the apparent 1000× gap.

**Fixes (all landed):** the seeder now runs `ANALYZE` after the bulk load so the suite measures the
steady-state plan; migration `TuneAgentCallAutovacuum` lowers `autovacuum_analyze_scale_factor` on
`AgentCallEntity` so production stats stay fresh as the table grows; `GetLatencyAsync` uses a single
`percentile_cont(ARRAY[…])`. The `stats*` budgets are now **real measured p95 + headroom**, not targets.
After the fix everything is green except where noted: the three heaviest aggregates
(`statsLatencyPercentiles` ~880 ms full sort, `statsTokenUsage` / `statsCallTrends` ~780–880 ms bucketed
full scans) are **scan-bound** — no index helps a full-window aggregate, so sub-second at 10M+ would
require pre-aggregated rollups. A return to the nested-loop plan (e.g. a fresh restore without `ANALYZE`)
would blow past these budgets and the suite would catch it.
