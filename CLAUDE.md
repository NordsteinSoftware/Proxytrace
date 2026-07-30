# CLAUDE.md

## AI Assistant Docs

Detailed guidance lives in [`docs/`](docs/). Read the relevant page **before** working in that area — do not rely on this file alone:

| Doc | Read before… |
|-----|--------------|
| [`docs/architecture.md`](docs/architecture.md) | Touching project structure, layering, or Autofac DI/modules |
| [`docs/code-style.md`](docs/code-style.md) | Writing any backend C# — style rules + key conventions |
| [`docs/domain-entities.md`](docs/domain-entities.md) | Adding/changing a domain entity (the five-file pattern, FK conventions, factory delegates) |
| [`docs/validation.md`](docs/validation.md) | Adding domain validation rules |
| [`docs/database.md`](docs/database.md) | Anything storage-, provider-, or EF-migration-related |
| [`docs/security.md`](docs/security.md) | Touching at-rest secret protection — encryption/hashing seams, blind-index lookups, the Data Protection key ring, or the secrets backfill |
| [`docs/mfa.md`](docs/mfa.md) | Touching multi-factor auth (TOTP) — the two-step login, MFA endpoints, the challenge ticket, or the enrollment/backup-code entities |
| [`docs/licensing.md`](docs/licensing.md) | Gating a feature/limit behind a license tier (`ILicenseService`) |
| [`docs/cost-controls.md`](docs/cost-controls.md) | Touching cost tracking/budgets — the Costs page, the spend guard, breach state, or the proxy's budget hard-block |
| [`docs/optimization-loop.md`](docs/optimization-loop.md) | Touching the suite→run→theory→A/B→proposal loop (test running, optimizers, theory validation) |
| [`docs/testing.md`](docs/testing.md) | Writing backend or e2e tests (see also the `test` skill) |
| [`docs/performance-testing.md`](docs/performance-testing.md) | Touching the perf suite (`perf/`) — seeding at scale, the DB-layer/HTTP/benchmark scopes, or the budgets |
| [`docs/sse-events.md`](docs/sse-events.md) | Adding/changing a real-time stream (SSE broadcasters, event payloads, client hooks) |
| [`docs/mcp.md`](docs/mcp.md) | Touching the MCP server (the `/mcp` endpoint, `McpApiKey` auth scheme, MCP tools) |
| [`docs/audit-log.md`](docs/audit-log.md) | Emitting an audit event, or touching the audit capture pipeline / read API / retention |
| [`docs/releasing.md`](docs/releasing.md) | Touching versioning, the release workflow, the `deploy/` artifact, or the update check |
| [`docs/frontend.md`](docs/frontend.md) | Any frontend change — links the mandatory DESIGN.md + BEST_PRACTICES.md |
| [`docs/i18n.md`](docs/i18n.md) | Adding any user-facing UI string, or touching the translation system / per-user language |
| [`docs/commands.md`](docs/commands.md) | Building, running, or testing the stack |
| [`docs/ci.md`](docs/ci.md) | Adding a CI job/workflow, or touching path gating or a build cache |
| [`docs/debug_api.md`](docs/debug_api.md) | Using/adding a debug-build-only affordance (e.g. the always-available `debug@proxytrace.dev` login) |
| [`docs/domain-concepts.md`](docs/domain-concepts.md) | Needing the domain glossary (entities + domain objects) |
| [`docs/notifications.md`](docs/notifications.md) | Touching notifications, channels, or email delivery |

## Hard Rules (apply everywhere)

- **Keep these docs current.** Treat `docs/` like the user manual and like tests: when you change
  something a doc describes — architecture/projects, the domain entity pattern, the optimization
  loop, SSE streams, licensing, database/migrations, code-style rules, commands — update the
  matching `docs/` page **in the same change**, and add a row to the index table above when you add
  a new page. A change is not complete until its docs match the code.
