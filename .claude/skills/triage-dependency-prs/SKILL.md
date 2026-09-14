---
name: triage-dependency-prs
description: Work the open Dependabot / dependency-update PR queue end-to-end. Finds every open PR labeled "dependencies", evaluates each proposed package bump (release notes, breaking changes vs our usage, security advisories, CI + e2e status, local build/test), then autonomously squash-merges the safe ones and closes the rejected ones via a "@dependabot ignore" comment. Use whenever the user wants dependency PRs handled — "triage/process/handle the dependabot PRs", "merge the dependency updates", "work the dependency queue", "are the dep bumps safe?", "clean up the dependabot backlog", "deal with the version bumps" — even if they don't say "dependabot" or "PR" explicitly. Supports a dry-run mode ("evaluate but don't merge") that produces the full assessment without merging or closing anything.
---

# Triage Dependency PRs

Evaluate every open dependency-update PR in this repo and land a decision on each:
**merge** (squash), **close** (via `@dependabot ignore`), or **escalate** (leave open with an
assessment comment for the user). The goal is an empty-or-justified queue, not a pile of
half-reviewed PRs.

## Modes

- **Live** (default): merge and close autonomously per the rubric below.
- **Dry-run**: if the user says "dry run", "don't merge", "just evaluate", or passes `dry-run`,
  do every evaluation step but make **no** mutating call — no `gh pr merge`, no `gh pr close`,
  no PR comments. End with the same report, with a "would do" column.

## Hard safety rules

These exist because this skill performs irreversible actions on a public repo:

- Only touch PRs that are labeled `dependencies` **and** authored by `app/dependabot` (or another
  bot the user names). A human-authored PR that happens to carry the label gets escalated, never
  auto-merged.
- Never merge a PR whose checks are red or pending. "Green" means **completed success** on every
  workflow that is actually *active* for PRs (see Step 1 — a disabled workflow's missing check is
  a coverage gap to compensate for, not a failure and not a pass). Use `gh pr checks <n> --watch`
  to wait rather than polling.
- Never use `--admin` or otherwise bypass branch protection.
- When genuinely uncertain after evaluating, escalate — an open PR costs nothing; a bad merge or
  a wrongly-ignored security patch costs a lot.
- Do not push commits to dependabot branches; dependabot force-pushes over them.

## Step 1 — Discover the queue, and find out what "green" actually proves

```bash
gh pr list --label dependencies --state open \
  --json number,title,author,url,labels,mergeable --limit 50
```

Before trusting any checkmark, establish what the gates really are — once, up front:

- `gh api repos/{owner}/{repo}/actions/workflows` — a workflow in state `disabled_manually`
  never runs even if its `on: pull_request` trigger says it should. A "green" PR with a disabled
  E2E workflow has **no** e2e coverage; don't wait forever for a check that will never appear,
  and don't treat its absence as success either. Surface a disabled gate to the user immediately
  (it changes every verdict), and compensate with local verification.
- Map each PR's changed files to the workflow that actually exercises them. A green PR check
  proves nothing about files no PR-time job touches — e.g. an action used only in `release.yml`
  (tag-triggered) or a Dockerfile no CI job builds. For those, green is *silent*, not *safe*:
  rely on local verification and release-notes analysis instead, and say so in the report. After
  merging release-path-only action bumps, suggest exercising them (e.g. a throwaway `-rc` tag)
  before the next real release.
- `gh api repos/{owner}/{repo}/dependabot/alerts --jq '[.[] | select(.state=="open")]'` — map
  open security alerts to the queue. A PR that fixes an open alert is a priority merge; an open
  alert **no** PR fixes (e.g. blocked behind a transitive pin) is a finding the queue will never
  resolve on its own — flag it and file an issue (see Step 6).

Filter to bot-authored PRs. Classify each by ecosystem (nuget / npm / github-actions / docker —
readable from the title and branch name) and by semver impact: the repo's `dependabot.yml` groups
minor+patch into one PR per ecosystem, so **individual PRs are majors** and grouped PRs
(`...-minor-patch` in the branch/title) are minor/patch batches.

Process order: grouped minor/patch PRs first (lowest risk, clears most of the queue), then majors.
Work **one PR at a time** — local test runs share one Postgres test database, and each merge can
invalidate the others' CI (see Step 4).

## Step 2 — Evaluate one PR

Gather, in this order (cheap → expensive):

1. **The diff and the PR body.** `gh pr view <n>` and `gh pr diff <n>`. Dependabot's body embeds
   release notes, changelogs, and commit lists per bumped package — read them for the packages
   with the biggest version jumps. For a grouped PR, skim every entry but spend attention where
   the delta is largest.
