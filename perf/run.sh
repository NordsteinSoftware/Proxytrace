#!/usr/bin/env bash
# Performance suite orchestrator. Boots a throwaway stack, seeds ~N agent calls into real Postgres,
# then runs the requested scopes (db-layer / http / benchmarks), each failing on a budget breach.
#
#   perf/run.sh [--size N] [--scopes all|db-layer,http,benchmarks] [--vus N] [--duration 30s]
#              [--pages a,b] [--keep]
#
# Requires: docker, dotnet. The `http` scope additionally requires k6 (skipped with a warning if absent).
set -uo pipefail

cd "$(dirname "$0")/.." || exit 1
REPO_ROOT="$(pwd)"

SIZE=1000000
SCOPES=all
VUS=10
DURATION=30s
PAGES=""
KEEP=0

while [ $# -gt 0 ]; do
  case "$1" in
    --size) SIZE="$2"; shift 2 ;;
    --scopes) SCOPES="$2"; shift 2 ;;
    --vus) VUS="$2"; shift 2 ;;
    --duration) DURATION="$2"; shift 2 ;;
    --pages) PAGES="$2"; shift 2 ;;
    --keep) KEEP=1; shift ;;
    *) echo "unknown arg: $1"; exit 1 ;;
  esac
done

wants() { [ "$SCOPES" = "all" ] || [[ ",$SCOPES," == *",$1,"* ]]; }

CONN="Host=localhost;Port=5433;Database=proxytrace;Username=proxytrace;Password=proxytrace"
export PROXYTRACE_PERF_CONNECTION="$CONN"
COMPOSE="docker compose -f docker-compose.yml -f perf/docker-compose.perf.yml"
RESULTS="$REPO_ROOT/perf/results"
mkdir -p "$RESULTS"

NEEDS_STACK=0
if wants db-layer || wants http; then NEEDS_STACK=1; fi

cleanup() {
  if [ "$NEEDS_STACK" = 1 ] && [ "$KEEP" = 0 ]; then
    echo "[run] tearing down stack"
    $COMPOSE down -v >/dev/null 2>&1
  fi
}
trap cleanup EXIT

FAIL=0

if [ "$NEEDS_STACK" = 1 ]; then
  echo "[run] booting stack (postgres, redis, api)…"
  $COMPOSE up -d --build postgres redis api --wait || { echo "[run] stack failed to become healthy"; exit 1; }

  echo "[run] seeding $SIZE agent calls…"
  dotnet run --project perf/Proxytrace.PerfHarness -c Release -- seed --size "$SIZE" --connection "$CONN" \
    || { echo "[run] seed failed"; exit 1; }
fi

if wants db-layer; then
  echo "[run] === DB-layer scope ==="
  dotnet run --project perf/Proxytrace.PerfHarness -c Release -- db-layer \
    --connection "$CONN" --out "$RESULTS/db-layer.json"
  [ $? -ne 0 ] && FAIL=1
fi

if wants http; then
  echo "[run] === HTTP page-load scope ==="
  if command -v k6 >/dev/null 2>&1; then
    BASE_URL=http://localhost:5230 VUS="$VUS" DURATION="$DURATION" PAGES="$PAGES" k6 run perf/load/read-endpoints.js
    [ $? -ne 0 ] && FAIL=1
  else
    echo "[run] k6 not installed — skipping HTTP scope (install: https://k6.io/docs/get-started/installation/)"
  fi
fi

if wants benchmarks; then
  echo "[run] === Micro-benchmark scope ==="
  dotnet run --project perf/Proxytrace.Benchmarks -c Release -- --out "$RESULTS/benchmarks.json"
  [ $? -ne 0 ] && FAIL=1
fi

echo
echo "[run] results written to perf/results/ — overall: $([ $FAIL -eq 0 ] && echo PASS || echo FAIL)"
exit $FAIL
