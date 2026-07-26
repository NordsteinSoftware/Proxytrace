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

**The suite is green-expected end to end**, so a FAIL is a regression to chase rather than a known
signal. ([#246](https://github.com/SyntaktikEU/Proxytrace/issues/246) — the `stats*` aggregations
measuring ~4s at 1M — landed long ago: the cause was stale planner statistics after the bulk seed, the
seeder now `ANALYZE`s, and those budgets are real measured p95 plus headroom. See `_comment_stats`.)

Mind what the reported "p95" actually is when you calibrate: with `--iterations 10` (the default)
the 95th percentile of ten samples is **the slowest of the ten**, so it carries every scheduling
hiccup and background autovacuum the run happened to hit. A budget set just above one observed
measurement will therefore flap on a busier host or a noisier run — give short queries headroom over
their spread, not over a single sample ([#372](https://github.com/SyntaktikEU/Proxytrace/issues/372),
`_comment_anomaly`).

## Components

```
Proxytrace.PerfHarness/   seeder + db-layer scenario runner (seed | db-layer | all)
Proxytrace.Benchmarks/    BenchmarkDotNet micro-benchmarks
load/read-endpoints.js    k6 HTTP load test (+ helpers/auth.js)
docker-compose.perf.yml   stack overlay (use with ../docker-compose.yml)
perf-budgets.json         absolute budgets, shared by every scope
run.sh                    orchestrator (mirrored by .github/workflows/perf.yml)
```