2. **Security signal.** Does the body mention a CVE/GHSA or "security" fix? A bump that fixes a
   published advisory is a strong merge-favoring signal — closing one needs an explicit reason.
   If the body is thin, check the package on the GitHub Advisory DB
   (`gh api graphql` `securityVulnerabilities` query, or the upstream repo's security tab).
3. **Breaking-change scan against our usage.** For anything flagged "BREAKING" or any major bump:
   find what the notes say was removed/changed, then grep this codebase for those APIs / config
   keys / CLI flags. A breaking change we don't use is not a blocker — say so in the assessment
   rather than reflexively rejecting majors.
4. **CI + e2e status.** `gh pr checks <n> --watch`. Note: `@llm`-tagged e2e specs skip in CI
   (no API key there) — green e2e in CI does not cover those, which is acceptable for dep bumps.
5. **Local verification** (per ecosystem, only after 1–4 look plausible — it's the expensive
   step). Check out with `gh pr checkout <n>`; return to the original branch afterwards
   (`git checkout -` and restore any stash) even on failure.

   | Ecosystem | Local verification |
   |---|---|
   | nuget | `dotnet restore && dotnet build Proxytrace.sln && dotnet test Proxytrace.sln`. Also `dotnet list package --vulnerable`. Tests hit the shared Postgres test DB — never run two test sessions concurrently. |
   | npm `/frontend` | `cd frontend && npm ci && npm run build && npm test && npm run lint`. Also `npm audit --omit=dev`. |
   | npm `/e2e` | `cd e2e && npm ci && npx playwright test --list`. This loads every spec through Playwright's own transform (217 tests in 44 files on this repo). Full Playwright run is covered by the PR's E2E workflow; don't repeat it locally unless CI was inconclusive. |
   | npm `/manual` | `cd manual && npm ci && npm run docs:build`. |
   | docker | If a Docker daemon is available, build the affected Dockerfile (e.g. `docker build -f frontend/Dockerfile frontend/`). For base-image major bumps (node N→N+2 etc.) also check engine/runtime compatibility claims in the image's release notes against what the Dockerfile actually runs. |
   | github-actions | No local run possible — rely on CI plus the action's release notes (majors here usually mean a runner/node version floor or renamed inputs; check our workflow files use the inputs the new major expects). |

Skip local verification only when the ecosystem makes it impossible (github-actions) or the PR is
a patch-level group with green CI+e2e and unremarkable notes — and say in the report that you
skipped it and why.

## Step 3 — Decide

**Merge** when all of:
- Every PR-active gate green (per the Step 1 workflow-state mapping), and any coverage gap left
  by a disabled/silent gate compensated by local verification.
- Release notes show no breaking change that touches our usage (or the PR fixes a security
  advisory and the breakage is absent/trivial).
- Local verification (when run) passed.

**Close** when the update is affirmatively bad for us — not merely "big":
- The new version breaks APIs/behavior we depend on and adapting is out of scope for a dep bump.
- The new version has its own published advisory or a serious open regression upstream.
- The bump makes no sense for this repo (e.g. base image variant we deliberately pin).

**Escalate** (leave open + post an assessment comment, and flag it in your report) when:
- Evidence conflicts (notes say safe, local test disagrees, or vice versa).
- The change is safe but demands follow-up work beyond the PR (code adaptation, config change) —
  that's a task for the user or a separate issue, not an auto-merge.
- The update is correct but **premature** — e.g. a runtime/base-image major onto a release line
  that hasn't reached LTS yet, or a bump that would put dev-time types ahead of the runtime the
  code actually executes on. Check the support/LTS status for runtime majors (Node, .NET, base
  images). Not a close (the version is fine, the timing isn't): leave open and state the revisit
  condition ("re-check after Node 26 enters LTS in Oct 2026") in the comment and report.
- The PR is human-authored or otherwise outside the safety rules.

## Step 4 — Merge mechanics

```bash
gh pr merge <n> --squash
```

Squash keeps master one-commit-per-bump. After each merge the remaining dependabot branches may
fall out of date or conflict (lockfiles especially). Before evaluating/merging the next PR:
- If `mergeable` reports conflicts, comment `@dependabot rebase` and move on to another PR while
  dependabot rebuilds it; come back once its checks re-run.
- If checks were invalidated by the rebase, `gh pr checks <n> --watch` again — never merge on
  stale green.

## Step 5 — Close mechanics

Do **not** plain-close: dependabot recreates plain-closed PRs on the next weekly run. Instead
comment the ignore command — dependabot closes the PR itself and remembers the decision:

- Single-dependency PR: comment
  `@dependabot ignore this minor version` — blocks only this release line; the next minor or
  major still arrives, so upstream fixes aren't silenced forever.
- Grouped PR where **one** package is the problem: don't reject the whole group. Comment
  `@dependabot ignore <dependency-name> this minor version` — dependabot recreates the group
  without that package on the next run; then re-evaluate the recreated PR.

Always precede the ignore command (same comment) with 1–3 sentences of rationale, citing the
specific breaking change/advisory — the comment is the audit trail for why the update was
rejected.

## Step 6 — Report

End with a table the user can act on, one row per PR:

| PR | Packages (delta) | Decision | Why |
|---|---|---|---|
| #331 | dompurify 3.4.10→3.4.11 | merged | patch, CI+e2e green, no notes of interest |
| #336 | node 24→26-alpine | escalated | needs engines bump in frontend/package.json first |

Below the table, expand anything non-obvious: escalations get a short paragraph each (what you
found, what the user must decide); closes get the rationale you posted. In dry-run mode the
Decision column reads "would merge" / "would close (ignore minor)" / "escalate".

Triage regularly surfaces problems that are nobody's PR's fault — a disabled workflow, a test
suite CI never runs, an advisory no bump can fix. In live mode, capture each one with the
`file-issue` skill instead of letting it evaporate into the report; in dry-run mode, list them
under "worth filing" so the user can decide.

No CHANGELOG entry is needed for dependency bumps — they are not user-facing product changes.
