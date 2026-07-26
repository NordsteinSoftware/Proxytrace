# Deployment

Proxytrace is self-hosted with Docker.

To **install** it, you don't need this page: run the released image
(`ghcr.io/proxytrace/proxytrace`, also on Docker Hub as `proxytrace/proxytrace`) either
directly or with the pinned compose file shipped on every
[GitHub release](https://github.com/SyntaktikEU/Proxytrace/releases) — see
[Installation](/admin/installation). One image holds every process described below.

This page is about the shapes the app can be **run** in from a source checkout, which is what
the composes in the repository build, and what the release image packs into one container.

## Standard (split) deployment

The default `docker-compose.yml` runs the app split into services:

```bash
docker compose up --build      # API on :5100, frontend on :5101
```

In this shape the **frontend is served by a separate nginx container**
(`frontend/Dockerfile` + `frontend/nginx.conf`), which proxies `/api` **and `/mcp`** to the API
container. A **Redis** event broker carries real-time updates between the ingestion proxy and the app.

::: tip Custom reverse proxy
If you put your own reverse proxy in front of Proxytrace, forward **`/mcp`** to the API just like
`/api` (disable response buffering — the MCP server uses Streamable HTTP). Without it, external agents
can't reach the [MCP server](/guide/mcp-server).
:::

### Tuning trace ingestion throughput

Under high proxy volume the app drains captured calls from the Redis stream and persists them. Two
optional settings tune that consumer (split/Redis deployment only — kiosk's in-process channel
stays serial):

| Setting | Default | Effect |
|---------|---------|--------|
| `Messaging:MaxConcurrency` | `4` | How many captured calls are persisted in parallel. Raise it if the ingestion backlog (dashboard **Queue depth**) grows under load; each unit uses its own database connection. |
| `Messaging:BatchSize` | `64` | How many stream entries are read per round. |

The dashboard's live-telemetry **Queue depth** now reflects the real Redis consumer-group lag, so a
consumer that is falling behind is visible before the stream's retention cap (`Messaging:MaxStreamLength`,
default 1,000,000) trims unprocessed entries.

## Kiosk (single-process) deployment

Kiosk mode runs the app as a single process — the **.NET API serves the compiled frontend
from its `wwwroot/`** and no Redis is required. Useful for demos and simple installs.

```bash
docker compose -f docker-compose.kiosk.yml up --build   # API :5200, UI :5201, sample client :5202
```

Locally, `./dev.sh` runs the kiosk shape by default; `SPLIT=1 ./dev.sh` runs the split
shape with a throwaway Redis.

### Live showcase stack

The compose file ships a bundled **sample chat client** (`:5202`) and supports a **live LLM
endpoint** so a presenter can drive real calls through the kiosk and watch them appear as traces
in the web UI in real time.

| Service | Port | URL |
|---------|------|-----|
| Proxytrace API | 5200 | `http://localhost:5200` |
| Proxytrace UI | 5201 | `http://localhost:5201` |
| Sample chat client | 5202 | `http://localhost:5202` |

**Configuration.** Copy `kiosk.env.example` to `.env` at the repo root and fill in your LLM
credentials, then start the stack:

```bash
cp kiosk.env.example .env   # set KIOSK_LLM_BASE_URL, KIOSK_LLM_API_KEY, KIOSK_LLM_MODEL
docker compose -f docker-compose.kiosk.yml up --build
```

Without a `.env` (or with credentials omitted) the stack starts in **read-only demo mode**: the
API and UI boot normally but the OpenAI proxy route is not mounted (requests to `/openai/v1/*`
return `404`) and the sample client idles — useful for passive walkthroughs of the pre-seeded data.

In read-only mode the UI dims every control that would change something, and the app declines
those actions in the browser rather than letting them fail against the server — so a visitor who
clicks or tabs onto a disabled button gets a quiet "read-only demo" notice instead of an error.
The server enforces this independently, so the mode is not something the browser can talk its way
out of.

**Demo API key.** When a live endpoint is configured, the kiosk mints a fixed API key
(`pk-kiosk-demo` by default, overridable via `KIOSK_DEMO_API_KEY` in `.env`) shared by the
API and the sample client. Both containers read the same variable, so they stay in sync
automatically. See [Configuration](/admin/configuration#interactive-kiosk-real-llm-endpoint)
for the full `Kiosk:DemoApiKey` setting.

**Presenter runbook.** The step-by-step demo script — walk-through flow, the "trick" shortcut,
troubleshooting tips — ships in `sample-client/README.md`.

## The bundled manual (`/docs`)

This manual is built (VitePress) into the same web root the app serves and is reachable at
**`/docs`** in both deployment shapes:

- **Kiosk** — the manual is copied into `Proxytrace.Api/wwwroot/docs` and served by the
  API's static file middleware.
- **Split / nginx** — the manual is built into the nginx image at
  `/usr/share/nginx/html/docs`.

Both serve paths apply a strict Content-Security-Policy. The `/docs` path is configured to
allow exactly what the static manual needs, so it loads without CSP violations while the
app itself keeps its strict policy.

::: warning Deep links
The manual is built with file extensions kept (`cleanUrls: false`) so that requests like
`/docs/admin/database.html` resolve to real files and are not captured by the SPA's
catch-all route. Keep this setting if you customize the build.
:::

## Single API instance by design

Both deployment shapes run **exactly one API process**. This is intentional, and a few security
features depend on it: the two-factor **challenge tickets** and their per-attempt cap, the SSE stream
tickets, and the per-IP **rate limiters** on the sign-in / password-reset / MFA endpoints all keep
their state **in the API's own memory**, not in Redis.

::: warning Do not run multiple API replicas
If you put two or more API containers behind a load balancer, that in-memory state **partitions per
replica** — each instance counts attempts and holds tickets independently, which weakens the
brute-force protection and breaks two-step logins routed to a different replica than the one that
issued the challenge. Horizontal scale-out of the API is **not supported as-is**; it would require
moving this state into the shared Redis first. Scale the database and the ingestion **proxy**
independently as needed, but keep the API to a single instance.
:::

(The Redis broker in the split deployment carries real-time trace-ingestion events only; it does
**not** today back the auth/MFA/rate-limit state described above.)

## Health & security headers

The API sets security headers (CSP, `X-Frame-Options`, `X-Content-Type-Options`,
`Referrer-Policy`, `Permissions-Policy`) on every response; the nginx config mirrors them
for the split deployment. See [Configuration](/admin/configuration).
