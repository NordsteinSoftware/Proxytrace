---
name: release
description: >-
  Cut and publish a Proxytrace release end-to-end: roll the CHANGELOG, push the
  release tag, monitor the GitHub release workflow, and verify the published
  images and artifact. Use this whenever the user wants to release, publish,
  ship, or tag a new version (e.g. "release 1.2.0", "cut a patch release",
  "ship what's on master", "publish an rc"), wants to know whether master is
  releasable, or needs to recover from a failed/broken release. Also use it for
  prereleases (rc tags) and for verifying that a just-published release is
  healthy.
---

# Cutting a Proxytrace Release

The release pipeline is fully automated behind one trigger: **pushing an annotated tag
`vX.Y.Z`**. The tag is the version's single source of truth — there is no version file or
package.json to bump (see `docs/releasing.md` for the architecture). Your job is to prepare
the changelog, push the tag, watch the pipeline, and verify the result.

Releases are consumed by customers: images land on GHCR, a GitHub release with install
artifact is created, and running installations show an update notice. Get explicit user
confirmation before pushing the tag; everything before that point is local and reversible.

**The GitHub release is created as a draft — it does NOT go live on its own.** The workflow
pushes images to GHCR and creates the release *unpublished*; a human publishes it from the
GitHub UI when ready. While drafted it is hidden from the public Releases API, so the update
banner on running installations stays silent until you publish. Pushing the tag is therefore
*not* the customer-facing point of no return — the manual **Publish release** click is.

(Caveat: rolling image tags `latest`/`X.Y`/`X` still move on GHCR when images publish, before
you click publish. Customers pin versions and the banner is API-driven, so this is harmless,
but be aware `latest` advances at image-publish time, not at release-publish time.)

## 1. Pre-flight

All of these must hold before touching anything:

- **On `master`, clean tree, up to date** — `git status`, `git pull origin master`.
  Releases are cut from master only; the tag must point at a master commit.
- **CI green on the tip commit** — `gh run list --branch master --limit 5`. The release
  workflow re-runs ci + e2e as gates, but finding out via a failed release wastes a cycle.
- **`CHANGELOG.md` has content under `[Unreleased]`** — that section becomes the release
  notes verbatim. If it's empty, the work wasn't logged; reconstruct it from
  `git log <last-tag>..HEAD` before proceeding (only user-facing changes, Keep a Changelog
  categories: Added/Changed/Fixed/Removed/Security).

## 2. Choose the version

SemVer against the previous tag (`git describe --tags --abbrev=0`):

- **major** — breaking config/API/upgrade-path change for operators or API consumers.
- **minor** — new user-facing features (the common case).
- **patch** — fixes only.
- **prerelease** (`X.Y.Z-rc.N`) — when the user wants a dry run or staged rollout. Publishes
  only the exact image tag (no `latest`/`X.Y` rolling tags) and a GitHub *prerelease* —
  customer installs on pinned versions and the update banner are unaffected.

Propose the bump with one sentence of reasoning from the changelog content; let the user
override.

## 3. Roll the changelog

