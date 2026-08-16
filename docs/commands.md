# Commands

## Backend (.NET 10)
```bash
dotnet restore Proxytrace.sln          # Restore packages
dotnet build Proxytrace.sln            # Build all projects
dotnet test Proxytrace.Domain.Tests    # Run a single test project (the default — see below)
dotnet test Proxytrace.Domain.Tests --filter "FullyQualifiedName~TestRunGroup"   # one class/area
dotnet test Proxytrace.sln             # Run all tests (cross-cutting changes / releases only)
cd Proxytrace.Api && dotnet run        # Start API on http://localhost:5001
```

### Nordstein.Core

The shared Common, Domain, and Testing packages under [`core/`](../core/) are their own solution and are **not** part of
`Proxytrace.sln`. Building the product builds it too (through the project references), but it must
also keep standing alone — that is the property that keeps it extractable:

```bash
dotnet build core/Nordstein.Core.sln    # Core on its own
dotnet test  core/Nordstein.Core.sln    # Core's tests (not included in the Proxytrace.sln run)
```

`core/` is a **git submodule** of
[Nordstein.Core](https://github.com/NordsteinSoftware/Nordstein.Core). Clone with
`--recurse-submodules`, or the directory is empty and the build silently falls back to package
mode:

```bash
git clone --recurse-submodules <proxytrace-repo>
git submodule update --init        # if you already cloned without it
```

To check the product against the packed packages instead of the sources — what a consumer that has
no Proxytrace checkout sees:

```bash
dotnet pack core/Nordstein.Core.sln -c Release -p:NordsteinCoreVersion=0.1.0-dev -o core/artifacts
dotnet build Proxytrace.sln -p:UseLocalCore=false -p:NordsteinCoreVersion=0.1.0-dev
```

CI does both (`backend` and `core-package`). See [`code-reuse.md`](code-reuse.md).

**Scope your test runs.** The full solution run is ~2,200 tests across 10 projects and CI runs it on
every push — locally, run only the projects (or classes) affected by your change. See
[`testing.md`](testing.md#which-tests-to-run) for the mapping from changed code to test project.

Swagger UI is available at `http://localhost:5001/swagger` in Development mode.

The dev backend port is **5001** everywhere: `launchSettings.json`, `dev.sh`, the `Self:BaseUrl`
default, and the `/api` + `/mcp` proxy targets in `frontend/vite.config.ts`. Change one and you must
change all of them, or `npm run dev` proxies to a dead port.

## EF Core Migrations (PostgreSQL-only; supply a Postgres connection string at design time)
```bash
ConnectionStrings__Default="Host=localhost;Port=5432;Database=proxytrace;Username=proxytrace;Password=proxytrace" \
  dotnet ef migrations add <MigrationName> --project Proxytrace.Storage --startup-project Proxytrace.Api
dotnet ef database update --project Proxytrace.Storage --startup-project Proxytrace.Api
```

See [`database.md`](database.md) for full migration details.

## Frontend (React 19 / Vite, inside `frontend/`)
```bash
npm install
npm run dev         # Dev server on http://localhost:4201
npm run build       # Production build
npm test -- src/features/playground   # Vitest, scoped to the touched files (preferred)
npm test            # Vitest, all ~96 spec files (~3s — cheap enough to be the final check)
```

## All-in-one dev mode
```bash
./dev.sh            # Starts backend (5001) + frontend (4201)
```

The `./dev.sh` flow does not auto-seed; use the `/setup` page (or `SetupController`) to populate demo data.

## Release
```bash
# Cut a release (after moving CHANGELOG [Unreleased] under the new version heading):
git tag -a v1.2.3 -m "Proxytrace 1.2.3" && git push origin v1.2.3

# Build the released image locally (the all-in-one container) with the version injected:
docker build -f deploy/allinone/Dockerfile --build-arg APP_VERSION=1.2.3 -t proxytrace:1.2.3 .

# Run it exactly as a customer would — embedded Postgres/Redis, nothing to configure:
docker run -d --name proxytrace -p 5101:80 -p 5102:8081 -v proxytrace:/data proxytrace:1.2.3

# Run the customer deployment artifact locally (managed Postgres/Redis; .env optional):
cd deploy && docker compose up -d
```

See [`releasing.md`](releasing.md) for the full release pipeline (version SSOT, the single
released image, deploy artifact, changelog discipline).

## End-to-end tests (Playwright, inside `e2e/`)
The e2e suite boots the full stack via Docker Compose (`docker-compose.e2e.yml`).
**Do not run the e2e tests if Docker is not installed** — they require a working Docker daemon and
will fail without one. Check first (e.g. `docker --version` and `docker info`); if Docker is
unavailable, skip the e2e suite and say so. See the `run-e2e-tests` skill for how to execute and
triage them.

## Kiosk showcase demo (one-command boot)

Start the full demo stack — kiosk API, frontend, and sample chat client — with a single command:

```bash
docker compose -f docker-compose.kiosk.yml up --build
```

Ports:
| Service         | Host port | URL                        |
|-----------------|-----------|----------------------------|
| Kiosk API       | 5200      | http://localhost:5200      |
| Frontend        | 5201      | http://localhost:5201      |
| Sample client   | 5202      | http://localhost:5202      |

**Read-only mode (no `.env`):** the stack boots with in-memory storage and no real LLM endpoint.
The frontend is fully browsable; the OpenAI proxy route is not mounted (`/openai/v1/*` returns 404)
and the sample client idles.

**Demo data.** Seeding paints a business-scale deployment: a 14-day traffic backfill at per-agent
daily volumes (~1,300–1,700 interactions/day across the four demo agents, with production-sized
token counts, so cost/throughput cards read like a real installation), plus a continuous
**simulated live traffic feed** (`KioskLiveTrafficService`) that keeps fabricating agent calls
after boot — the dashboard's pulse band, live telemetry and recent-traces feed stay in motion
without a real LLM endpoint. Content, rates and volumes live in
`Proxytrace.Application/Demo/Internal/DemoTrafficCatalog.cs`, shared by the backfill and the live
feed so both describe the same business.

**Live demo mode:** copy `kiosk.env.example` to `.env` and fill in your LLM credentials:

```bash
cp kiosk.env.example .env
# Edit .env — set KIOSK_LLM_BASE_URL, KIOSK_LLM_API_KEY, KIOSK_LLM_MODEL
docker compose -f docker-compose.kiosk.yml up --build
```

`.env` variables (all optional — omit for read-only mode):

| Variable | Description |
|---|---|
| `KIOSK_LLM_BASE_URL` | Provider base URL, e.g. `https://api.openai.com/v1` |
| `KIOSK_LLM_API_KEY` | Provider API key |
| `KIOSK_LLM_MODEL` | Model name, e.g. `gpt-4o-mini` — feeds **both** the api service and the sample client |
| `KIOSK_LLM_KIND` | Provider kind: `OpenAi` \| `OpenAiCompatible` (default `OpenAi`) |
| `KIOSK_DEMO_API_KEY` | Proxytrace demo key shared by api and sample-client (default `pk-kiosk-demo`). Override only if you need a custom key — change here and nowhere else; both sides derive from this variable |

`KIOSK_LLM_MODEL` is deliberately shared between both services to prevent the registered endpoint
and the chat client from drifting to different models (which would cause ingestion to flip the demo
agent's endpoint mid-demo).

These three are the machine's **general-purpose local model config**, not kiosk-only: the
`prompt-lab` skill and `npm run i18n:translate` (see [`i18n.md`](i18n.md)) both fall back to them,
so one endpoint set up once serves every local tool that needs a model.

See `sample-client/README.md` for the demo script and walk-through.

## Manual toolchain (VitePress) — the Vite override

`manual/package.json` carries an `overrides` entry pinning **Vite ≥ 6.4.3**. VitePress 1.6.4 (the
current latest) still depends on `vite ^5.4.14`, and every Vite at or below **6.4.2** carries
[GHSA-fx2h-pf6j-xcff](https://github.com/advisories/GHSA-fx2h-pf6j-xcff) (path traversal in the dev
server's optimized-deps `.map` handling) plus two moderate advisories and the transitive esbuild one
— so `npm audit` in `manual/` reported one high and two moderates with **no automatic fix available**
([#373](https://github.com/NordsteinSoftware/Proxytrace/issues/373)). The exposure is the preview server a
contributor runs locally, not the shipped output (the manual builds to static HTML), but it should
not sit there indefinitely.

The override resolves VitePress 1.6.4 onto Vite 6.4.3 / esbuild 0.25.12; `npm run docs:build`,
`npm run docs:dev` and a clean `npm ci` (what both Dockerfiles do) all pass on it, and `npm audit`
reports **0 vulnerabilities**. Drop the override once a VitePress release depends on a patched Vite
on its own — check with `npm view vitepress dependencies`, then delete the `overrides` block, run
`npm install`, and confirm `npm audit` is still clean.

## Manual screenshots (Playwright + kiosk stack)
Add or refresh screenshots in the VitePress manual with the `manual-screenshots` skill
(`.claude/skills/manual-screenshots/SKILL.md`). It boots the self-seeded, login-free kiosk stack
(`docker-compose.kiosk.yml`, served at http://localhost:5201), captures with Playwright via
`manual/screenshots/capture-lib.mjs`, embeds the PNGs under `manual/public/screenshots/<page>/`, and
tears the stack down. **Docker required.**
