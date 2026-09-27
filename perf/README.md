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
| `http` | Page-load profiles (dashboard, traces, agents, suites, runs, costs, evaluators, anomalies) under concurrent VUs — each visit fires the page's mount requests as one batch | `k6` (`perf/load/read-endpoints.js` + `load/helpers/pages.js`) |
| `benchmarks` | Per-row JSON serialize/deserialize cost (pure CPU, no DB) | BenchmarkDotNet (`Proxytrace.Benchmarks`) |

## Run it

```bash
# full suite, ~1M rows (requires docker + dotnet; http scope also needs k6)
perf/run.sh

# quick smoke
perf/run.sh --size 100000 --scopes db-layer,benchmarks

# only the HTTP load test, heavier load, keep the stack up afterwards
perf/run.sh --scopes http --vus 25 --duration 60s --keep

# only the pages you care about (comma-separated; default: all)
perf/run.sh --scopes http --pages traces,dashboard
```

`run.sh` boots a throwaway stack (`docker-compose.perf.yml`: Postgres on `:5433`, API on `:5230`),
seeds, runs the scopes, writes `perf/results/*.json`, and tears the stack down (`--keep` to leave it up).

## Budgets

All three scopes read [`perf-budgets.json`](perf-budgets.json) — the single source of absolute budgets.
Most are calibrated from a 1M-row dev run (set ~20–30% above the observed p95/mean); recalibrate on your
hardware. A missing entry means "measure but never fail", so new scenarios run before a budget is set.

**The suite is green-expected end to end**, so a FAIL is a regression to chase rather than a known
signal. ([#246](https://github.com/NordsteinSoftware/Proxytrace/issues/246) — the `stats*` aggregations
measuring ~4s at 1M — landed long ago: the cause was stale planner statistics after the bulk seed, the
seeder now `ANALYZE`s, and those budgets are real measured p95 plus headroom. See `_comment_stats`.)

Mind what the reported "p95" actually is when you calibrate: with `--iterations 10` (the default)
the 95th percentile of ten samples is **the slowest of the ten**, so it carries every scheduling
hiccup and background autovacuum the run happened to hit. A budget set just above one observed
measurement will therefore flap on a busier host or a noisier run — give short queries headroom over
their spread, not over a single sample ([#372](https://github.com/NordsteinSoftware/Proxytrace/issues/372),
`_comment_anomaly`).

### HTTP page budgets

The page-load scope reports two kinds of series, both budgeted through this file: per-request p95s
(keep their `name` tag, so the **existing** `httpP95Ms` entries apply unchanged) and one
`page_load_<page>` trend per visit — the time until the page's slowest mount request returned. The
**page/endpoint metrics added with the page profiles have no budgets yet**: a missing key means
measure-only, so they produce numbers before thresholds are invented for them. Calibrate after a full
run by adding the endpoint p95s under `httpP95Ms` (short narrow reads ≈ 3× their db-layer twin, heavy
aggregates ≈ 2–2.5×) and each page's p95 under `httpPageP95Ms` (+20–30%); the k6 script picks both up
automatically (see `_comment_httpPages`).

## Components

```
Proxytrace.PerfHarness/   seeder + db-layer scenario runner (seed | db-layer | all)
Proxytrace.Benchmarks/    BenchmarkDotNet micro-benchmarks
load/read-endpoints.js    k6 page-load entry point (scenarios, thresholds, summary)
load/helpers/pages.js     page -> mount-request profiles + visit weights
load/helpers/auth.js      login/setup + id discovery for the load test
docker-compose.perf.yml   stack overlay (use with ../docker-compose.yml)
perf-budgets.json         absolute budgets, shared by every scope
run.sh                    orchestrator (mirrored by .github/workflows/perf.yml)
```
