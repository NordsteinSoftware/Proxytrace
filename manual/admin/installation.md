# Installation

This section is for operators self-hosting Proxytrace. For using the product, see the
[User Guide](/guide/getting-started).

Proxytrace ships as **one container image** holding the whole product: the web UI, the API,
the OpenAI-compatible ingestion proxy, PostgreSQL and Redis. There are two supported ways to
run it, and they use the same image.

| | [Single container](#single-container) | [Docker Compose](#docker-compose) |
|---|---|---|
| Install | `docker run …` — nothing to download | `proxytrace.zip` from the latest release |
| Database | Embedded PostgreSQL, in the `/data` volume | Its own `postgres` container, own volume |
| Best for | Evaluation, laptops, small installs | Production: back up, tune and upgrade the database on its own |

**Prerequisite for both:** Docker (Compose plugin only for the second). Nothing else — no
PostgreSQL, no Redis, no runtime to install.

## Single container

```bash
docker run -d --name proxytrace \
  -p 5101:80 -p 5102:8081 \
  -v proxytrace:/data \
  ghcr.io/syntaktikeu/proxytrace
```

That's the whole install. The container starts PostgreSQL, Redis, the API, the ingestion proxy
and nginx, applies the database migrations, and comes up ready.

- `5101` → the web UI (and this manual, at `http://localhost:5101/docs`).
- `5102` → the ingestion proxy your agents call.
- `/data` → **everything that must survive**: the database, the generated session signing key
  and secret-encryption key ring, and the search index. Back up this volume.

Open `http://localhost:5101` and follow the first-run setup to create the admin account, the
provider and the first project. The wizard ends with the **project-scoped endpoint** —
`http://localhost:5102/{project-slug}/openai/v1` — plus ready-to-paste client snippets; point
your agents' OpenAI base URL at that to start capturing traces. Keep the project segment: it is
what attributes calls when the client keeps using your **upstream provider key**. See
[Capturing Traces](/guide/capturing-traces) and [Proxy Setup](/guide/proxy-setup).

To override configuration, pass environment variables (`-e`). The two you are most likely to
need are the public URLs, once you serve Proxytrace on anything other than `localhost`:

```bash
docker run -d --name proxytrace \
  -p 5101:80 -p 5102:8081 \
  -v proxytrace:/data \
  -e PROXYTRACE_PUBLIC_URL=https://proxytrace.example.com \
  -e PROXYTRACE_PROXY_PUBLIC_URL=https://ingest.proxytrace.example.com \
  ghcr.io/syntaktikeu/proxytrace
```

`PROXYTRACE_PROXY_PUBLIC_URL` is what the UI advertises to clients as their OpenAI base URL
(setup wizard, API-keys page), so set it whenever the proxy is not reachable at
`http://localhost:5102`. Every setting is listed under [Configuration](/admin/configuration).

## Docker Compose

For production, run the same image against a PostgreSQL and a Redis container of your own, so
the database is an independent, backup-able, separately upgradable thing. Every
[GitHub release](https://github.com/SyntaktikEU/Proxytrace/releases) attaches a `proxytrace.zip`
(extracting to `proxytrace-<version>/`) with exactly that compose file — pinned to the release
— plus an `.env` template and a quickstart README. The latest is always at
`https://github.com/SyntaktikEU/Proxytrace/releases/latest/download/proxytrace.zip`.

```bash
# 1. Download and unpack the deployment artifact from the latest release
curl -fLO https://github.com/SyntaktikEU/Proxytrace/releases/latest/download/proxytrace.zip
unzip proxytrace.zip && cd proxytrace-<version>

# 2. Start it — no configuration required
docker compose up -d
```

Every setting has a working default: the Postgres container is internal-only with a default
password, the session signing key and the at-rest secret-encryption key ring are generated on
first start and persisted in the `appdata` volume, and without a license Proxytrace runs the
Free tier. To override anything (ports, public URL, your own database password — recommended
for production):

```bash
cp .env.example .env   # the template documents every value
# edit .env, then:
docker compose up -d
```

The compose file points the app container at the managed services with
`ConnectionStrings__Default` and `Redis__ConnectionString`. **Setting those is what switches
the embedded database off** — the image only starts its own PostgreSQL/Redis when they are
absent. The same lever lets you point a plain `docker run` at a managed database (RDS, Cloud
SQL, your own cluster):

```bash
docker run -d --name proxytrace \
  -p 5101:80 -p 5102:8081 -v proxytrace:/data \
  -e "ConnectionStrings__Default=Host=db.internal;Port=5432;Database=proxytrace;Username=proxytrace;Password=…" \
  -e Redis__ConnectionString=redis.internal:6379 \
  ghcr.io/syntaktikeu/proxytrace
```

## What runs inside the container

| Process | Purpose |
|---|---|
| nginx | Serves the web UI and this manual, reverse-proxies `/api` and `/mcp` |
| api | REST API, SSE, test runner, optimizer, ingestion consumer — applies schema migrations on start |
| proxy | OpenAI-compatible ingestion proxy your agents call |
| postgres | Database — **only when `ConnectionStrings__Default` is unset** |
| redis | Ingestion transport between proxy and api — **only when `Redis__ConnectionString` is unset** |

Database schema migrations apply automatically on start — both on first install and on
upgrades. See [Upgrading](/admin/upgrading).

Because all five run in one container, they also restart together: an API crash-loop cycles
trace ingestion with it. That is the trade for a single-command install; the Compose shape at
least keeps your database out of the blast radius.

## Where the image lives

The image is published to **two registries** on every release, from the same build — identical
tags (`X.Y.Z`, `X.Y`, `X`, `latest`), identical digests, both `linux/amd64` and `linux/arm64`:

| Registry | Image | Notes |
|---|---|---|
| GitHub Container Registry | `ghcr.io/syntaktikeu/proxytrace` | **Default.** What the shipped compose file pins. No anonymous pull-rate limit. |
| Docker Hub | `proxytrace/proxytrace` | Same image. Note Docker Hub rate-limits anonymous pulls per IP. |

Pin the exact `X.Y.Z` tag in production; `latest` is a convenience for evaluation.

## License

Without a license, Proxytrace runs the Free tier. To activate a license key, enter it
during the first-run setup wizard or later under **Settings → License** (applies
immediately, no restart), or set the `PROXYTRACE_LICENSE` environment variable. A key
activated in the UI takes precedence over the environment variable — see
[Licensing](/admin/licensing).

## Run from source (development)

For development or evaluation from a repository checkout you need the **.NET 10 SDK**,
**Node.js 20+**, and (for persistent runs) **PostgreSQL** — or use kiosk mode for a
zero-dependency in-memory demo (see [Database](/admin/database)).

From the repository root:

```bash
./dev.sh
```

This starts:

- **Backend** on `http://localhost:5001`
- **Frontend** on `http://localhost:4201`
- **Swagger** (Development only) on `http://localhost:5001/swagger`

It installs frontend dependencies if needed and seeds demo data on first run.

::: tip Split mode
`SPLIT=1 ./dev.sh` runs the production-shaped split — a standalone ingestion proxy, the
app, and Redis — so agent traffic flows through the standalone proxy. It requires Docker
(used to run a throwaway Redis). See [Deployment](/admin/deployment).
:::

### Run services individually

```bash
# Backend
cd Proxytrace.Api && dotnet run

# Frontend
cd frontend && npm install && npm run dev
```

### Build and test

```bash
dotnet restore Proxytrace.sln   # restore packages
dotnet build Proxytrace.sln     # build all projects
dotnet test Proxytrace.sln      # run all tests
```

Frontend (inside `frontend/`):

```bash
npm install
npm run build    # production build + type-check
npm run lint     # ESLint
npm test         # Vitest unit tests
```

## Next step

Configure the application — see [Configuration](/admin/configuration).