In `CHANGELOG.md`, insert a new heading under `## [Unreleased]` so the unreleased section
stays (now empty) and the content moves beneath `## [X.Y.Z] - YYYY-MM-DD` (today's date).
Then prove the workflow's guard will pass:

```bash
./scripts/release/extract-changelog.sh X.Y.Z
```

It must print the section and exit 0 — this exact script gates the release workflow.

Commit (this is the release commit) and push:

```bash
git add CHANGELOG.md
git commit -m "Release X.Y.Z"
git push origin master
```

## 4. Tag and push

**Confirm with the user first**: version, one-line summary of the notes, and that pushing
publishes images to GHCR and creates a **draft** release (no customer-facing update yet —
that waits for the manual publish in step 7). Then:

```bash
git tag -a vX.Y.Z -m "Proxytrace X.Y.Z"
git push origin vX.Y.Z
```

## 5. Monitor the pipeline

```bash
gh run list --workflow=release.yml --limit 1   # grab the run id
gh run watch <run-id> --exit-status
```

Job order in `.github/workflows/release.yml`: `meta` (tag/changelog guard) → `ci` + `e2e`
(reused gate workflows) → `publish-image` (the single all-in-one image → GHCR + Docker Hub) →
`release` (zips the pinned compose artifact, creates the GitHub release **as a draft**).

The e2e gate takes the longest (Docker stack + Playwright, ~15 min). LLM specs are skipped
when the `OPENAI_API_KEY` secret is absent — that is normal, not a failure.

### If a job fails

`gh run view <run-id> --log-failed` and triage by job:

- **meta** — bad tag format or missing changelog section. Fix on master, then delete and
  re-push the tag (safe: nothing was published yet):
  `git push origin :refs/tags/vX.Y.Z && git tag -d vX.Y.Z` → fix → re-tag.
- **ci / e2e** — a real regression or a flaky e2e spec (consult the `run-e2e-tests` skill
  to triage). Fix forward on master, delete the tag, re-tag the fixed commit.
- **publish-image** — usually registry permissions: GHCR (org must allow `GITHUB_TOKEN`
  package creation; packages must exist/be linked) or Docker Hub (`DOCKER_HUB_PAT` expired /
  lacks *Read & Write*, or is not an owner of the `proxytrace` org) — see `docs/releasing.md`
  one-time setup. Re-run the failed job after fixing: `gh run rerun <run-id> --failed`
  (re-pushing the same tags is idempotent). A red **Sync Docker Hub repo description** step is
  `continue-on-error` cosmetic noise — the image shipped; don't re-run the release for it.
- **release** — images are already pushed at this point; prefer `gh run rerun --failed`
  over re-tagging.

## 6. Verify the draft release

A release isn't done because the workflow is green — check what customers *will* get before
you publish. The release is a draft, so `gh release view` shows `Draft: true` and the public
API won't list it yet:

```bash
gh release view vX.Y.Z                                        # Draft: true, notes + zip asset attached
docker manifest inspect ghcr.io/syntaktikeu/proxytrace:X.Y.Z   # image exists
docker manifest inspect ghcr.io/syntaktikeu/proxytrace:latest  # rolling tag moved (not for rc)
docker manifest inspect proxytrace/proxytrace:X.Y.Z           # Docker Hub got it too
```

Strongest check — run the customer artifact exactly as a customer would (use a throwaway
compose project name so nothing touches a local dev stack):

```bash
mkdir -p /tmp/proxytrace-release-verify && cd /tmp/proxytrace-release-verify
gh release download vX.Y.Z --pattern '*.zip' && unzip -o proxytrace.zip && cd proxytrace-*/
docker compose -p proxytrace-release-verify up -d --wait   # no .env needed — all settings default
curl -s http://localhost:5101/api/config    # must report "version":"X.Y.Z"
curl -s -o /dev/null -w '%{http_code}' http://localhost:5101/docs/   # 200
docker compose -p proxytrace-release-verify down -v && cd / && rm -rf /tmp/proxytrace-release-verify
```

Report to the user: version, draft release URL, image digests pulled OK, smoke result.

## 7. Publish the release (manual, customer-facing go-live)

This is the real point of no return. **The user does this**, or explicitly tells you to.
Publishing flips the draft live: it appears in the public Releases API and running
installations start showing the update banner.

- **UI (default):** open the draft release page, review notes + attached zip, click
  **Publish release**.
- **CLI equivalent**, if the user asks you to do it:

  ```bash
  gh release edit vX.Y.Z --draft=false   # add --latest for the newest stable
  ```

For a prerelease (rc), publishing keeps it flagged as a prerelease — the banner and
`latest`-pinned installs are unaffected.

## 8. If a release is broken

**Still a draft (not yet published in step 7)?** Cheap to recover — nothing is customer-facing
yet. Just `gh release delete vX.Y.Z` the draft, then delete + re-push the tag on the fixed
commit (`git push origin :refs/tags/vX.Y.Z && git tag -d vX.Y.Z` → fix → re-tag). The only
caveat is the rolling `latest`/`X.Y` image tags already moved; re-running the release with the
fixed commit moves them again.

**Already published?** Releases are immutable in customers' eyes — installations may already have pulled the
images and the update banner already points at the release. **Fix forward with a patch
release** (back to step 1) rather than deleting. Deleting (`gh release delete` + tag
removal + GHCR cleanup) is a last resort reserved for a release that is dangerous to run,
within minutes of publishing, with explicit user sign-off.

## First release on a fresh setup

One-time prerequisites live in `docs/releasing.md` ("One-time setup notes"): org-level
`GITHUB_TOKEN` package-creation permission, and flipping the three GHCR packages to
**public** + linking them to the repo *after* the first push (images are private until
then — customer pulls fail). Verify public visibility with an unauthenticated
`docker manifest inspect`.
