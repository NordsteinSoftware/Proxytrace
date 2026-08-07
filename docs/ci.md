# CI pipeline

How the GitHub Actions workflows decide what to run. Read this before adding a job, adding a
workflow, or changing a build cache — the gating rules below are not obvious from the YAML alone.

## The workflows

| Workflow | Trigger | What it does |
|---|---|---|
| `ci.yml` | PR, master push, `workflow_call` | Secret scan, frontend lint/test/build, backend build+test, all-in-one image build + boot smoke test |
| `e2e.yml` | PR, master push, `workflow_call` | Boots the full Docker stack and runs the Playwright suite |
| `codeql.yml` | PR, master push, weekly | Static analysis; findings land in the Security tab |
| `cache-cleanup.yml` | PR closed | Deletes the closed PR's Actions caches |
| `release.yml` | tag `v*.*.*` | Release gates, then publishes the image and the GitHub release. See [`releasing.md`](releasing.md) |
| `perf.yml` | manual only | The perf suite. Never runs on push/PR. See [`performance-testing.md`](performance-testing.md) |

## What runs when

Jobs are gated on which areas a diff touches, computed by the local composite action
[`.github/actions/detect-changes`](../.github/actions/detect-changes/action.yml).

| Job | PR | master push | Release gate |
|---|---|---|---|
| `secrets` | always | skipped | always |
| `frontend` | if `frontend/**` changed | if `frontend/**` changed | always |
| `backend` | if .NET sources changed | if .NET sources changed | always |
| `image` | if backend/frontend/`deploy/**`/Dockerfiles changed | skipped | always |
| e2e | unless the diff is purely prose | unless the diff is purely prose | always |
| CodeQL | only the affected languages | all languages | n/a |

Three rules explain the table:

- **master keeps `frontend`, `backend` and e2e as a post-merge safety net.** A PR is verified
  against the base it was opened on; if master moved underneath it, the merge can still break. Tests
  are what catch that — both the .NET suite and the frontend's Vitest suite, which is why `frontend`
  is no longer PR-only. Its lint and build steps ride along on the same `npm ci` rather than paying
  for a second install; packaging and the secret scan cannot regress from merge order alone, so the
  PR run is the only time they need to run.
- **e2e is barely gated on purpose.** 98% of merges touch backend or frontend, so a path filter
  would almost never skip it and skipping it wrongly is expensive. Only a diff that is entirely
  `docs/**`, `manual/**` or `**.md` misses it, via `paths-ignore`.
- **CodeQL subsets only on PRs.** A master push or the weekly run feeds the Security tab's baseline,
  where a partial scan reads as "alert resolved" for every language that did not run.

`detect-changes` **fails open**: when the diff range cannot be resolved (tag push, brand-new branch)
every area reports changed. A change under `.github/` also forces everything, so a commit cannot
rewrite the gates and skip its own verification in the same run.

### The backend job needs a container runtime

`backend` sets `PROXYTRACE_REQUIRE_DOCKER_TESTS=true` on its `dotnet test` step. A few backend tests
start a throwaway container (Testcontainers) to exercise a real service — currently the Redis
ingestion transport. Those tests **skip themselves** when no container runtime is reachable, so
`dotnet test` never becomes a hard Docker dependency for a local run; the variable flips that skip
into a hard failure. GitHub-hosted runners always have Docker, so the only thing it can catch is a
runner that lost it — which would otherwise silently drop the coverage rather than report it. See
[`testing.md`](testing.md#container-backed-tests).

### Adding a job to `ci.yml`

Gate it as `inputs.full || <your condition>`. Inside a reusable workflow `github.event_name` reports
the **caller's** event — a release is a tag push, so an `if: github.event_name != 'push'` check reads
`'push'` during a release and silently skips the job in the gate that is supposed to guarantee it.
`release.yml` passes `full: true` for exactly this reason.

## Build caches

Two separate systems, and the split matters:

- **Actions cache** (`actions/setup-*`, capped at **10 GB repo-wide**, LRU-evicted): NuGet restore
  and npm. Nothing else should go here.
- **Registry cache** (`ghcr.io/nordsteinsoftware/proxytrace-buildcache`, free and uncapped for a public
  package): every Docker layer cache.

The split exists because it was previously violated. Two `cache-to: type=gha,mode=max` image builds
grew to 8.36 GB of the 10 GB budget across 560 entries and continuously evicted the .NET and npm
caches, so `backend` and `frontend` were restoring nothing and rebuilding cold.

**Never point a Docker build's `cache-to` at `type=gha`.** Use a registry ref with
`ignore-error=true`, which lets a fork PR — whose `GITHUB_TOKEN` cannot write packages — degrade to
a slow build instead of a failed one.

| Cache ref | Written by |
|---|---|
| `:allinone` | `ci.yml` job `image` (amd64 only) |
| `:release-allinone` | `release.yml` (multi-arch; separate so the two do not evict each other) |
| `:api`, `:proxy`, `:frontend` | the e2e stack, via `docker-compose.ci-cache.yml` |

The e2e stack's cache lives in [`docker-compose.ci-cache.yml`](../docker-compose.ci-cache.yml), a
CI-only overlay layered on top of the base and e2e compose files. It is kept out of
`docker-compose.yml` so a local `docker compose up` never reaches for a remote registry, and it
needs `COMPOSE_BAKE=1` — that is what routes the build through buildx bake, which is what honours
`cache_from`/`cache_to`. Without it the stack rebuilt every image from scratch on every run.

`cache-cleanup.yml` deletes a PR's Actions caches when it closes, so merged branches stop competing
for the 10 GB budget with branches that still exist.

## Branch protection

`master` currently has **no** branch protection and therefore no required status checks, which is
what makes skipping jobs safe: a skipped job reports no status, and nothing is waiting on one.

**If required checks are ever enabled, this breaks.** A required check that never reports leaves a
PR pending forever. The gated jobs would then have to switch to the always-run/no-op pattern — the
job always starts and exits early — rather than being skipped by `if`.
