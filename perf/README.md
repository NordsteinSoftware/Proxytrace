# Performance testing

An **opt-in, run-on-demand** suite that exercises the real code paths against a **real Postgres seeded
with ~1M agent calls** and fails against **absolute budgets**. It exists because the unit suite runs on
the in-memory EF provider, whose query semantics (no real indexes, no `percentile_cont`) cannot surface
performance regressions. See [`docs/performance-testing.md`](../docs/performance-testing.md) for the
design rationale.

Nothing here is part of `dotnet test Proxytrace.sln` — the `.NET` projects are console apps, run
explicitly via `perf/run.sh` or the **Performance** GitHub workflow (`workflow_dispatch`).

## Scopes

| Scope | What it measures | How |
|-------|------------------|-----|
| `db-layer` | Statistics/list/histogram query latency (p95) + write-ingestion throughput, against the seeded DB | `Proxytrace.PerfHarness` (boots the real Storage+Application graph against Postgres, times the real readers) |
| `http` | Read endpoints (dashboard, agent-calls list, agent distributions) under concurrent VUs | `k6` against the running stack |
| `benchmarks` | Per-row JSON serialize/deserialize cost (pure CPU, no DB) | BenchmarkDotNet (`Proxytrace.Benchmarks`) |

## Run it

```bash
# full suite, ~1M rows (requires docker + dotnet; http scope also needs k6)
perf/run.sh

# quick smoke
perf/run.sh --size 100000 --scopes db-layer,benchmarks

# only the HTTP load test, heavier load, keep the stack up afterwards
perf/run.sh --scopes http --vus 25 --duration 60s --keep
```

`run.sh` boots a throwaway stack (`docker-compose.perf.yml`: Postgres on `:5433`, API on `:5230`),
seeds, runs the scopes, writes `perf/results/*.json`, and tears the stack down (`--keep` to leave it up).

## Budgets

All three scopes read [`perf-budgets.json`](perf-budgets.json) — the single source of absolute budgets.
Most are calibrated from a 1M-row dev run (set ~20–30% above the observed p95/mean); recalibrate on your
hardware. A missing entry means "measure but never fail", so new scenarios run before a budget is set.

**The suite is intentionally RED right now.** The `stats*` query budgets and the HTTP dashboard budget
are set to *target* values, not current measurements, because those aggregations client-evaluate at
scale ([#246](https://github.com/SyntaktikEU/Proxytrace/issues/246)) and measure ~4s. Expect those
metrics to FAIL until #246 lands; everything else passes. See `perf-budgets.json`'s `_comment_stats`.

## Components

```
Proxytrace.PerfHarness/   seeder + db-layer scenario runner (seed | db-layer | all)
Proxytrace.Benchmarks/    BenchmarkDotNet micro-benchmarks
load/read-endpoints.js    k6 HTTP load test (+ helpers/auth.js)
docker-compose.perf.yml   stack overlay (use with ../docker-compose.yml)
perf-budgets.json         absolute budgets, shared by every scope
run.sh                    orchestrator (mirrored by .github/workflows/perf.yml)
```
