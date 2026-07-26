# Releasing

How Proxytrace versions are defined, how a release is cut, and what the release pipeline
produces. Read this before touching versioning, the release workflow, or the deploy artifact.

## Version single source of truth: the git tag

The release version is defined **only** by the annotated git tag `vX.Y.Z` (SemVer). There is
no VERSION file and no version field to bump in `package.json`/csproj — nothing in-repo can
drift from the tag.

Propagation:

- `Directory.Build.props` defaults `Version` to `0.0.0-dev`; the release workflow overrides it
  with `-p:Version=X.Y.Z` (via the `APP_VERSION` Docker build arg in all Dockerfiles). MSBuild
  derives `AssemblyVersion`/`FileVersion` (`X.Y.Z.0`) and `AssemblyInformationalVersion` (the
  full SemVer incl. prerelease suffix) from it. `IncludeSourceRevisionInInformationalVersion`
  is disabled so no `+<sha>` suffix leaks into UI strings.
- Backend code reads the version **only** through `IAppVersion`
  (`Proxytrace.Common/Hosting/IAppVersion.cs`, registered in `Common.Module`), which reads the
  `AssemblyInformationalVersionAttribute`. Never read `Assembly.GetName().Version` (it loses
  the prerelease suffix). Consumers: dashboard telemetry, the license-server check payload,
  `GET /api/config` (`version` field), and the update check.
- Frontend builds bake `__APP_VERSION__` via the `define` block in `frontend/vite.config.ts`
  (fed by the `VITE_APP_VERSION` env var / `APP_VERSION` build arg). The SPA primarily shows
  the backend-reported version from `/api/config`.
- Dev builds everywhere self-identify as `0.0.0-dev` (which also disables the update check).

## Cutting a release

> Agent-assisted releases: invoke the `release` skill (`.claude/skills/release/SKILL.md`) —
> it walks pre-flight checks, the changelog roll, tag push, workflow monitoring, and
> post-release verification, including failure recovery.

1. On `master`, move the `CHANGELOG.md` `[Unreleased]` content under a new
   `## [X.Y.Z] - YYYY-MM-DD` heading (the workflow fails fast if this section is missing).
2. Tag and push:

   ```bash
   git tag -a vX.Y.Z -m "Proxytrace X.Y.Z"
   git push origin vX.Y.Z
   ```

3. `.github/workflows/release.yml` does the rest (see below). Prerelease tags
   (`v1.2.0-rc.1`) work too: they publish only their exact image tag (no `latest`/rolling
   tags) and create a GitHub prerelease.

## What the release workflow does

`release.yml` (trigger: tag push `v*.*.*`, serialized via a `release` concurrency group):

1. **meta** — validates the tag is SemVer and that `CHANGELOG.md` has the matching section
   (`scripts/release/extract-changelog.sh`).
2. **ci / e2e** — reuses the existing `ci.yml` and `e2e.yml` workflows as release gates
   (both expose `workflow_call`). `ci.yml` is called with **`full: true`**, which is what makes
   the gate complete: on ordinary PRs and master pushes that workflow skips jobs whose paths did
   not change, and skips some entirely on master. The input exists rather than an
   `if: github.event_name != 'push'` check because inside a reusable workflow `github.event_name`
   reports the *caller's* event — a release is a tag push, so an event-based condition would read
   `'push'` and silently skip the gate's own jobs. **If you add a job to `ci.yml`, gate it on
   `inputs.full || …` or it will not run for releases.**
   `e2e.yml` needs no such input: its `paths-ignore` filter does not apply to `workflow_call`.
3. **publish-image** — builds the **all-in-one image** (`deploy/allinone/Dockerfile`) once,
   multi-arch (linux/amd64 + linux/arm64; build stages cross-compile natively via
   `--platform=$BUILDPLATFORM` + `dotnet -a $TARGETARCH`, QEMU only runs the runtime stage's
   apt install) with `APP_VERSION` injected, and pushes that one build to **both registries**
   with an identical tag set — `X.Y.Z`, `X.Y`, `X`, and `latest` (rolling tags suppressed for
   prereleases):
   - `ghcr.io/syntaktikeu/proxytrace` — **canonical**; what `deploy/docker-compose.yml`, the
     README and the manual pin. No anonymous pull-rate limit.
   - `docker.io/proxytrace/proxytrace` — Docker Hub, for discoverability. Same digests (one
     build, two push targets). A final `peter-evans/dockerhub-description` step syncs
     `scripts/release/dockerhub-overview.md` onto the Hub repo page; it is `continue-on-error`
     because the image is already pushed by then and a description failure must never fail a
     release.

   This is the **only** released image — it contains nginx + the SPA + the manual, the API, the
   ingestion proxy, and (started only when `ConnectionStrings__Default` / `Redis__ConnectionString`
   are unset) PostgreSQL and Redis, supervised by supervisord (`deploy/allinone/entrypoint.sh`).
   The per-service Dockerfiles (`Proxytrace.Api/`, `Proxytrace.Proxy.Api/`, `frontend/`) still back
   the source-built dev/e2e/perf composes; they are not published. `ci.yml`'s **image** job builds
   the same Dockerfile and boots the container on a cold volume, so a broken release image fails
   on the PR, not on the tag.

   Layer cache for this build lives in a **registry** (`ghcr.io/syntaktikeu/proxytrace-buildcache`,
   tag `release-allinone`), not in the Actions cache. Two `type=gha,mode=max` image builds
   previously held 8.4 GB of the repo-wide 10 GB Actions budget and LRU-evicted the .NET and npm
   caches every other job restores from. `cache-to` carries `ignore-error=true` so a registry
   hiccup degrades to a slow build instead of failing a release. The tag is deliberately separate
   from `ci.yml`'s `allinone`: this build is multi-arch and mixing them would have each evict the
   other's platform layers.

   Adding a registry = add its image to the `images:` list of the `docker-meta` step; the
   tag rules apply to every image in the list.