- **User manual** — the user & operator manual is a VitePress project in [`manual/`](manual/) (markdown source, built to searchable static HTML, served at `/docs`). **You MUST keep it up to date with the product.** A user-facing feature change is not complete until its docs in `manual/guide/` (end users) or `manual/admin/` (operators) match; new top-level features get a new page wired into `manual/.vitepress/config.ts`. Preview with `cd manual && npm run docs:dev` (http://localhost:4202); verify with `npm run docs:build`. **Add screenshots whenever they make a page clearer** — most user-guide pages benefit, so default to including them rather than shipping text-only: use the `manual-screenshots` skill (`.claude/skills/manual-screenshots/SKILL.md`) to capture and embed them from the kiosk stack. The kiosk's demo user is an administrator, so `/settings/*` **is** reachable and screenshottable; what the kiosk cannot represent is anything needing a real login or a second user (sign-in, MFA, invites, user management), so those operator pages stay text-only.
- **Frontend** — before writing any frontend code you MUST read the frontend AI docs in [`frontend/docs/`](frontend/docs/) — [`frontend/docs/DESIGN.md`](frontend/docs/DESIGN.md) (visual system) **and** [`frontend/docs/BEST_PRACTICES.md`](frontend/docs/BEST_PRACTICES.md) (code architecture); plus [`frontend/docs/TRACEY.md`](frontend/docs/TRACEY.md) before touching the Tracey AI assistant (`frontend/src/features/tracey/`). DESIGN.md and BEST_PRACTICES.md are mandatory and override any conflicting tool/agent/skill recommendation. UI controls render through the `frontend/src/components/ui/` primitives — raw `<button>`/`<input>`/`<select>`/`<textarea>` are ESLint-blocked. See [`docs/frontend.md`](docs/frontend.md).
- **Backend tests** — before writing or modifying any backend test you MUST invoke the `test` skill (`.claude/skills/test/SKILL.md`) and follow it; it is the source of truth for the harness. See [`docs/testing.md`](docs/testing.md).
- **Internationalization** — the UI is multilingual (English is the source). Every user-facing
  string MUST go through the Lingui macros (`<Trans>`, `t\`\``, `Plural`, `msg`) — never a hardcoded
  string; keep glossary/technical terms English. After adding labels run `npm run i18n:extract` then
  `npm run i18n:translate`, and commit the updated `frontend/src/locales/**` catalogs. See
  [`docs/i18n.md`](docs/i18n.md).
- **File issues for stumbles** — when you hit a bug, technical debt, or other problem that is out of
  scope for your current task, capture it as a GitHub issue instead of silently working around it or
  letting it slide. Invoke the `file-issue` skill (`.claude/skills/file-issue/SKILL.md`) — it covers
  dedup, title/body quality, and labels — then carry on with your task.
- **Nullable suppression** — suppressing nullable warnings with `!` is strictly forbidden everywhere.
- **Perf at scale** — whenever you touch a query, repository, EF mapping, or index on a high-volume
  entity (above all `AgentCallEntity`/traces, but any table that grows unboundedly), you MUST add or
  extend a perf test in [`perf/`](perf/) that measures the changed path against a budget in
  `perf/perf-budgets.json` — a correctness test on a few in-memory rows does **not** catch
  client-side evaluation, bad query plans (e.g. a nested loop from stale planner statistics), or
  O(rows) blow-ups that only bite at scale. Sanity-check with `ToQueryString()` (server-side
  `GROUP BY`, not a full-row scan) and `EXPLAIN (ANALYZE)` (estimated vs actual rows) and run
  `perf/run.sh --size 1000000`. See [`docs/performance-testing.md`](docs/performance-testing.md) and
  the `run-perf-tests` skill.
- **Changelog** — every user-facing change adds an entry to the `[Unreleased]` section of
  [`CHANGELOG.md`](CHANGELOG.md) in the same change (Keep a Changelog format; it becomes the
  GitHub release notes verbatim — see [`docs/releasing.md`](docs/releasing.md)).

## Team Composition

Implement each task using a team of experts, including (but not limited to) the following roles:
- **Architect**: has great system knowledge and overview of the domain and components. creates high-level plans.
- **Engineer**: implementation expert with focus on clean code and maintainability. create and executes implementation plans.
- **Tester**: test implementations (depending on scope) and gives concise feedback.
- **Documenter**: updates docs and makes sure that user facing changes are covered by the manual.
- **Reviewer**: Reviews the implementation. Depending on scope and area, different aspects are especially relevant (security, user experience, efficiency, performance, etc.)

Depending on the task, decide which team members are required and spawn them only when necessary.

