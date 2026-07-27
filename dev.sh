#!/usr/bin/env bash
# Start backend and frontend together for local development.
# Demo data is seeded automatically when the database is empty.
#
# Default mode: single process (kiosk demo) — API + frontend, no Redis required.
# Split mode:   SPLIT=1 ./dev.sh — runs the production-shaped split (ingestion proxy + app +
#               Redis), so agent traffic flows through the standalone proxy. Requires Docker
#               (used to run a throwaway Redis + PostgreSQL) shared between the two hosts.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SPLIT="${SPLIT:-0}"
REDIS_CONTAINER="proxytrace-dev-redis"
POSTGRES_CONTAINER="proxytrace-dev-postgres"
PG_CONN="Host=localhost;Port=5432;Database=proxytrace;Username=proxytrace;Password=proxytrace"

cleanup() {
    echo ""
    echo "Stopping dev servers..."
    if [ "$SPLIT" = "1" ]; then
        docker rm -f "$REDIS_CONTAINER" >/dev/null 2>&1 || true
        docker rm -f "$POSTGRES_CONTAINER" >/dev/null 2>&1 || true
    fi
    # Kill all child processes in the process group
    kill -- -$$ 2>/dev/null || kill $(jobs -p) 2>/dev/null || true
}
trap cleanup EXIT INT TERM

echo "=== Proxytrace Dev Mode ==="

# Install frontend dependencies if node_modules is missing
if [ ! -d "$REPO_ROOT/frontend/node_modules" ]; then
    echo "Installing frontend dependencies..."
    (cd "$REPO_ROOT/frontend" && npm install)
fi

# Manual (VitePress) deps — the frontend dev server now also serves the docs at /docs.
if [ ! -d "$REPO_ROOT/manual/node_modules" ]; then
    echo "Installing manual dependencies..."
    (cd "$REPO_ROOT/manual" && npm install)
fi

if [ "$SPLIT" = "1" ]; then
    echo "Split mode: ingestion proxy + app + Redis + PostgreSQL"

    echo "Starting Redis (docker) on localhost:6379 ..."
    docker rm -f "$REDIS_CONTAINER" >/dev/null 2>&1 || true
    docker run -d --rm --name "$REDIS_CONTAINER" -p 6379:6379 redis:7-alpine >/dev/null

    echo "Starting PostgreSQL (docker) on localhost:5432 ..."
    docker rm -f "$POSTGRES_CONTAINER" >/dev/null 2>&1 || true
    docker run -d --rm --name "$POSTGRES_CONTAINER" -p 5432:5432 \
        -e POSTGRES_USER=proxytrace -e POSTGRES_PASSWORD=proxytrace -e POSTGRES_DB=proxytrace \
        postgres:16-alpine >/dev/null

    # App (consumer): real mode, consumes the Redis stream, owns the shared PostgreSQL schema.
    echo "Starting app (API) on http://localhost:5001 ..."
    (cd "$REPO_ROOT/Proxytrace.Api" && \
        ASPNETCORE_ENVIRONMENT=Development \
        Kiosk__Enabled=false \
        Messaging__Provider=Redis \
        Redis__ConnectionString=localhost:6379 \
        ConnectionStrings__Default="$PG_CONN" \
        dotnet run --urls "http://localhost:5001") &

    # Give the app a moment to initialize the database before the proxy reads from it.
    sleep 4

    # Proxy (producer): forwards agent traffic, publishes captured calls to Redis.
    echo "Starting ingestion proxy on http://localhost:5002 ..."
    (cd "$REPO_ROOT/Proxytrace.Proxy" && \
        ASPNETCORE_ENVIRONMENT=Development \
        Messaging__Provider=Redis \
        Redis__ConnectionString=localhost:6379 \
        ConnectionStrings__Default="$PG_CONN" \
        dotnet run --urls "http://localhost:5002") &
else
    # Start backend in development mode (single-process kiosk demo). Kiosk is opted into
    # explicitly here: appsettings.json ships Kiosk:Enabled=false so a plain `dotnet run` of the
    # published app never silently boots the login-free demo handler.
    echo "Starting backend on http://localhost:5001 ..."
    (cd "$REPO_ROOT/Proxytrace.Api" && \
        ASPNETCORE_ENVIRONMENT=Development \
        Kiosk__Enabled=true \
        dotnet run --urls "http://localhost:5001") &
fi

# Give the backend a moment to bind its port before the frontend proxy tries to connect
sleep 2

# Start frontend dev server
echo "Starting frontend on http://localhost:4201 ..."
(cd "$REPO_ROOT/frontend" && npm run dev -- --port 4201) &

echo ""
echo "Dev servers running:"
echo "  Frontend: http://localhost:4201"
echo "  Docs:     http://localhost:4201/docs/"
echo "  Backend:  http://localhost:5001"
echo "  Swagger:  http://localhost:5001/swagger"
if [ "$SPLIT" = "1" ]; then
    echo "  Proxy:    http://localhost:5002  (point client base_url here: /openai/v1)"
fi
echo ""
echo "Press Ctrl+C to stop all servers."

wait