4. **release** — pins the version into `deploy/docker-compose.yml` (replacing the
   `${PROXYTRACE_VERSION:-latest}` placeholder), zips it with `deploy/.env.example` +
   `deploy/README.md`, extracts the changelog section as release notes, and creates the
   GitHub release with the zip attached **as a draft** (`gh release create --draft`).

A drafted release is hidden from the public Releases API, so the update banner (below) does
not fire until a human publishes it. **The customer-facing go-live is the manual "Publish
release" click** in the GitHub UI (or `gh release edit vX.Y.Z --draft=false`), not the tag
push. Note the rolling image tags (`latest`/`X.Y`/`X`) still move at the publish-image step,
before the release is published — customers pin versions and the banner is API-driven, so
this is harmless, but `latest` advances ahead of the release going live.

## Source protection in shipped images

Self-hosted .NET ships decompilable IL — accepted (the protection model is licensing +
the proprietary LICENSE, like every commercial self-hosted product). The cheap hardening
that *is* applied: backend Dockerfiles publish with `-p:DebugType=none`, so released images
contain **no PDBs** — which would otherwise carry line-accurate symbols and a SourceLink map
revealing the private repo URL + commit SHA. Consequence: production stacktraces (error log)
have full frames but no file/line numbers; pair them with the reported version to locate
code. Don't reintroduce PDBs into images, and don't bother with IL obfuscators — the
reflection-based Autofac/EF discovery breaks under renaming. Frontend ships minified bundles
without sourcemaps (Vite default — don't enable `build.sourcemap` for production).

## The deploy artifact (`deploy/`)

`deploy/docker-compose.yml` is the customer-facing **production** install: the pinned GHCR
all-in-one image plus postgres 16 and redis as their own containers, healthchecks, restart
policies, named volumes (`pgdata`, `appdata`). It sets `ConnectionStrings__Default` and
`Redis__ConnectionString`, which is precisely what makes the image skip its embedded database
and cache. The in-repo copy tracks `latest`; the released zip is fully pinned. Keep it runnable
from a bare directory containing only the three artifact files — it must never reference repo
paths (which is why `frontend/nginx.conf` is baked into the image).

The simpler shape — `docker run … ghcr.io/syntaktikeu/proxytrace` with the embedded database —
needs no artifact at all; it is what the README, the website and the Hub page lead with.

## Changelog discipline

`CHANGELOG.md` follows [Keep a Changelog](https://keepachangelog.com): user-facing changes are
added to `[Unreleased]` **in the same PR** that makes them (CLAUDE.md hard rule). The release
workflow turns the version's section into the GitHub release notes verbatim.

## Update notification

The app polls the GitHub releases feed daily (`UpdateCheckService`,
`Proxytrace.Application/Updates/`) and surfaces an admin-only banner when a newer version
exists. Config section `Updates` (`Enabled`, `ManifestUrl`, `CheckIntervalHours`); admin
endpoint `GET /api/updates`. Disabled in kiosk mode and for `0.0.0-dev` builds. Operator docs:
`manual/admin/configuration.md` (§ Update notifications).

## One-time setup notes (first release)

- After the first image push, set the GHCR packages to **public** and link them to the repo
  (GitHub UI; not automatable from the workflow).
- The org must allow `GITHUB_TOKEN` package creation (org settings → Packages).
- E2E gates use `secrets: inherit` for `OPENAI_API_KEY` (LLM specs are skipped without it).
- **Docker Hub** — the image is published under the plain `proxytrace` **account**
  (`DOCKERHUB_USERNAME`, hardcoded in the `publish-image` job), authenticated with its PAT in
  the repo secret `DOCKER_HUB_PAT`. Deliberately not an organisation: Hub orgs are a paid
  feature and buy nothing here — a user account gives the same `proxytrace/proxytrace`
  namespace, and Docker can convert it to an org later without losing the name. The repository
  is **auto-created as public** on first push, so nothing must be pre-created. PAT scopes:
  *Read & Write* is enough to push; the description-sync step additionally wants *Delete*
  (Docker's API ties the description endpoint to that scope) — without it that step just logs
  a failure and the release proceeds. Changing the namespace = change `DOCKERHUB_USERNAME` +
  the secret; also update `scripts/release/dockerhub-overview.md`.
