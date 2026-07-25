# Changelog

All notable, user-facing changes to Proxytrace are documented in this file.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and versions
follow [Semantic Versioning](https://semver.org). Ongoing work is collected under
`[Unreleased]`; cutting a release moves that section under the new version heading
(see `docs/releasing.md`).

## [Unreleased]

### Added

- **Debugging sessions: group live traces across agents and conversations.** Tag your calls with the
  `x-proxytrace-session-id` header and Proxytrace collects every trace sharing that key — spanning
  multiple agents and conversations — into one **session**, the bigger picture around a single app run
  or user session. Sessions are auto-created on the first trace with an unseen key, work on every
  license tier, and need no setup. A dedicated **session page** (`/sessions/:sessionId`) shows one session's
  traces as a live, chronological timeline: header counters (trace and token totals,
  first-seen/last-activity) and the trace list update in real time as new calls arrive, with a **Live**
  indicator while the session saw activity in the last five minutes. On the **Traces** page, a new
  **Session** filter narrows the table (and its timeline) to a single session — pick from the project's
  recent sessions — and every trace row and the trace detail panel carry a **Session** link to jump
  straight to the whole session. For the API, `GET /api/sessions?projectId=…` lists a project's recent
  sessions (most recently active first, with per-session trace and token counters) and
  `GET /api/sessions/{id}` returns one; sessions are scoped to the projects you can access, exactly
  like traces.
- **Notification details view.** Clicking a notification in the bell inbox now opens a detail drawer
  instead of navigating away: the full, untruncated message, its kind, status, project and
  timestamps, and a live summary of whatever the notification is about (test run, agent, proposal
  or trace) with a link to it. If that item has since been deleted the drawer says so rather than
  linking nowhere — a notification is often the only record an anomaly ever had. The drawer steps
  through the inbox with prev/next, is deep-linkable on any page via `?notification=<id>`, and
  notification emails now link straight to it (`/notifications/<id>`) instead of to the target's
  list page.

- **German language selection for the sample client.** The sample chat client in the kiosk showcase now has an EN/DE toggle in the header. UI chrome, agent display names, and example shortcuts (including a stage-ready German version of the trick message) switch to German instantly; the agent system prompt, tool definitions, and `X-Proxytrace-Agent` attribution header remain byte-identical English so ingestion attribution and the optimizer loop are unaffected.

- **One-command live showcase stack.** The kiosk now serves an OpenAI-compatible proxy in-process
  when a live LLM endpoint is configured, so a sample client pointed at the demo can generate calls
  that appear as traces in real time. Copy `kiosk.env.example` to `.env`, fill in your credentials,
  and run `docker compose -f docker-compose.kiosk.yml up --build` to bring up the full three-service
  stack — Proxytrace API (`:5200`), web UI (`:5201`), and the bundled sample chat client (`:5202`).
  Without credentials the stack still boots in read-only demo mode; the demo API key defaults to
  `pk-kiosk-demo`. The full presenter runbook is in `sample-client/README.md`.

- **The demo "Customer Support" agent can now showcase social-engineering resistance.** The kiosk seed
  arms the support agent with an `issue_refund` tool and a ten-case refund test suite — five of
  which are social-engineering attempts to extract unauthorized refunds — pre-seeded with a 100%
  pass-rate history, so a presenter can trigger the trick in the sample chat client and watch the
  pass-rate drop on screen.

- **Upstream provider key rotations are audited distinctly.** Replacing a provider's upstream API
  key now records a dedicated *Provider Key Rotated* audit event instead of the generic provider
  config update, so credential rotations stand out in incident review and compliance reporting.
  The key value itself is never recorded.

### Changed

- **`x-proxytrace-session-id` now names a debugging session, not a conversation.** The header that
  used to set the conversation/thread key now identifies the broader *session* (see Added), and
  thread-level grouping moves to the new `x-proxytrace-conversation-id` header. Existing clients need
  no change: when no `x-proxytrace-conversation-id` is sent, the session key still drives conversation
  grouping, so calls keep grouping into threads byte-for-byte as before — and now gain a session view
  on top. Send `x-proxytrace-conversation-id` only when you want one session to hold several distinct
  conversations. Neither header is forwarded upstream.

### Fixed

- **A notification about a trace no longer blanks the whole app.** Notifications raised for a
  captured call (a blocked call, or a custom anomaly detector's review) carried a target kind the
  web UI did not know, and rendering one threw while drawing the top bar — which sits outside every
  page's error boundary, so the entire app went blank until a reload. Unknown target kinds now
  degrade to a notification with no link.
- **Opening a notification marks it read**, including when it is opened from a deep link or an
  emailed link; previously the unread badge stayed until you clicked the tick explicitly.
- **The notification panel no longer closes over the page you navigated to**, and marking one
  notification read or dismissing it no longer freezes the buttons on every other row while the
  request is in flight.

- **Provider key rotation and revocation now take effect on the very next proxied request.** The
  ingestion proxy previously cached resolved credentials — including the decrypted upstream provider
  key — for up to 30 seconds, so a rotated key could keep being forwarded (and the replaced key kept
  authenticating inbound) until the cache expired, in every proxy replica independently. The proxy
  now resolves credentials from the database on every request and fails closed when the database is
  unreachable instead of serving stale credentials. The `ApiKeyCache` setting is removed.
- **Keyboard focus is now visible on the remaining bespoke controls.** The playground settings
  rail, agent picker, endpoint chip, tool result/error tabs, suite-wizard preset chips, search
  indexing kind toggles, the evaluator recent-evaluations filter chip, and the move-version target
  list now show the standard focus indicator when reached with the keyboard, completing the
  focus-ring sweep started with the shared button and row primitives.
- **All text sizes now come from the design type scale.** Seven components (evaluator cost and
  stat panels, the setup wizard headings, and the evaluator playground score chip) used one-off
  pixel sizes; they now use scale tokens, including a new intermediate 22px display size, and the
  score chip's "/5" suffix no longer renders below the 10px legibility floor.

- **The demo seed now backdates evaluation history along with its runs.** Evaluation statistics
  previously kept the seed time even when their runs were spread across the past 30 days, so the
  evaluator workspace's pass-rate trend showed "Not enough data" in the demo/kiosk stack. Updated
  test results now rewrite their evaluation-statistics timestamps, and the trend chart renders
  real history.

## [1.8.0] - 2026-07-22

### Added

- **Upstream provider keys can be rotated from Settings.** Admins can edit a provider's upstream
  API key inline; Proxytrace verifies the replacement against the provider before saving it and
  keeps the existing credential when verification fails.

### Changed

- **The interface has been redesigned.** Proxytrace now wears *Signal Desk* — a flat, ruled
  instrument surface in blue-petrol ink with a single signal-cyan accent, in place of the previous
  rounded, gold-accented, softly-shadowed look. Structure comes from 1px rules rather than from
  shadows and floating panels: corners are square, fills are one flat colour, and the gradients,
  glows, and background atmosphere are gone. Mono type now carries the structural labels — table
  headers, KPI eyebrows, nav page codes, the breadcrumb — so the data reads as instrument
  readout rather than prose. Nothing moved: every screen keeps its layout, and no workflow
  changed. Alongside the reskin, label and on-fill text contrast was corrected across the app so
  small text meets WCAG AA, and a few visual defects were fixed — row and message-header hover
  states now fill their full row, and chart end-point markers no longer overhang the card edge.
  The bundled manual at `/docs` was rethemed to match.

### Fixed

- **Provider connection tests no longer report invalid credentials as successful.** Upstream
  authentication and network failures are now surfaced in the setup wizard instead of being
  mistaken for a successful connection with an empty model list. A successful provider response
  with no models remains valid and is shown as a warning.
- **The Tracey message box now shows a focus ring.** Clicking or tabbing into the "Ask Tracey…" box
  previously changed nothing but a faint 1px tint on its border — easy to miss against the dark panel,
  and the one input in the app that opted out of the standard focus ring. The composer frame now
  carries the same accent ring every other control uses, and it lights only while the message field
  itself holds focus, so the New conversation and Send/Stop buttons still show focus on themselves. (#388)
- **The global search box can be cleared with the keyboard.** The **✕** button beside the search
  field sat in the tab order but responded only to a mouse click — pressing Enter or Space on it did
  nothing, so keyboard users had to select-all and delete instead. It now activates on Enter and
  Space like any other button, shows a focus ring, and uses the standard close icon. (#396)
- **Firefox shows which parameter slider has keyboard focus.** The Agent Playground's temperature and
  top-p sliders suppressed the browser's own focus outline but only drew a replacement ring on
  Chrome and Safari, so on Firefox tabbing to a slider changed nothing on screen and the arrow keys
  then adjusted a value with no indication of which one. Firefox now gets the same ring — plus the
  hover and drag states it was also missing. (#395)
- **`./dev.sh` now actually serves the UI.** The dev frontend proxied `/api` and `/mcp` to port 5000
  while `dev.sh` started the backend on 5001, so every request from http://localhost:4201 failed with
  `http proxy error … ECONNREFUSED` and the app never loaded. The dev backend port is now 5001
  consistently — `launchSettings.json`, the `Self:BaseUrl` default, `vite.config.ts`, and the docs —
  so both `./dev.sh` and `cd Proxytrace.Api && dotnet run` work with `npm run dev`.
- **The sample client pointed at a port that serves nothing.** `sample-client/.env.example` set
  `PROXYTRACE_BASE_URL` to `localhost:5000/openai/v1`, but `/openai/v1` is served by the standalone
  ingestion proxy, never by the API — it is `localhost:5002` under `SPLIT=1 ./dev.sh` and
  `localhost:5102` under Docker Compose. The example now points at 5002.

## [1.7.0] - 2026-07-20

### Added

- **Scoped API keys for the REST API.** A Proxytrace API key can now drive `/api/*` directly, so an
  external service no longer needs a long-lived user login (with MFA disabled and a token-refresh loop)
  to call the API. Mint a key with the new **REST API read** and/or **REST API write** capabilities:
  read keys may issue `GET` requests, write keys may also create and change data. The key acts as its
  owner and, like an MCP key, can never reach admin-only endpoints. Capabilities stay least-privilege
  and are not interchangeable across surfaces — a REST key cannot drive MCP or proxy LLM traffic, and
  existing keys are unaffected. (#365)
- **Record corrections over MCP.** The `add_trace_to_suite` tool now takes an optional `expectedOutput`.
  Provide it to log a *correction* — "the agent saw this input, and the right answer was X" — turning a
  captured trace into a regression test, instead of only promoting the trace as-is. An external agent
  can now drive the entire capture → correct → propose → validate loop with a single scoped MCP key. (#366)

### Changed

- **The proxy now forwards client headers transparently.** LLM requests through the ingestion proxy
  previously only passed a small fixed set of headers to the upstream provider; everything else was
  dropped. Now every header travels upstream unchanged — `OpenAI-Beta`, `openai-organization`,
  idempotency keys, custom tracing headers, and anything else your provider expects — so an existing
  client can swap its base URL to Proxytrace with no behavior change. Only Proxytrace's own
  `x-proxytrace-*` control headers, credentials (replaced with the provider's real key), and
  hop-by-hop/connection headers are stripped. Upstream response headers are relayed the same way, and
  Azure OpenAI upstreams now also receive the provider key in the `api-key` header that Azure's
  classic data-plane auth expects.
- **Test cases now remember which trace they came from.** Promoting or correcting a trace into a test
  suite records a link back to the source trace, so "which trace produced this case?" is answerable from
  Proxytrace's own data. (Previously the link was silently dropped, despite the API documenting
  otherwise.) Synthetic cases built from raw input and expected output have no source and are unaffected. (#367)

## [1.6.0] - 2026-07-13

### Added

- **Install with a single `docker run`.** Proxytrace now ships as one image containing the
  whole product — web UI, API, ingestion proxy, PostgreSQL and Redis — so a complete install
  is one command with nothing to download and nothing to configure:
  `docker run -d -p 5101:80 -p 5102:8081 -v proxytrace:/data ghcr.io/proxytrace/proxytrace`.
  All state lives in the `/data` volume; schema migrations still apply on start.
- **Images are published to Docker Hub as well.** Each release pushes the image to
  `proxytrace/proxytrace` on Docker Hub and `ghcr.io/proxytrace/proxytrace` on GHCR — one
  build, identical tags and digests, `linux/amd64` + `linux/arm64`.

### Changed

- **The release now ships one image instead of three.** The separate `proxytrace-api`,
  `proxytrace-proxy` and `proxytrace-frontend` images are no longer published; the all-in-one
  image replaces them. The Docker Compose deployment attached to every release still runs
  PostgreSQL and Redis as their own containers — it points the app at them with
  `ConnectionStrings__Default` / `Redis__ConnectionString`, which is what keeps the image's
  embedded database and cache switched off. **Upgrading an existing Compose install:** take a
  database backup, then swap in the new release's `docker-compose.yml` — it replaces the three
  app services with one and keeps your `pgdata`, `appdata` and `searchindex` volumes exactly as
  they are, so the database, the secret-encryption key ring and the search index all carry over.

- **Errored A/B validations no longer count as disproven theories.** When a theory's A/B
  validation cannot run at all (unreachable or unauthorized provider, upstream timeout,
  incomplete run), the theory now settles in a new **Failed** state instead of *Invalidated*.
  Failed theories are excluded from the review desk's **win rate** (an outage is not a lost
  experiment), surface in a new **Needs attention** queue group with a red *could not test*
  node on the loop strip instead of disappearing into History, and can be **retried** from
  their dossier once the underlying problem is fixed (or dismissed). Resubmitting the same
  idea is no longer blocked by a failed prior attempt, and each failure is recorded in the
  audit log (*Theory Validation Failed*).

## [1.5.0] - 2026-07-12

### Added

- **Ask Tracey everywhere.** Context-aware ⚡ *Ask Tracey* buttons now appear throughout the
  app — on a trace's detail drawer (anomaly-aware: flagged traces ask *why did this anomaly
  happen and how do we prevent it*, with the detector hits passed along), on an agent's header
  (pass-rate-aware: agents with weak suites ask for an improvement to A/B-test), on a test
  run's header (explain the failures and suggest fixes), on a theory's drawer (walk through the
  proposal and recommend accept/reject), and on the Anomalies and Dashboard pages (project-wide
  investigation / health review). Clicking one jumps to Tracey AI and starts a fresh
  conversation pre-loaded with the entity's context; the previous conversation is kept in the
  history rail.

- **Real-time blocking anomaly detectors.** A custom anomaly detector can now also **block**: turn
  on *Block matching requests at the proxy* and the proxy checks each incoming request's body
  against the detector's phrase/regex triggers **before forwarding** — on a match the request is
  rejected with an OpenAI-compatible `403` (`code: proxytrace_blocked`) and **never reaches the
  upstream provider**. The canonical use case is stopping secrets (e.g. a password pattern) from
  being sent to the LLM provider. Blocked calls still show up as traces, flagged **Blocked at
  proxy**, with the detector and matched trigger attributed in the trace's anomaly banner, a live
  entry on the Anomaly dashboard, and a notification. Blocking is trigger-match only (the LLM
  review never runs in the request path), applies rule changes within ~30 seconds, fails open if
  the rules cannot be loaded, and — for detectors scoped to specific agents — enforces only when
  the client names its agent via the `x-proxytrace-agent` header. Part of the Enterprise custom
  anomaly detectors feature.

- **Sortable trace table + composable filters.** The Traces table can now be sorted by any
  metric column — Latency, Tokens, Tools, Cached, or Time — with a click on the column header
  (click again to flip direction); sorting is server-side, so "slowest call" means across all
  matching traces, not just the visible page. The toolbar's agent dropdown and "Outliers only"
  pill are replaced by a composable **+ Filter** button that sits on the toolbar line beside
  search and the time range: stack removable filter chips for agent, anomaly type (any, or a
  specific reason like high latency or a custom-detector hit), tool name (picked from the tools
  your traces actually called — and once you've picked an agent, only the tools *that* agent
  used), model, HTTP status class (2xx/4xx/5xx), token/latency ranges,
  and **System traces** (include traces from system agents — chosen from **+ Filter** instead
  of a separate toggle). Filters combine, the timeline follows them, and
  your chips are remembered per project. Traces captured before this release are indexed
  automatically on upgrade so the tool-name filter covers them too.

- **A new Anomaly dashboard.** A dedicated **Anomalies** page (in the sidebar, after Traces) brings
  every agent's anomalies together in one place: a table of recently flagged calls (agent, message
  preview, why it was flagged, when) beside a statistics column — a live, stacked per-agent
  timeline with an agent legend (five-minute, hourly, or daily buckets), summary tiles (flagged
  calls, statistical vs. detector flags, agents affected), and a **Most flagged agents** ranking
  with proportional share bars. Filter by agent and click any row to open the trace's full detail
  panel right on the dashboard (the same panel as the Traces page, with prev/next stepping through
  the flagged calls). The whole page updates in real time as calls are captured and flagged.

- **Custom LLM-based anomaly detectors (Enterprise).** Define your own anomaly detectors per
  project: describe what "anomalous" means in plain-language review instructions, pick a review
  model, and set 1–20 trigger words or regular expressions that gate which calls get reviewed. When
  a trigger matches a new turn, the detector's model reviews it and — on an anomalous verdict — flags
  the call with a **Custom detector** chip, adds it to the Anomaly dashboard, and raises a
  notification that deep-links to the trace. Scope a detector to all agents or selected ones, and
  enable or disable it without losing its configuration. Because reviews cost one model call per
  trigger-matched turn, triggers keep the LLM focused only on the calls that could be a problem.
  Detectors are managed on the dashboard's **Detectors** tab — a two-column view (like Evaluators)
  with the searchable detector list on the left and the selected detector's instructions, triggers,
  and agent scope on the right, including a quick enable/disable toggle in the detail header.

- **Anomalous traces announce themselves in the trace detail panel.** Opening a flagged call's
  details — from the Traces list or the Anomaly dashboard — now shows an **Anomalous trace**
  warning banner right below the header: the statistical reasons as chips (high latency, high
  token count, …) and, for custom-detector hits, the detector's name, the trigger that matched,
  and the reviewer's reasoning.

- **Non-LLM upstream endpoints now pass through the proxy.** Any path under your project base URL
  that isn't part of the OpenAI API (for example `/{project}/health`) is transparently forwarded to
  your provider's upstream host instead of returning `404`, so clients can reach a provider's health
  check or other endpoints through the same base URL they use for completions. These pass-through
  calls are not captured as traces and still require a valid project API key. Redirect, throttling,
  and caching response headers (`Location`, `Retry-After`, `Allow`, `Cache-Control`) are relayed,
  and upstream redirects are passed back to the client verbatim instead of being followed
  server-side.

### Changed

- **Proxytrace is now source-available.** The full source code is public at
  [github.com/Proxytrace/Proxytrace](https://github.com/Proxytrace/Proxytrace) under the
  Elastic License 2.0: read, build, run, and modify it freely. Providing Proxytrace as a
  managed service to third parties and removing or circumventing the license-key
  functionality are not permitted. Paid tiers keep working exactly as before — unlocked
  with a license key.

- **Quick-start now teaches deterministic agent naming.** The ingestion quick-start — the
  Traces empty state and the setup wizard's final step — and the proxy setup guide now show
  the optional `x-proxytrace-agent` header, which attributes calls to the named agent
  directly instead of relying on prompt-similarity matching.

- **The dashboard is now a live mission control.** A new full-width pulse band charts
  per-minute call activity over the last hour and beats in real time as traces arrive. The
  live trace feed moved to center stage with richer rows (agent identity, live age, arrival
  flash), the token headline grew into an animated gradient display, and queue depth and p95
  latency joined the stat tiles. Charts draw in on load; all motion honors reduced-motion
  preferences. The old telemetry strip's proxy-version label was retired along with the strip
  itself.

- **The dashboard's lower half got the mission-control treatment.** The old donut, one-bar
  latency histogram, and agent-card grid are replaced by two denser, more honest sections:
  an **Agent fleet** roster — one row per agent with its own activity sparkline (the top
  pulse band, decomposed per agent), endpoint, token total and fleet share, trace count, and
  last-active time — and a **Latency spectrum** showing each endpoint's min→max latency span
  on a shared log scale with p50/p95/p99 markers, alongside the project-wide percentile
  strip. The fleet header's proposals chip now shows the **real** count of pending
  optimization proposals (it was previously a static placeholder) and links to the
  Proposals view.

- **The Proposals page is now a review desk.** The four-column theory kanban (whose first two
  columns sat empty most of the time) is replaced by a master/detail decision inbox. A queue
  rail groups theories by urgency — *Needs decision* first, then *Awaiting adoption*, live
  *In flight* items, and a collapsed *History* — and a loop strip across the top shows the
  optimization pipeline at a glance (testing → need decision → awaiting adoption → decided,
  closing with the total proven gain; each node jumps to its group). Selecting an item opens a
  full-width dossier in place of the old drawer: the measured gain and significance lead, the
  proposed change diff finally has room, evidence (A/B results, source runs, rationale) sits
  alongside, and Promote / Dismiss live in a pinned decision bar. Promoted proposals surface
  their handoff package first; validated-but-promoted, adopted, and dismissed items no longer
  masquerade as reviewable.

- **The sidebar now follows your workflow.** Navigation is regrouped into **Monitor**
  (Dashboard, Traces, Anomalies), **Build** (Agents, Agent Playground), and **Improve** — the
  whole optimization loop in order (Test Suites, Evaluators, Evaluator Playground, Test Runs,
  Proposals), so a proposal and the run that produced it finally live side by side. **Tracey AI**
  moved to a dedicated slot at the top, and the **Audit Log**, an admin **Settings** shortcut,
  and the Documentation link now sit together in a utility area above the project selector.

- **Tracey AI chat is easier on the eyes — and looks the part.** Chat messages, the composer, and
  in-chat headings now render at a comfortable reading size instead of the app's compact data
  scale, and the whole page picked up an identity: an animated gold-and-teal halo around Tracey's
  avatar (it spins while she's thinking), a soft aurora across the top of the chat panel, a
  gradient-lit wordmark and welcome screen, a shimmering *Thinking…* indicator, and larger
  starter/follow-up chips. All motion respects your system's reduced-motion preference.

- **Clearer trace detail header.** The trace drawer's header now leads with the identity that
  matters: the agent (entity-colored, click to open its page), the model, and the HTTP status
  on the first line; the full trace ID (with copy) and the exact capture time — date and time
  to the second — on the line below. *Promote to test case* is renamed to the shorter
  **Add test** (the dialog it opens follows suit), and the redundant *Create suite →* link is
  gone — the Add test tooltip now points to the Test Suites page when the agent has no suite
  yet.

- **Compact page layouts everywhere.** The remaining pages that still opened with a large
  title and subtitle — Proposals, Anomalies, Error Log, Audit Log, Users, and Account
  security — now start directly with their content (the top bar's breadcrumb already names
  the page). Filters and actions that lived in those headers moved into the pages' toolbars.

### Fixed

- **Provider endpoint URLs no longer need the `https://` prefix.** Entering an upstream
  endpoint without a scheme (e.g. `api.openai.com/v1`) — in the setup wizard or in
  Settings → Providers — previously failed with an unexpected-error toast. `https://` is now
  assumed when no scheme is given, and a genuinely malformed URL returns a clear validation
  message instead of a server error.
- **Live updates stop needing a page reload.** Real-time streams (new traces, notifications,
  anomalies, run progress) no longer go silent after a dropped connection. The stream credential is
  single-use, so the browser's automatic reconnect was replaying a consumed ticket and getting
  rejected — permanently killing the stream after the first blip (a server restart, a proxy timeout,
  a laptop waking from sleep) until you reloaded the page. The client now reconnects with a fresh
  ticket and an exponential backoff, so the Traces list and other live views keep updating on their
  own.
- The redesigned dashboard's labels (activity band, live feed, queue and latency tiles) are now
  translated into German, Spanish, French, and Italian instead of falling back to English.
- **The proxy no longer errors on a bare base URL.** Hitting the traced proxy surface with an empty
  path — `GET /openai/v1` or `GET /{project}/openai/v1` with no trailing segment — used to throw a
  `NullReferenceException` and return an opaque `500`. The empty path is now handled cleanly instead
  of faulting.
- **Dashboard metric tiles and the pass-rate gauge now show real numbers, not placeholders.** The
  trend chips on the Traces, Avg Latency, Throughput, and Pass Rate tiles were hardcoded (`+24%`,
  `-8%`, `+18%`, `+7pt`) and never moved with your traffic; they now compare the first and last half
  of each tile's own trend series and show the true change — or nothing at all when there isn't
  enough data. The pass-rate gauge's footer showed a fabricated *best* and a made-up *90% target*;
  it now reports the real change since the previous run and the best pass rate across your recent
  runs, and the fake target is gone.
- **Agent colors are distinct again in charts, legends, and badges.** The per-agent color palette
  had eight slots but only about three visibly different hues (three near-identical warm golds, plus
  a repeated teal and green), so unrelated agents routinely drew the same color — on some seeds every
  bar, dot, and legend entry on the Anomalies dashboard rendered the same amber, erasing the only
  thing distinguishing one agent from another. The palette is now eight genuinely distinct,
  theme-legible hues (also used for project and provider colors), so stacked timelines, the
  most-flagged-agents ranking, agent badges, and the project/provider avatars stay readable.
- **Settings project and member avatars get distinct colors too.** The Settings project list, the
  Settings members list, and the add-member picker drew their avatar colors from a second, separate
  palette that still carried the original defect — six slots collapsing to about three visible hues
  (a repeated teal and three near-identical warm golds) — so unrelated projects and teammates kept
  landing on the same or an indistinguishable color even after the agent palette was deduplicated.
  These avatars now share the single eight-hue palette used everywhere else, so each project and
  member reads as its own color.
- **The kiosk demo no longer spews failed test-run errors on boot.** The showcase's seeded
  incident run and its hidden A/B comparison runs were persisted in a not-yet-finished state and
  then executed by the real test runner against the read-only demo model, which has no LLM
  endpoint — so every case failed with a fail-level stack trace and raced the seeder. Those runs
  are now seeded directly in their final state (a failed run stays failed, a completed run stays
  completed), so nothing re-runs them and the boot logs stay clean.
- **The kiosk demo's "Data Analytics — SQL Correctness" suite passes on a live re-run.** The
  Data Analytics agent is told to answer from its `run_sql` tool rather than invent numbers, so
  its first turn is a tool call. Each seeded case now includes the tool round-trip (the query and
  its returned rows) in its input, so re-running the suite against a configured demo model scores
  the final written answer instead of failing on the intermediate tool-call turn.
- **The kiosk demo's tool-name filter no longer lists tools whose traces have expired.** With trace
  retention active, a long-running kiosk (or in-memory demo) deleted old traces but left their
  per-call tool-name rows behind, so the Traces tool-name filter kept offering tools that then
  matched no traces. Retention now removes those child rows together with the trace, matching the
  cascade the persistent PostgreSQL deployments already enforced (they were never affected).

### Removed

- The dashboard API's live-telemetry payload no longer carries the unused `proxyVersion` field;
  its only consumer was the retired telemetry strip.

### Security

- **Role changes now take effect immediately.** Session tokens bake the user's role at
  login, but the API trusted that baked role for the token's full 7-day lifetime — so a
  demoted admin kept admin access until their token expired. The API now re-reads the live
  role from the database on every request and ignores the stale token claim, so a demotion
  (or promotion) applies on the user's very next request.

- **Official images now trust only the production license-signing key.** Images published before
  this release embedded a throwaway test key whose private half is public knowledge, so a
  self-signed license token could unlock paid tiers on a stock image. The embedded key is rotated
  to the current production key, and the test key is now baked into the development, e2e, and perf
  images only, through a compile-time build argument — a shipped image trusts exactly the keys it
  was built with and never a runtime value. No customer licenses were issued against the retired
  key, so existing installations are unaffected; license keys issued by Proxytrace continue to
  validate as before.

- **The proxy's path-traversal guard now resists URL-encoding.** The guard that rejects `..` in a
  forwarded proxy path previously matched only a literal `..`, so a percent-encoded `%2e%2e` (or
  double-encoded `%252e%252e`) slipped past it. The path is now fully decoded before the check, on
  both the traced and pass-through proxy routes. This was not exploitable — the forward host stays
  pinned to the configured provider origin (no cross-host SSRF) — so the change is defense-in-depth.

## [1.4.0] - 2026-07-02

### Changed

- **The dashboard scales to many concurrent viewers.** The dashboard payload is now served from a
  short-lived server-side cache (10 seconds by default, configurable via
  `Statistics:DashboardCacheTtlSeconds`, `0` disables): everyone watching the same dashboard shares
  one set of statistics queries per refresh instead of each viewer re-running them all, and
  simultaneous requests no longer stampede the database. Dashboard numbers may lag reality by up to
  the configured TTL. In addition, the overall pass rate and the pass-rate sparkline now aggregate
  in the database — the sparkline shows the 50 most recent run cohorts — so dashboard load time no
  longer grows with the total amount of accumulated test-run history.

- **A more polished Tracey AI experience.** The chat got a visual and interaction pass: the composer
  now carries the animated streaming ring while Tracey works, the send button follows the gold
  primary-action treatment, the slash menu animates in and shows its keyboard shortcuts, starter
  chips stagger in and lift on hover, messages and tool cards fade in, and the "Thinking…" indicator
  is an animated three-dot wave. The statistics cards render their figures as KPI tiles (with a
  color-coded pass rate and a live-telemetry row). The **waiting card** for long-running actions was
  redesigned: it now shows each awaited action's live backend status — suite → agent with a
  case-progress bar for test runs, the A/B phase for theories — an elapsed stopwatch, and, once
  finished, a per-action outcome list with pass/fail tallies under a card-level verdict badge. All
  animations respect the system's reduced-motion preference.

### Added

- **Richer kiosk demo data covering the newer features.** The kiosk's seeded showcase now includes:
  a deliberately defective *Email Triage* agent (vague prompt, missing tool, cheap model) whose test
  suite regresses sharply — with the resulting alerts produced by the **real anomaly detector**
  (pass-rate/latency regression + an endpoint-down failed run) instead of hand-written notification
  text; anomaly-flagged outlier traces for every flag kind (a runaway tool loop, a 20k-token context
  blow-up, a prompt-cache collapse, a 14-second giant review) plus a provider-timeout error and a
  prompt-injection trace, so the outliers-only filter, distribution charts and Tracey's diagnose
  tools have real material; prompt-cache usage and rare flagged latency/token spikes across the
  14-day statistics backfill; and a completed optimization loop — validated/invalidated theories now
  link a real (hidden) A/B candidate run, and the proposals board includes an *Adopted* proposal and
  fresh triage hypotheses.

- **Tracey can diagnose an agent from its anomalies.** Ask what's wrong with an agent (there's a
  *Diagnose an agent* starter chip, too) and Tracey fetches its recent anomaly-flagged calls —
  shown as a card with per-call reason badges (high tokens, high latency, low cache hit, many tool
  calls) — analyzes the flagged traces to name the failure pattern, and turns the problem into test
  cases: added to a fitting suite, or a new suite created with a matching evaluator (she can now
  list and create evaluators — LLM judge or exact/numeric/JSON-schema match — and attach them at
  suite creation). She then runs the suite, reads the failures, and validates a concrete fix with
  an A/B-tested optimization theory, ending in a reviewable proposal when it improves the pass
  rate.
- **Conversation history for Tracey.** The Tracey AI page now keeps a conversation history: keep
  and revisit up to 20 past conversations per project, open any one to view and continue it, and
  delete the ones you no longer need. Conversations are titled automatically from your first message
  and stored locally in your browser. The history lives in a side panel on the right, hidden by
  default — the sidebar icon in the chat header opens and closes it, and the choice is remembered
  on the device.
- **Tracey suggests follow-ups.** After each reply, Tracey proposes two likely next messages as
  animated, clickable chips beneath her answer. Click one to send it immediately, or keep typing
  your own. The suggestions clear the moment you send anything and are not persisted (reopening a
  past conversation shows no chips).

### Fixed

- **Average latency no longer under-reports when some calls have no latency.** The dashboard
  summary and the per-model breakdown averaged failed calls without a recorded latency as 0 ms,
  dragging the reported mean below the real one. The average is now computed over the calls that
  actually have a latency.

- **Tracey reliably follows up after waiting on long-running actions.** Three gaps could leave a
  finished test run or theory validation without Tracey's promised same-turn analysis: a single
  transient network/server hiccup during the minutes-long result poll permanently gave up on that
  action (polling now rides out brief failures and only reports an error after several consecutive
  failed checks); a wait that crashed still counted as "done", so Tracey could end her reply
  without the outcome (the wait is now re-forced until it actually returns); and reloading the page
  mid-wait corrupted the conversation so every later message failed (the interrupted wait is now
  dropped from the model's view of the history).
- **Free models are no longer hidden from discovery.** A provider model with a price of 0 (free
  tiers, self-hosted/local models) was silently skipped during model discovery and never got an
  endpoint. Zero is now accepted as a valid, known price — free models appear with a €0 cost —
  while "price unknown" (no cost shown) is still represented by an unpriced endpoint. Models whose
  catalog price puts input above output (some batch/reasoning tiers) are no longer skipped either.
- **Saving an unchanged system prompt no longer creates a new agent version.** The "has the prompt
  actually changed?" check compared prompt templates by internal reference, so re-applying an
  identical system prompt always minted a redundant agent version.
- **Image content survives storage.** Captured image content lost its media type when persisted and
  failed to load back; the media type is now stored and restored with the image bytes.
- **Opening a trace from the title-bar search opens its details.** Clicking a trace result in the
  global search now lands on the Traces page with that trace's detail drawer open. Previously the
  page navigated and reset the filters but the drawer stayed closed, because clearing the deep-link
  parameter raced the selection and wiped it from the URL.
- **Keyboard access across selectable lists.** Evaluator, test-case and trace rows in the suite
  builder and suite detail — plus the evaluator attach/detach row and the collapsible agent widgets —
  are now real, focusable controls you can reach and activate with the keyboard, each with a visible
  focus ring.
- **Dialogs trap focus and close on Esc.** The Promote-to-test-case and New-evaluator dialogs now use
  the standard modal shell, so keyboard focus stays inside them, Esc closes them, and they announce
  themselves as dialogs to screen readers.
- **Loading no longer looks like empty.** The Error log, Audit log, agent detail, version history,
  recent-evaluations table and dashboard cards now show shaped skeletons while loading instead of
  flashing an empty state or a bare "Loading…" line and then jumping when the data arrives.
- **More of the interface is translated.** Time-range presets ("Last 15 minutes", "All time", …), the
  optimization decision-flow stage labels, proposal tool messages, the "Expected" conversation label
  and the Tracey quick-action chips now go through translation (German, Spanish, French, Italian).
- **Kiosk no longer spends on LLM calls at startup.** When an interactive `Kiosk:Endpoint` was
  configured, every kiosk boot re-queued the freshly demo-seeded `Proposed`/`Validating`
  optimization theories into the validation pipeline, firing real A/B test runs (and model
  cost) on each start. The restart-recovery pass is now skipped in kiosk mode; theories a user
  submits during a kiosk session still validate normally.
- **Tracey conversation history restores again.** Opening a past conversation from the history
  rail (and restoring the active conversation after a page reload) rendered an empty thread —
  clicking a conversation appeared to do nothing. Snapshots are now persisted in the AI SDK's
  native message format, which survives the localStorage round-trip. Conversations saved before
  this fix keep their history entry but can no longer be reopened (their messages were stored in
  a format that never restored correctly); opening one starts a fresh thread.
- **Costs display in € everywhere.** Test-run views (cost panel, champion/medals/comparison stats,
  suite totals, Tracey run cards) and agent trend statistics rendered costs with a `$` prefix even
  though all Proxytrace costs are computed and stored in EUR. Every cost readout now uses the euro
  sign, and amounts from €1 up show cents (with thousands grouping) instead of four decimals.
- **Kiosk demo traces now show real costs.** Seeded demo endpoints priced tokens in per-token
  units instead of EUR per 1M tokens, so every trace and agent statistic displayed a cost of
  €0.0000. Prices are now seeded in the correct unit, the interactive kiosk endpoint falls back
  to a small-model rate when `Kiosk:Endpoint` omits token costs, and the configuration manual's
  example uses per-1M values.
- **Kiosk demo: re-running the Email Triage test suite no longer fails every case.** The triage
  agent's prompt tells it to use its `search_kb` tool, but the seeded test cases were single-turn —
  against a live model (`Kiosk:Endpoint`) the agent's first move was a tool call, which the
  single-completion test runner scored as the answer, failing all cases. Each seeded triage case
  now embeds the `search_kb` round-trip in its input conversation, so a live re-run produces the
  final triage answer and the suite's pass/fail pattern reflects the agent's real defects.
- **Kiosk demo: "Promote" on a validated theory no longer 409s.** Two seeded validated
  optimization theories for the same agent could point at the same draft proposal, so promoting one
  left the other offering a "Promote" the server rejected (`409 Conflict`). Seeding now hands each
  validated theory its own proposal.

### Changed

- **Tracey starter chips send immediately.** Clicking a conversation-starter chip on the empty
  Tracey view now sends that request right away instead of only prefilling the message box. To edit
  a quick-action prompt before sending, pick it from the `/` menu.
- **Tracey always auto-approves actions.** The "Auto-approve actions" toggle is gone; Tracey's
  write actions (starting runs, curating suites, deciding proposals, submitting theories) now
  always run without a confirmation card, as they did with the toggle in its default position.
- **UI consistency pass.** The Providers list now uses the same framed master/detail rail as Agents,
  Evaluators, Suites and Runs; a shared switch-pill control backs the toggles on the Agents, Traces and
  Tracey screens; hand-rolled dropdowns/menus were replaced with the standard components; and a sweep
  aligned spacing, colour, shadow and status treatments to the design tokens. Behaviour is unchanged.
- **Kiosk demo seeds a premium flagship model.** The showcase's OpenAI demo models are now
  `gpt-5.4` (priced as a premium flagship, €15/€60 per 1M tokens) and `gpt-5.4-mini` instead of
  `gpt-4o`/`gpt-4o-mini`, so the seeded cost cards and model-switch proposals show meaningful
  amounts at the demo's call volumes.
- **More believable kiosk demo traces, with tool calls front and center.** The seeded showcase data
  now holds together when inspected: the Data Analytics agent gained `run_sql`/`get_schema` tools
  and every one of its numeric answers is grounded in a query round-trip, the Email Triage agent
  gained a `search_kb` tool for how-to/bug replies (while still lacking the plan lookup its
  fabrication storyline needs), and support answers about a specific order go through
  `lookup_order`/`start_return` — across the curated traces and a large share of the two-week
  history. Traces flagged as "high token count" now actually contain the pasted wall of text — an
  email thread, a full diff, a schema dump — that justifies the flag.

### Security

- **Patched OpenAPI dependency.** `Microsoft.OpenApi` is pinned to 2.7.5, replacing the transitively
  referenced 2.4.1 that carries a known high-severity advisory (GHSA-v5pm-xwqc-g5wc, OpenAPI parsing
  can hang on circular schema references).

## [1.3.0] - 2026-06-30

### Added

- **Outlier detection for traces.** Each ingested call is now flagged when it deviates from its
  agent's own recent behaviour on any of four per-call metrics — **high token count** (also the cost
  signal), **high latency**, **low turn-2+ cache hit**, and **many tool calls**. Detection is
  per-agent and adaptive: a call is flagged when a metric exceeds the agent's recent **mean ± N
  standard deviations**, so a cheap fast agent and an expensive reasoner each get their own "normal".
  The **Traces** list carries a dedicated **Anomalies** column (just before the timestamp) that shows
  an amber warning chip on each flagged call — hover it for the reasons — plus a new **Outliers only**
  toggle that filters the list to just the outliers, and the agent detail page gains a **Recent outliers** widget
  that lists why each recent call was flagged. Admins tune the sensitivity (enable/disable, sigma,
  minimum samples, baseline window) under **Settings → Outlier detection**. Existing traces are not
  retroactively flagged; detection applies to calls ingested from now on.
- **Call distribution stats on the agent page.** The agent detail view's **Performance** card now
  shows one small card per stat in a single grid that reflows to the available width: the window
  **totals** (pass rate, traces, tokens, cost, latency — each with a trend sparkline), then the
  **mean ± standard deviation** of an agent's successful calls over the selected range — **input** and
  **output tokens** and **latency** (per call), and **cost**, **cache hit rate** (turns after the
  first, which can't be cache hits) and **tool calls** (per conversation). Each distribution card draws
  a small **density curve** of the real sample shape — hover to read a slice's value range and how many
  calls (or conversations) fall in it — and metrics with no signal in the window (an agent that never
  caches or calls a tool) are dropped rather than shown empty. Everything shares one time-range selector
  that **persists as you switch agents**, and updates live as new traces arrive, so a single card shows
  not just the totals but how consistent — or skewed — your agent's calls are.
- **Sample a test run multiple times.** When you start a run you can now pick a **sample count (1–5)** —
  Proxytrace runs each selected endpoint that many times and **averages the results per endpoint**, so
  non-deterministic models don't hide flaky cases. The results matrix shows one column per endpoint with
  a per-case **pass fraction (e.g. 4/5)** and average score, a new **Flaky** filter surfaces cases whose
  samples disagree, and clicking a cell drills into the individual **sample i/N** runs. Anomaly detection
  and the auto-optimization loop operate on **one representative run per endpoint**, so sampling never
  fires duplicate anomalies or biases a proposal toward a single lucky sample. Existing single-sample
  runs look exactly as before.
- **Two-factor authentication (TOTP).** Protect your account with a second factor from an
  authenticator app (Google Authenticator, Authy, 1Password). Turn it on under **Account security**
  (in the account menu): scan the QR code, confirm a code, and save the **10 one-time backup codes**
  you're shown. After that, signing in asks for a 6-digit code — or a backup code if you've lost your
  device. Disabling it requires your password. Admins can clear a locked-out user's MFA from
  **Settings → Users** (**Reset MFA**). MFA is opt-in per user, free on every tier, and applies to
  password (local) sign-in; SSO/OIDC handles MFA at your identity provider. Enabling, disabling, and
  failed code attempts are recorded in the audit log, and the verification endpoint is rate-limited.
- **Forgot your password? Self-service reset.** The sign-in screen now has a **Forgot password?**
  link. Enter your email and Proxytrace sends a one-time reset link — valid for 1 hour — that lets you
  choose a new password and signs you straight in. **No SMTP? You're still covered:** if outgoing
  email isn't configured, the reset link is written to the server log for the operator to relay, and
  an admin can mint a one-time reset link for any user from **Settings → Users** (the **Reset
  password** button). Reset requests and completions are recorded in the audit log, and the public
  reset endpoints are rate-limited.
- **Cancel or reject an optimization theory.** On the **Optimization Theories** board you can now
  dismiss a theory you don't want to pursue: **Reject** a *Proposed* theory to skip A/B validation
  entirely, or **Cancel validation** on a *Validating* theory to abort its in-flight A/B run. Either
  way the theory moves to *Rejected* and can still be reset later. Validation already runs **one
  theory at a time**, so these controls let you clear the queue and stop runs you no longer need.
- **Much broader audit-log coverage.** The audit log now records far more of what happens in
  Proxytrace: the whole optimization-theory loop (theory submitted/reset/rejected, plus the A/B
  pipeline's validated/invalidated decisions, the proposals it generates, and proposals it
  auto-adopts), the test-run lifecycle (cancel, optimize, delete) and recurring **schedules**
  (create/update/delete/run-now), trace deletions, agent-version moves, **test-case edits**, the
  destructive **non-model data purge**, and the one-time at-rest **secrets backfill**. **New OIDC coverage:** the first-time
  provisioning of an SSO user is now recorded. **New failure visibility:** a forbidden attempt to
  change something (HTTP 403) is logged as an **Access denied** failure, so privilege-probing shows up
  alongside failed sign-ins. All new action types are filterable on the audit-log page.

### Changed

- **The Evaluators page remembers your selected time range.** The range selector (1h / 24h / 7d /
  30d) on the evaluator detail now persists, so it survives switching between evaluators and reloading
  the page instead of snapping back to 7d each time (matching the agent detail page).
- **Consistent typography, spacing and corners across the app.** Swept every screen onto the design
  system's type scale, spacing steps, corner radii and surface colours, replacing dozens of off-scale
  one-offs that left labels, paddings and rounded corners subtly mismatched between otherwise-identical
  panels. Purely visual — no behaviour changes.

- **Live duration, cost and tokens during a test run.** While a run is in progress its model cards now
  count **duration, cost and tokens** up as each case lands, instead of sitting at "—"/`$0` until the
  run finished. Running runs are also highlighted in the left-hand **Test Runs** list with an animated
  accent ring and a pulsing **Running** tag, so an in-flight run is obvious at a glance.
- **The Test Runs list loads incrementally.** Instead of fetching a large batch up front, the runs rail
  now loads the most recent runs first and reveals older ones on demand via a **Load more** button —
  faster to open and lighter on projects with a long run history.
- **Test-run model comparison, rebuilt around your production model.** A run's results now open with
  the model you have **in production** (the agent's deployed endpoint) as the **baseline** — a
  highlighted champion card carrying the headline pass rate plus duration, cost, and token totals.
  Every other model is a **candidate**, read as deltas measured *against production*: pass-rate
  points, faster/slower, and cheaper/pricier, coloured green when the candidate wins that metric and
  red when it loses — so it's obvious at a glance whether a candidate is worth switching to. Three
  **award medals** call out the highest pass rate, the fastest, and the cheapest model, and the
  evaluator breakdown now highlights the leading model per evaluator. When a run doesn't include your
  deployed model, the best performer stands in as the baseline. Deltas and medals still appear only
  once the whole run group has finished.
- **Tracey no longer cuts a reply short at a turn limit.** The assistant's per-turn tool-step cap and
  its "Step limit reached" notice have been removed, so a complex request that needs many tool steps
  now runs to completion instead of stopping early and asking you to continue. (A high internal
  safety backstop still prevents a runaway loop.)
- **Tracey's per-response stats now break down token usage.** The quiet status row beneath each reply
  shows **input tokens**, the **share of input served from cache**, and **output tokens** instead of
  a single total — making it clear how much of a turn's cost was cached prompt vs. fresh input vs.
  generated output.
- **Tracey now focuses on your own agents.** Her data tools hide Proxytrace's internal *system*
  agents — Tracey herself and the evaluators that score your test runs — by default, so "list my
  agents", token-usage charts, recent test runs (the internal A/B validation runs are hidden too),
  and trace searches stay about your work rather than the platform's own activity. Ask explicitly
  (e.g. "include the Tracey agent" or "list system agents") and she'll add them back in.

### Fixed

- **Evaluator statistics now include historical evaluations.** The Evaluators page (average score,
  evaluation count, pass rate, average latency, score distribution and the per-evaluator sparkline)
  reads a query-optimized projection that was only written for evaluations recorded after the feature
  shipped — so existing results showed a dash or zero even when an evaluator had plenty of past
  evaluations. A one-time, idempotent backfill now rebuilds that projection for older results on
  startup, so historical evaluations count toward the statistics.
- **Test-run latency now measures the model, not the wall clock.** The latency shown on a run's
  per-model comparison cards (the champion card, the candidate "Speed" deltas, and the "Fastest"
  medal) and on an optimization proposal's A/B-test card was computed as a wall-clock timer over the
  whole run (`completed − started`), so it also counted the time the run waited in the queue and the
  time spent running evaluators, and was compressed by cases running in parallel — none of which is
  the model's latency. It now reports the **average per-case inference latency** (the same figure the
  test-case matrix shows), so model speed comparisons reflect the model itself and a run that merely
  waited longer in the queue no longer looks slower.
- **Polished several rough UI details.** The test-run "Evaluation started" confirmation now uses a
  proper check icon instead of a typed character; the evaluator score legend no longer crams a full
  sentence into each coloured pill (short pills now sit beside their plain-text meaning); the **Error
  Log** and **Audit Log** page titles match the size of every other page header; a trace's ID in the
  detail panel renders at its intended size again; and the "move version" agent picker no longer shows
  a transparent, off-theme list. A further pixel-level pass squared up details across the app: **long
  names, IDs, links and titles now shorten with an ellipsis** instead of overflowing their card or
  shoving neighbouring controls off-screen (agent/suite/evaluator/run headers, notifications, global
  search results, password-reset and invite links, the member picker, Tracey tool cards and the
  playground tool list); **section, dialog and entity-detail titles now share a single weight**; the
  scheduled-runs **"recent runs" strip lays out as a horizontal row** again instead of a vertical
  stack; and assorted **side-by-side inconsistencies** were aligned — matching selected-row styling
  and panel framing in the evaluator bench, label casing in the playground parameters, row indentation
  and divider alignment between suite tabs, and date-column contrast and form-field labels on the
  admin and sign-up screens. Finally, **status and agent chips now sit vertically centred against
  their heading** across every detail header (test runs, suites, agents, evaluators, the evaluator
  bench and the proposals board) — previously the chips drooped a few pixels below the title — and
  the two chips in a run header now render at a **single matching size** instead of one larger than
  the other.

- **Deleting a test run no longer flashes a "not found" error.** Removing a run refreshed the whole
  run namespace, which re-fetched the just-deleted run's own detail and surfaced a 404. Delete now
  drops the run from the list immediately and skips re-fetching the gone detail, so it disappears
  cleanly.
- **Interrupted test runs no longer hang in "Running" forever.** A run in progress when the server
  restarts (deploy, crash, container recycle) could be stranded in **Running**/**Pending**
  indefinitely, since its work only lived in memory and can't be resumed. On startup the server now
  marks any such orphaned run **Cancelled**, so the list and headers reflect reality instead of a
  ghost run that never finishes.
- **A running test run now shows a live duration.** Each model card's **Duration** stayed at "—" for
  the whole run (the per-run state only flips once a case finishes and that transition wasn't streamed)
  and only filled in at the end. A run now reads as **running** as soon as its first case starts, with
  the duration ticking up live alongside cost and tokens.
- **A finished test run now updates to "Completed" on its own.** The run header could stay stuck on
  **Running** after the run had actually finished on the server, only flipping to **Completed** after a
  manual page refresh. The live view now keeps its event stream open until the *group* finishes (not
  just its individual runs) and flips the status the moment the completion event arrives, so the header,
  the **Cancel** button, and the live progress bar all settle without a refresh.
- **Test-run pass rate no longer shows a long decimal.** Averaged pass rates on the comparison cards
  rendered as e.g. `96.66666666666667%`; they're now rounded to a whole percent like every other
  pass-rate readout.
- **Outlier-detection changes show a proper label in the audit log.** Tuning **Settings → Outlier
  detection** records an audit entry, but the Audit Log page rendered that action with a blank,
  uncoloured, unfilterable label. The `Outlier Settings Updated` action now shows its label and
  colour like every other audit action.
- **Deleting an agent from its detail page works again.** With an agent open, the detail view kept
  several live-update streams connected at once, which on the bundled (HTTP/1.1) setup could use up
  the browser's small per-site connection budget. A delete then had no connection left and silently
  never reached the server — the confirmation closed but the agent stayed in the list. The detail
  view now shares one connection across those streams, freeing capacity so Delete (and other actions
  taken while viewing an agent) go through reliably.
- **Enabling MFA no longer fails when the setup request is sent twice.** Two near-simultaneous
  "set up MFA" requests for the same account (e.g. a double-click or a retried request) raced on the
  one-enrollment-per-user rule and the second crashed with a server error. Setup now tolerates the
  race and returns the enrollment that took effect, so the QR code always matches the stored secret.
- **Traces show their message preview again.** Traces ingested before the list's denormalised preview
  column was introduced rendered with a blank message preview. A one-time, idempotent startup backfill
  now recomputes the preview (the first user message) for those rows in bounded batches, so every trace
  shows its preview after the next restart — no longer only the ones captured since the column was added.
- **Password reset and invite links point at the right address.** The emailed reset/invite links fell
  back to the API server's own host and port when no explicit frontend URL was configured, producing a
  link the browser couldn't open. They now use the configured frontend origin (`Frontend:AllowedOrigin`),
  so the links work out of the box in every environment.
- **"No traces" message instead of setup instructions when filters exclude everything.** The Traces
  page treated an empty list from the new **Outliers only** filter as an empty project and showed the
  first-time setup instructions. It now shows "No traces match your filters" when any filter (including
  Outliers only) is active, and keeps the setup instructions only for a project with no traces at all.
- **Dashboard and statistics stay fast on large datasets.** On a database with a lot of history the
  dashboard and statistics aggregates could take several seconds because PostgreSQL's query planner,
  working from out-of-date table statistics, chose a plan that scanned the whole traces table the slow
  way. Proxytrace now keeps the planner's statistics fresh on the high-volume traces table (more
  frequent auto-analysis), so the same queries run in a few hundred milliseconds. If you bulk-import or
  restore a large database, run `ANALYZE` once afterwards so the speed-up applies immediately rather
  than after the next automatic analysis.
- **Tracey's own traces are captured reliably again.** Tracey runs inside the app, but her captured
  calls were being routed through the same Redis message stream used to bridge the standalone
  ingestion proxy — so whenever that stream was unavailable, every Tracey trace was silently dropped
  (her replies still worked, but the trace link reported "still being captured" forever and nothing
  showed in Traces). In-app captures now persist directly, with no dependency on the proxy's
  transport.
- **Test-run results stay readable with many evaluators.** The test-case matrix used to scroll inside
  its own card, shrinking to an unusable height when a run had lots of evaluators. The whole results
  column now scrolls as one unit, so the matrix keeps its full height.
- **Global search hides built-in system agents and their traces.** The title-bar search and recent
  feed no longer surface internal system agents (Tracey, the optimization/A-B optimizer agents,
  agentic-evaluator agents) or the traces they generate — only your own agents, suites, traces,
  evaluators, and test cases. Any previously indexed system entities are purged on the next reindex.
- **Global search again shows recent agents, suites, and evaluators.** The title-bar search's default
  (empty-query) list was being crowded out by traces on busy projects, leaving only recent traces.
  Each entity type is now surfaced independently, so recent agents, test suites, evaluators, and
  traces all appear again.
- **Evaluator playground shows tool-call responses.** When a selected past evaluation's response was a
  tool call with no text, the **reference** showed "—" and the **candidate** was blank (the scoring
  itself was unaffected). Both now render the tool call (e.g. `[tool call] get_weather({…})`).
- **Numeric evaluator scoring no longer depends on the server's locale.** The numeric-match evaluator
  parsed expected and actual values using the server's regional settings, so a value like `3.14` could
  be read as `314` on a non-US host and silently flip a pass to a fail. Numbers are now parsed the same
  way everywhere (invariant format), and a tool message carrying more than one result no longer drops
  the extra results from its text.
- **Operator error log keeps its full retention under bursty errors.** When many errors shared the
  exact same timestamp, trimming the error log to its configured size could delete the whole group at
  the cutoff and leave fewer entries than intended. Trimming now breaks ties deterministically, so it
  keeps exactly the configured number of most-recent errors.
- **Provider pricing with input cost ≥ output cost can be saved again.** Activating or updating a model
  endpoint wrongly required input token cost ≤ output token cost, rejecting legitimate provider pricing
  (some cached, batch, and reasoning tiers price input at or above output). That rule is gone; cost
  calculation is unaffected. Proxytrace also now rejects nonsensical stored numbers — out-of-range pass
  rates and p-values, and invalid inference parameters such as negative max tokens or NaN/Infinity —
  instead of persisting them.
- **Promoting a response-less trace returns a clear 400.** Promoting a captured call that has no
  response into a test case failed with a generic server error (500); it now returns 400 (bad request),
  like the adjacent validation cases.
- **A slow trace ingest no longer produces duplicate traces.** When persisting a single captured call
  took unusually long (heavy database contention or a very large transcript), the ingestion worker could
  reclaim the still-in-flight item and process it a second time — creating two identical traces, two
  notifications, and two outlier evaluations. The worker now skips an item it is already processing and
  waits much longer before reclaiming, so a slow-but-live ingest is never double-counted.
- **Sign-in stays fast and email is treated case-insensitively.** Email addresses are now stored in a
  normalised (lower-case) form and looked up by exact match, so logging in uses the email index instead
  of scanning the whole users table, and `Foo@x.com` and `foo@x.com` can no longer become two separate
  accounts. Existing addresses are normalised once on upgrade. Creating a model or endpoint during a
  burst of traffic also retries cleanly instead of failing if two requests create it at the same moment.
- **Cancelled playground and Tracey calls are no longer recorded as failed traces.** Cancelling a model
  request (or shutting the app down mid-call) recorded a phantom HTTP-500 trace that polluted statistics
  and outlier detection. Cancellations are now ignored rather than captured.
- **Latency percentiles honour the "exclude system agents" option on PostgreSQL.** The p50/p95/p99
  latency and live-telemetry queries ignored the option that hides built-in system agents (Tracey, the
  optimizer/evaluator agents), so those calls could skew the percentiles. The option is now applied on
  PostgreSQL, matching every other statistic.
- **Test-suite run statistics stay fast as history grows.** Opening the test-suites list or a single
  suite read the entire run-statistics table and filtered it in memory; it now asks the database only
  for the suites in view. Archived-entity lists are likewise filtered in the database rather than after
  loading every row.
- **Real-time streams clean up and stay bounded.** The long-lived trace, proposal, theory, and
  notification streams now send a periodic keep-alive, so a connection that dies without notice (a
  half-open socket) releases its slot instead of lingering, and the two streams every client subscribes
  to now cap their subscriber count like the others — protecting the server from a flood of stream opens.
- **Background backfills release database resources promptly.** The one-time preview and secret-
  encryption backfills, which run in batches over potentially millions of rows, held onto a database
  context per batch for the lifetime of the process; they now dispose each batch's context as they go.
- **Audit log shows a label for email-settings changes.** Saving SMTP/email settings produced an audit
  entry the Audit Log page rendered with a blank action label and no colour; it now shows a proper
  "Email Settings Updated" label and badge.

### Security

- **Evaluator test bench no longer exposes another tenant's test case via a supplied id.** Loading or
  running a test case on the bench verified access to the *evaluator* but not to the separately
  supplied test-case id, so a signed-in user could pass a test-case id from another project and read
  its conversation, expected and actual responses, and scores. The test case's owning project is now
  verified too, returning **404** on mismatch (no existence oracle).
- **Internal error detail no longer leaks on a few self-handled responses.** The playground's streamed
  error event and the admin email / SMTP connection tests echoed raw exception text — which can carry
  SQL, schema, or file-path detail — even in production, bypassing the global suppression. These now log
  the fault under an error id and return a generic message outside Development, matching the rest of the
  API.
- **Closed cross-tenant access gaps on statistics, the playground, the evaluator test bench, trace
  promotion, and theory submission.** Several endpoints accepted a project, agent, trace, or evaluator
  id without checking that the caller is a member of the owning project, so any signed-in user could
  read another tenant's data — or, worse, run a model completion on another tenant's provider
  credential (the playground and the evaluator test bench's *run*). All of these now resolve the
  owning project and return **404** when the caller lacks access (no existence oracle). The dashboard
  additionally refuses the unscoped, all-tenant aggregate to non-admins: a normal user must request a
  project they belong to (the app already does this), while administrators keep the global view.
- **Password-reset links are no longer written to the log by default.** When email is unconfigured or
  sending fails, Proxytrace previously logged the full one-time reset link (a live credential for an
  hour) so a sole administrator could still recover access. The log now records only a redacted hint by
  default; the full emergency link is logged only when an operator explicitly opts in with the new
  `Authentication:EmergencyLogResetLink` setting. The in-process auth, MFA, and rate-limit state is also
  now documented as single-instance by design — running multiple API replicas would split those limits
  per replica.

## [1.2.0] - 2026-06-24

### Added

- **Offline-only licenses for air-gapped installs.** Proxytrace now recognises license keys issued
  as *offline-only*: they are verified entirely on the box (signature + expiry) and are **never**
  checked against the license server, so an install with no outbound internet keeps running without
  hitting the offline grace window. Such a key cannot be revoked and works until its built-in expiry
  (capped at 365 days), at which point the installation downgrades to Free. **Settings → License**
  shows an "offline license" note and hides **Re-check now** for these keys. Normal (online) keys are
  unchanged — still re-checked every 24 hours and revocable.

## [1.1.0] - 2026-06-23

### Added

- **Secrets are now protected at rest.** Upstream provider API keys are encrypted in the database
  (recovered only to call the provider), while inbound Proxytrace API keys and invite tokens are
  stored as one-way hashes. As a result, a newly generated API key and a new invite link are now
  shown **once, at creation** — copy them then; afterwards the key list shows only a short,
  non-secret prefix to identify each key. Existing keys, provider credentials, and pending invites
  are protected automatically on upgrade, with no action required and no disruption to live
  integrations.

- **Email notifications.** Operators can configure outgoing SMTP under **Settings → Email notifications**, including an instance-wide **minimum severity** (default **Warning**, so members are emailed warnings and critical alerts by default). Users can opt in to receive notification alerts by email, choosing **All**, **Critical**, or **None** from the account menu (defaulting to **All**). The SMTP password is encrypted at rest using ASP.NET Data Protection.

- **Audit log of system actions.** Proxytrace now keeps a durable, user-attributed record of
  significant actions — authentication events (sign-in, failed sign-in, sign-out, first-admin setup,
  legacy-account claim), user invites/sign-ups/role changes/deletions, project create/rename/delete
  and membership changes, agent endpoint changes and deletions, test-suite / test-case / evaluator
  creation, update and deletion, test runs started (manual, scheduled, or via MCP), optimization
  proposal status changes, API keys minted and deleted, provider/endpoint configuration changes, and
  license changes. Each entry captures **who** performed it (the signed-in user, the owner of the API
  key used, or the system for scheduled work), **what** was acted on, and **when** — and entries are
  kept even after the thing they refer to is deleted. Admins see the full trail under
  **Settings → Audit log**; project members see their own project's trail (but not instance-wide
  actions). The log is lossless and retained for 365 days by default.

- **Cached-input tokens are now tracked and priced separately.** Many providers serve part of a
  prompt from their cache at a much lower rate. Proxytrace now captures how many of each call's input
  tokens were cache-served (from both the ingestion proxy and Playground/test-run/evaluator calls),
  fetches the cheaper **cached-input price** from the model-price catalog alongside the input/output
  prices, and factors it into every cost estimate — so the numbers reflect what you actually pay. A
  muted **"(N% cached)"** hint now appears next to the input-token figures across Traces, the
  dashboard, the Playground, agent and run summaries, and the LLM-judge cost panels. Calls with no
  cached price keep costing exactly as before.

- **Connect external AI agents over MCP.** Proxytrace now hosts a built-in
  [Model Context Protocol](https://modelcontextprotocol.io) server at `/mcp`, so external agents
  (Claude Desktop, Cursor, your own scripts) can use Proxytrace the way the built-in Tracey assistant
  does — listing and reading agents, traces, suites, runs, proposals and statistics, curating suites
  from captured traces, starting test runs, analysing run failures, and submitting A/B-tested
  optimization theories. It also ships **guided workflows** (MCP prompts an agent surfaces as slash
  commands — `optimize_agent`, `curate_suite`, `run_tests`, `review_proposals`, `project_insights`)
  that walk an external agent through the same playbooks the built-in Tracey assistant uses. It
  authenticates with a Proxytrace **API key** (minted
  on the Providers page): the key's project becomes the agent's working context. API keys now carry
  explicit **capabilities** — *Ingestion proxy*, *MCP read*, *MCP write* — chosen when the key is
  created, so an agent key can be made read-only and a proxy key can't drive MCP (least privilege).
  Each key also has an **owner** (a user, chosen at creation): every MCP call is attributed to that
  user. See the **MCP Server** guide for client setup and the full tool list.

- **Multilingual UI with per-user language.** Proxytrace can now display its interface in multiple
  languages, starting with **German** alongside English. Each user picks their own language from a
  grouped **Language** section in the account menu (top-right) — each option shown with its country
  flag — and the choice is saved to their account so it follows them across devices and browsers. Technical terms that AI engineers expect in English — Tool, User, Assistant,
  Trace, Token, Prompt, Agent, and the like — are deliberately kept untranslated. English remains the
  source language, and new translations are produced by an audience-aware translation tool, so more
  languages can be added without code changes.

- **Stop Tracey mid-reply.** While Tracey is thinking or replying, her **send** button now becomes a
  **Stop** button — pressing it cancels the response. The in-flight model call is torn down (not left
  running in the background), so a long or off-track answer can be halted immediately. If she was
  waiting on a long-running action you started (a test run or optimization theory), stopping only ends
  her wait — that action keeps running on the server and its result still lands on the Runs/Proposals
  page.

- **Jump from an error toast to the captured error.** When a backend request fails, the red error
  toast is now clickable for admins — selecting it opens the **Error Log** with that exact error
  already selected, so you go straight from "something broke" to its full stacktrace. The toast
  carries the captured error's id; non-admins (who can't see the Error Log) get the plain,
  non-clickable toast as before.

- **Notifications inbox in the top bar.** A new bell icon in the top bar — with an unread badge —
  opens a notifications inbox available on every page. It surfaces negative anomalies detected after
  each test run — a run that **failed** (e.g. the endpoint was unavailable), a **drastic pass-rate
  drop**, or a **strong latency increase** versus the suite's recent baseline. Alerts arrive live (no
  refresh), are colour-coded by severity, deep-link to the affected run, and can be marked read or
  dismissed; an unread count shows at a glance. The inbox is multi-purpose by design: the same surface
  will carry other notification kinds (such as a ready optimization proposal) and, in a future
  release, additional delivery channels like email.

- **A suite's run history at a glance.** The suite detail panel has a new **History** tab listing
  that suite's previous runs (newest first, with per-model pass rates); clicking a run opens it on
  the Runs page. The history is fetched suite-scoped, so it isn't diluted by other suites on the same
  agent. The header's run button is now simply labelled **Run**.

- **Periodic test-run scheduling (Enterprise).** Test suites can now be run automatically on a
  recurring schedule against a fixed set of model endpoints. Pick a frequency — **hourly** at a
  chosen minute, **daily** or **weekly** at a chosen time of day (UTC), or a **custom** every-N
  minutes/hours/days interval — and the dialog **previews the exact next execution date/time** as
  you choose. Schedules are managed from the new **Scheduled** tab on the Runs page — create, edit,
  pause/resume, and run-now — and each schedule card shows its cadence, the next run's date/time, and
  a summary of its most recent runs. Scheduled runs feed the optimization loop exactly like manual
  ones. Creating and managing schedules requires an Enterprise license; existing schedules stay
  listable after a downgrade but stop running until re-licensed. You can also create and manage a
  suite's schedules directly from its detail panel on the Suites page.

- **The Traces page empty state now shows how to ingest.** Instead of only a link to the manual,
  an empty Traces view displays the project's actual OpenAI `base_url` and a copy-paste quick-start
  snippet (Python / TypeScript / C# / curl), so you can wire up the proxy without leaving the page.
  A filtered-but-empty view shows a distinct "no match" hint instead.

- **The manual's Proxy Setup page now shows your instance's real endpoint.** When the manual is
  read from a running Proxytrace instance (served at `/docs`), the OpenAI `base_url` box fills in
  the operator's actually configured proxy host instead of a placeholder, and a clearer "what the
  proxy endpoint is" section explains where to copy the ready-to-use endpoint in the app.

- **Tracey can now curate test suites from traces.** The in-app AI assistant can build a new test
  suite for an agent from captured traces, add traces to an existing suite as test cases, set a
  case's expected output, and remove cases — closing the curate→benchmark→run loop entirely in
  chat. She can also **cancel an in-progress test run**. All are confirmation-gated writes.

### Changed

- **Tracey shows cards on demand instead of one per tool call.** A multi-step answer used to stack
  a full card for every lookup Tracey did on the way to the result. Now Tracey decides what's worth
  showing: the reads she does for her own reasoning collapse to a quiet, expandable one-line trace,
  and a full card appears only when that card *is* the answer you asked to see. Charts, tables, the
  entity you asked about, live test-run/optimization cards, and confirmations still render in full —
  the change only quiets the intermediate lookups, so the thread reads cleaner.

- **A suite now runs against at most three model endpoints at once.** Both manual runs and
  scheduled runs are capped at three endpoints per run, so a model comparison stays focused (and
  bounded in cost). Endpoints are now picked from a **searchable multi-select** (replacing the old
  stacked checkbox list, which scaled poorly with many models) — type to filter, selected models
  show as chips, and the picker disables further options once three are selected. The API rejects
  any attempt to exceed the limit.

- **One consistent left-hand list across the workspace.** Agents, Evaluators, Test Suites, Test
  Runs, and the Evaluator Playground now share a single left-column design — the same framed panel,
  the same header layout (title, count, create, search, filters), the same column width, and the
  same selected-row highlight everywhere. Previously each page styled its list differently; the
  views now feel like one product.

- **The Test Suites workspace is easier to scan and edit.** The performance strip is now a compact
  single-line KPI row (no boxed dividers, roughly half the height). In the **Test Cases** tab, the
  old Current / Add-from-traces tab chips are gone — current cases are always shown, and a single
  **Add from traces** button opens a full picker (search, time-range filter, live conversation
  preview); chosen traces stage inline as **Pending add** rows until you Save. In the **Evaluators**
  tab, each evaluator now attaches/detaches with a slide **toggle** instead of a checkbox. The suite
  list cards are more compact, mirroring the Agents list (avatar, agent subline, cases · pass rate ·
  last run).

- **The Test Suites page is now a master–detail view.** Instead of a grid of cards that each
  opened an edit modal, the page is a suite list on the left and a single workspace panel on the
  right. The panel leads with the suite header (run / delete) and a performance strip —
  bucket-selectable run statistics over a time window (Last run / last 7 days / last 30 days / all
  time — pass rate, run count, average run duration, total cost) — then a tabbed editor for the
  suite's **Test Cases**, **Evaluators**, and **Schedules** (add, remove, edit, attach/detach, and
  schedule inline). Staged edits collect in a sticky **Save changes** bar at the foot of the panel.
  Creating a new suite still uses the step wizard.

- **The dashboard and traces views stay fast with very large trace volumes.** Trace statistics
  (token usage, latency percentiles, call trends, per-agent rollups, live telemetry) now aggregate
  inside the database instead of loading every matching call into memory, and the traces table
  reads a lightweight row projection instead of the full request/response payload of each call.
- **Trace ingestion keeps up under high proxy load.** The ingestion worker now persists captured
  calls in parallel (tunable via `Messaging:MaxConcurrency`, Redis deployments only) and reads the
  stream in larger batches. The dashboard's **Queue depth** now reflects the real ingestion backlog,
  so a consumer falling behind is visible before unprocessed traces are dropped.
- **Live streams and the optimization views use less CPU and memory under load.** Real-time SSE
  event payloads are now serialized once and shared across all connected clients instead of being
  re-serialized per open stream, the proxy and Tracey token relays no longer allocate a throwaway
  buffer per streamed line, and the evaluator test-history queries read a lightweight row projection
  instead of every full result (including its stored response payload).
- **Test-run live updates scale with many concurrent runs.** The test-result event broadcaster now
  routes each event directly to the subscribers of that run/group instead of scanning every live
  subscriber on the instance, so a busy multi-run period no longer does work proportional to the
  total number of open streams per event.

### Removed

- **Error-report dialog removed from the error toast.** The "Send" action on error toasts and its
  report dialog have been retired — they posted a one-off server log line and nothing more. Errors
  are still surfaced as toasts and logged server-side; a proper error-reporting flow will be built
  from scratch in a future release.

### Security

- **Developer-local config is no longer baked into container images.** `*.local.json` files — which
  may carry a developer's license key or other secrets — were not excluded from the Docker build
  context and could be copied into a locally built image. They are now ignored, so a personal
  `appsettings.local.json` can no longer leak into an image (the official release images, built from a
  clean checkout, were never affected).

- **Systemic cross-tenant access (IDOR) across the CRUD and SSE APIs is now closed.** The app is
  multi-tenant (resources belong to a project; users belong to projects; admins bypass), but the bulk
  controllers never checked membership: any authenticated user could read, modify, delete, or trigger
  billable runs on **another tenant's** traces, agents, agent versions, proposals, theories, test
  suites/cases/runs/run-groups/schedules, evaluators, notifications, and search — and the real-time
  streams broadcast every tenant's events to everyone. A central `IProjectAccessGuard` (admin bypass)
  now backs every one of these endpoints: a single resource you can't access returns `404` (so its
  existence doesn't leak); list endpoints are scoped to your member projects instead of returning all
  tenants' rows; per-resource streams are membership-checked before subscribing; and the global trace
  and notification streams filter each event to your projects.

- **Projects and their members are no longer enumerable across tenants.** `GET /api/projects`,
  `GET /api/projects/{id}`, and `GET /api/projects/{id}/members` had no membership filter, so any
  authenticated user could list every project, read any project's details, and harvest any project's
  members' emails. Non-admins are now scoped to the projects they belong to (admins still see all),
  and out-of-scope projects return `404` so their existence does not leak. Membership can also no
  longer be mass-assigned through the generic project update: `memberIds` was dropped from the update
  request, so the member set changes only via the dedicated add/remove-member endpoints.

- **The user roster is no longer exposed to non-admins.** `GET /api/users` (every user's email,
  role and timestamps), `GET /api/users/{id}`, and `GET /api/users/{id}/projects` were callable by
  any authenticated user, leaking the full user base and their PII. They now require the Admin role,
  matching the existing role-change and delete endpoints; the self-service `me` endpoints stay open.
- **Test-support and `/seed` endpoints are no longer reachable on real deployments.** The e2e helper
  endpoints (including a destructive `POST /api/test/reset` that wiped all run data, and the
  per-controller `/seed` injectors) were exposed to any authenticated user in production. They are
  now gated behind a `TestOnlyEndpoint` guard (Development env or `TestSupport:Enabled`), so a
  normal user can no longer wipe or fabricate data.
- **Proxy API keys are now generated with a cryptographic RNG** instead of a GUID, and the
  long-lived session token is accepted in the `?access_token` URL only on SSE (`…/stream`) routes
  rather than on every request — closing a token-in-URL leakage path. Client error reporting
  (`POST /api/errors`) now requires authentication.
- **Upstream provider API keys are no longer rendered in diagnostics.** `ModelProvider` is a record
  whose compiler-generated `ToString()` printed every property, so a configured upstream credential
  could surface in a log line, exception message, or debugger string. The key is now redacted from the
  record's string representation.
- Updated `dompurify` (the HTML sanitizer behind the message HTML view and the search-snippet
  preview) to 3.4.10, picking up upstream sanitization-bypass fixes, and refreshed the frontend
  dev toolchain so `npm audit` reports no known vulnerabilities.

### Fixed

- **The ingestion proxy can decrypt upstream provider keys again.** Now that provider API keys are
  encrypted at rest, the standalone proxy needs the same ASP.NET Data Protection key ring as the app
  to recover a key before forwarding a call — without it every proxied request failed. The proxy now
  loads that key ring, and the shipped `docker-compose.yml` mounts the shared key-ring volume into
  both services. Operators running the proxy from a **custom** Compose must give it the same
  `PROXYTRACE_DATA_DIR` volume as the `api` service, or proxied calls cannot authenticate upstream.

- **Model-call clients no longer leak their HTTP transport, and the tool-schema parser no longer
  leaks pooled buffers.** Every LLM call (test runs, the Playground, agentic evaluators, and the
  prompt/tool optimizers) built a fresh client wrapping a disposable provider transport that was
  never disposed — so across a run's cases × evaluators × baseline/candidate A/B calls each one
  abandoned its transport state. The per-request tool-definition builder also parsed each tool's
  JSON schema with a `JsonDocument` that was never disposed, defeating its pooled-buffer reuse on
  every tool of every request. The model client is now disposable and every caller releases it
  immediately after use, and the schema parse is scoped so its buffer is returned right away.

- **Test run groups no longer leak `CancellationTokenSource` instances.** Every foreground,
  background, and A/B validation run group created an owned `CancellationTokenSource` plus a linked
  source (to combine the caller's token with run cancellation), but the `finally` only removed the
  owned source from the runner's registry — neither was disposed, and the linked source's reference
  was discarded entirely so it could never be disposed. The optimization loop fires baseline + candidate
  runs per theory, so both sources (and the callback the linked source registers on the caller's token)
  accumulated steadily in a long-running process. Both are now disposed in the `finally` (the linked
  source before the owned one).

- **Deleting a model provider or endpoint can no longer wipe your traces.** The trace history
  (`AgentCall`) and the endpoint→provider link were configured to *cascade* on delete, so a single
  hard delete of a provider could have removed every endpoint under it and, with them, every trace
  recorded against those endpoints — irreversible telemetry loss. Both foreign keys are now
  `Restrict`: providers and endpoints are still removed the safe way (they are archived, which keeps
  their history), but a stray hard delete or manual database statement can no longer cascade through
  to the traces table.

- **Deleting a model endpoint can no longer wipe its test-run history.** The `TestRun → ModelEndpoint`
  foreign key was still configured to *cascade* on delete (the sibling of the provider/endpoint fix
  above), so a stray hard delete of an endpoint could have removed every test run recorded against it.
  It is now `Restrict`: endpoints are still removed the safe way (they are archived), but a hard delete
  or manual database statement can no longer cascade through to the test-run history.

- **A burst of unparseable ingestion entries during a Redis outage no longer stalls the consumer.**
  The Redis ingestion consumer acknowledged "poison" entries (captured calls that fail to
  deserialize) with a blocking, synchronous `XACK` from inside its read loop. Because the Redis
  client is configured to fail slowly rather than on connect, each such ack could block for the full
  connect timeout (~5s) while Redis was unreachable — so a burst of poison entries during a Redis
  blip serialized those waits and wedged ingestion, and a thrown ack tore down the whole parallel
  processing round. Poison entries are now acknowledged with a single batched **asynchronous** ack
  per read, off the hot yield path and guarded so a transient Redis error just leaves them to be
  reclaimed and retried.

- **Retryable ingestion failures no longer leak memory or silently drop traces in single-process
  deployments.** The ingestion worker tracked retryable failures in a dictionary and left the
  message unacknowledged for the transport to redeliver. That is correct for Redis Streams, but the
  in-process channel used by single-process/kiosk runs never redelivers — so each retryable failure
  left an orphaned entry that grew unbounded under sustained failures, and the "retryable" message
  was lost rather than reprocessed. The worker now detects whether the transport actually redelivers:
  on the in-process channel it retries inline (a bounded number of times, then drops) and keeps no
  per-message state, while the Redis path is unchanged.

- **A large upstream response can no longer exhaust proxy memory on non-streaming calls.** The
  proxy's buffered (non-streaming) path read the entire upstream body into a string and then
  re-encoded it to bytes, leaving several full-size copies resident per in-flight request with no
  size cap on the response side — a very large or hostile upstream reply could push the ingestion
  proxy to OOM. The buffered path now streams the body straight through to the client in chunks
  (forwarded byte-for-byte, never truncated) and bounds only the copy it captures for ingestion to
  the same 16 MiB ceiling the streaming path already applied.

- **A hung model provider no longer stalls test and optimization runs indefinitely.** Internal model
  calls (optimizers, evaluators, the playground) were made with no request timeout and no retry
  policy, so a wedged or very slow upstream had no upper time bound and could pin a worker forever,
  stalling the serial A/B-validation / optimization queue. Each call now has a hard network-timeout
  ceiling independent of the caller, plus a bounded retry policy for transient failures.

- **Proxied calls are no longer dropped when the client disconnects after the upstream responds.**
  The proxy threaded the client request-aborted token all the way into the ingestion publish, so a
  client cancel/timeout/navigation *after* the upstream LLM call had already completed (a common
  pattern) cancelled the publish and silently lost the captured call. Capture is now decoupled from
  the client request lifetime — the publish runs with an independent token, and the streaming path
  publishes the accumulated transcript even on a mid-stream disconnect.

- **A malformed `Content-Type` header no longer crashes a proxied request.** The OpenAI-compatible
  ingestion proxy parsed the client-supplied `Content-Type` strictly, so a single bad value (e.g.
  `garbage;;`) threw and surfaced as an opaque `500`. The header is now parsed leniently and, when it
  cannot be parsed, forwarded upstream unchanged — the request proceeds normally instead of failing.

- **Captured calls with a non-UUID session id are no longer silently dropped.** When a client sent a
  session identifier that was not a GUID, Proxytrace hashed it to derive a stable conversation id but
  built the id from all 20 SHA-1 bytes, which threw and caused every such call to be discarded during
  ingestion. The hash is now truncated to 16 bytes, so calls carrying arbitrary session ids are
  captured and grouped into the same conversation as expected.

- **Saving a record no longer spuriously fails on the in-memory database.** After `UpdatedAt` became
  a database concurrency token, ordinary single-actor updates (for example a user changing their
  email-notification preference) could fail with a false "modified by another process" error on the
  in-memory storage backend used by the all-in-one/kiosk runtime, because the version stamp was being
  truncated to the precision PostgreSQL stores even when running in memory. The truncation now only
  applies on PostgreSQL, so updates succeed normally on both backends.

- **Concurrent edits to the same record no longer silently overwrite each other.** Optimistic
  concurrency was only checked in application code before a save, so two edits that started from the
  same version of a record could both pass the check and both write — the second silently discarding
  the first. The version stamp (`UpdatedAt`) is now enforced as a database concurrency token, so a
  genuine race is caught at write time and the losing edit fails cleanly instead of clobbering data.

- **The Playground agent picker only lists real agents.** The agent select-box no longer offers
  internal system agents (such as the built-in Tracey agent) — it shows only the user-facing agents
  you can actually run in the Playground.

- **Test suites can be deleted again.** Deleting a suite that had been run (or that had an
  optimization theory) failed with a foreign-key error: the run groups, runs, A/B-test proposals and
  theories that referenced it blocked the delete. Removing a suite now cascades to all of them — its
  run groups, runs, schedules, theories, and the proposals produced from those runs are removed with
  it — so a suite you no longer want always deletes cleanly.

- **Tracey reliably optimizes an agent you name.** Asked to "optimize the X agent", Tracey used to
  trip twice: she passed the typed agent *name* where an agent *id* was required (a guaranteed
  "not found"), and then listed *every* suite in the project with no way to tell which belonged to
  the agent (the suite index carried only id and name, not the agent). Now she resolves the name to
  an id first, and listing suites takes an optional agent filter with each row carrying its agent —
  so she finds the right agent and the right suite to validate the optimization against instead of
  guessing. Tracey can also now narrow **runs** and **proposals** to a single agent (previously only
  traces and theories could be filtered), so she pulls just that agent's evidence instead of the
  whole project's. If a name still slips into an id filter (suites, runs, proposals, or theories), it
  now degrades to a clean "not found" the assistant can recover from instead of a raw `400 Bad
  Request` error toast.
- **Tracey no longer goes silent after a page reload.** The in-app assistant kept the app's JWT in
  memory only, but its chat transport sent a placeholder `Authorization` header when that token was
  absent — which is the case after every browser reload (the session is restored from the cookie, not
  the token). The backend rejected the bogus bearer with a 401 instead of falling back to the valid
  session cookie, so every message sent after a reload produced no response. The transport now drops
  the placeholder header when there is no token, letting the same-origin session cookie authenticate
  the call exactly like every other request.
- **The dashboard no longer hangs for ~5 seconds when Redis is unreachable.** The dashboard reads the
  ingestion queue depth on every load; with the Redis transport configured but Redis down, that read
  blocked on the connection timeout (~5s) before quietly giving up, making the whole dashboard crawl
  on every refresh even though no other page was affected. The queue-depth read now bails out
  immediately when the connection is unavailable, so a Redis outage degrades only the depth figure
  (shown as 0) instead of stalling the page.
- **Test-run statistics no longer fail to project on a startup insert race.** When the statistics
  backfill (run at startup) and the live projector both computed stats for the same just-finished
  run, one lost the insert race and the recovery retry — sharing the same transactional context —
  replayed the orphaned insert, hitting the unique `TestRunId` constraint again and logging a
  `duplicate key value violates unique constraint "IX_TestRunStatsEntity_TestRunId"` error while
  leaving that run's stats unprojected. The failed insert is now discarded before the retry, so it
  correctly falls back to an update.
- **Setup no longer fails when a provider lists a zero-cost model.** Initial setup refreshes a
  provider's model catalog and prices; a discovered model whose price was non-positive (e.g. a free
  model listed at `0`) or inverted violated the model-endpoint price invariants and aborted the whole
  setup with a 500. Such models are now skipped (logged) and the remaining priced models import
  normally.
- **The dashboard loads much faster in kiosk/demo mode.** The dashboard's statistics aggregation
  fans ~11 independent queries out concurrently, but the in-memory store used by kiosk mode runs
  queries synchronously, which silently collapsed that fan-out into sequential execution — making
  the dashboard (the landing page) take ~0.5s warm / ~2s cold while every other page stayed
  instant. The queries now run concurrently again, cutting the dashboard load to roughly a single
  query's time.
- **The proxy no longer leaks database connections while resolving API keys.** The cached API-key
  resolver was a singleton holding repositories bound to the root scope, so the short-lived database
  context created on each cache-miss lookup was never disposed until shutdown. The resolver is now
  per-request (the cache it shares stays process-wide), so those contexts are released promptly.
- **Deleting a model provider no longer destroys its traces and test runs.** A provider delete used
  to cascade through its endpoints and silently hard-delete every `AgentCall`/`TestRun` that
  referenced them. Providers now archive (soft-delete) like endpoints do, so the history is preserved
  while the provider disappears from the UI.
- **Captured traces are no longer dropped on a transient database hiccup.** The ingestion worker
  acknowledged every message even when persisting it failed; a brief DB outage or a write race could
  lose a trace permanently. Failed writes are now retried (with an attempt cap) and only acknowledged
  once they succeed. A normal completion whose text happens to contain `data: ` is also no longer
  mis-parsed as a streamed response.
- **One failing test case no longer fails the whole test run.** A single flaky/erroring case (e.g. a
  timed-out LLM call) aborted the entire run and every sibling case; failures are now isolated so the
  rest of the run completes. Optimization A/B validation also ignores partial runs so a proposal is
  never spawned from incomplete evidence.
- **Real-time (SSE) streams no longer leak server resources.** Streams for already-finished runs and
  groups left a subscription registered forever; subscriptions are now always cleaned up, capped, and
  kept alive with a heartbeat so dead connections are detected.
- **Tracey's "remove test case" tool no longer renders a broken result card.** Removing a case
  from a suite via the assistant dropped the updated suite returned by the backend (the API call
  was typed as returning nothing), so the follow-up card showed empty. The updated suite is now
  carried through and rendered correctly.
- **An unevaluated test result is no longer counted as a pass.** A test result's `Passed` flag used a
  vacuous "all evaluations passed" check that was true when there were *no* evaluations, disagreeing
  with the canonical pass rule used by the optimization loop; both now agree — a result passes only
  with at least one evaluation, all passing.
- **The exact-match evaluator now fails when the response has a different number of content parts.**
  It compared parts pairwise with `Zip`, which silently truncated to the shorter sequence, so a
  partial or padded response could score as an acceptable match. A differing part count is now a
  mismatch.
- **A cancelled or failed test run can no longer be revived by a late result.** A result arriving
  in-flight after a run reached a terminal state transitioned it back to running/completed and
  overwrote its completion time; such late results are now ignored.
- **Valid configurations are no longer rejected by domain validation.** A numeric-match evaluator with
  a tolerance of `0` (exact numeric match) and prompt-template variables with a single letter (e.g.
  `{{x}}`) were incorrectly rejected; both are now accepted, while a purely numeric variable name (no
  letters) is still rejected.
- **Domain hardening.** Invalid tool JSON schemas are now surfaced as validation errors instead of
  throwing out of validation; referenced entities/values (A/B run, proposed tools, evaluator project,
  schedule endpoints) are validated consistently; entity identity and value-object hashing were
  corrected (identity by id; collection-backed value objects now hash by content); an agentic
  evaluator no longer records run cancellation as an evaluation error; and token-usage subtraction is
  clamped at zero so it can never underflow.

- Listing models through the proxy against an **Azure OpenAI** upstream
  (`GET /openai/v1/models`, e.g. `client.models.list()`) returned an empty list, because Azure
  exposes usable models as *deployments* rather than through an OpenAI-style `/models` route.
  The proxy now detects an Azure upstream and returns its deployments as the model list.

- The proxy forwarded bodyless requests (e.g. `GET /models`, `DELETE`) with an empty body and
  `Content-Length: 0`, which some strict OpenAI-compatible upstreams reject. It now attaches a
  request body only when one is present.

- The project segment in the proxy URL (`/{project}/openai/v1/…`) is now matched
  case-insensitively, so a base URL like `…/Development/openai/v1` resolves the **Development**
  project instead of returning **401 Unauthorized**.

## [1.0.3] - 2026-06-12

### Added

- "How to wire the proxy?" documentation link on the Traces page empty state.

### Changed

- The **Tracey AI** assistant is now an Enterprise feature. On the Free tier the sidebar
  entry is locked and the Tracey page shows an upgrade prompt; the Tracey API endpoints
  respond with HTTP 402.

### Fixed

- Upgrade/pricing links (upgrade placeholder, upgrade dialog, docs) pointed at
  `proxytrace.dev/pricing` instead of the correct `proxytrace.dev/#pricing` anchor.

- The ingestion endpoint shown in the setup wizard and on the API-keys page pointed at the
  web UI port instead of the ingestion proxy (e.g. `:5101` instead of `:5102` in the Docker
  deployment), where OpenAI calls fail with `405 Not Allowed`. The backend now advertises
  the proxy's public URL (`Proxy:PublicBaseUrl`; in Docker `PROXYTRACE_PROXY_PUBLIC_URL`,
  default `http://localhost:5102`) and the UI displays that.

## [1.0.2] - 2026-06-12

### Changed

- **Faster multi-architecture release builds** — the release pipeline now builds the
  `linux/amd64` and `linux/arm64` container images more efficiently, shortening release
  times. No change to the published images or runtime behavior.

## [1.0.1] - 2026-06-12

### Fixed

- **ARM64 container images** — release images are now published for both `linux/amd64`
  and `linux/arm64`, so `docker compose up` works on Apple Silicon (and other arm64
  hosts) without the `no matching manifest for linux/arm64/v8` error.

## [1.0.0] - 2026-06-12

First stable release. Consolidates everything from the `1.0.0-rc.1` and
`1.0.0-rc.2` prereleases.

### Added

- **Brand mark** — new "Scope" logo and app icons: a gold trace pulse over an
  oscilloscope graticule with a teal live cursor.
- **Trace capture** — OpenAI-compatible ingestion proxy that records every LLM
  interaction (requests, responses, tool calls, token usage, cost, latency) with
  zero agent code changes.
- **Projects, agents & API keys** — organize captured traffic per project and agent,
  authenticated with per-agent proxy API keys.
- **First-run setup wizard** — guided onboarding (provider → model → project) ending in
  per-language quick-start examples (Python, TypeScript, C#, curl); clients keep their
  upstream provider API key and swap only the base URL.
- **Dashboard & statistics** — live telemetry, token/cost breakdowns, latency and
  pass-rate trends per agent, model, and project.
- **Test suites & evaluators** — curate captured traces into benchmark suites and
  judge them with configurable evaluators (including agentic and custom evaluators).
- **Test runs** — execute suites against any model endpoint with a live, streaming
  results view and per-evaluator progress.
- **Optimization loop** — data-driven optimization theories, A/B validation runs,
  and reviewable improvement proposals.
- **Tracey** — built-in AI assistant with access to your traces and the manual.
- **Authentication** — local accounts or OIDC single sign-on (Enterprise).
- **Licensing** — Free tier built in; Enterprise features unlocked with a license key.
- **Self-hosted deployment** — versioned container images on GHCR with a downloadable
  Docker Compose artifact; database migrations apply automatically on upgrade.
- **Update notifications** — daily check against the public release feed; admins see a
  dismissible in-app notice when a newer version is available (opt-out via `Updates:Enabled`).
- **User & operator manual** — bundled at `/docs`, searchable, with admin guides.
- **License management in the UI** — a license key can now be activated without a restart:
  the setup wizard's Welcome step offers a *"Have a license key?"* field, and a new
  **Settings → License** page lets admins validate a key (dry run showing tier, customer,
  and expiry), activate it, force a license-server re-check, or remove it. A key activated
  in the UI is stored in the database and takes precedence over the `PROXYTRACE_LICENSE`
  environment variable.
- **Zero-configuration Docker install** — `docker compose up -d` now works without any
  `.env` file: the internal-only Postgres password defaults, the session signing key is
  generated on first start and persisted in a new `appdata` volume (sessions survive
  container recreation), and without a license Proxytrace runs the Free tier. All `.env`
  settings remain available as overrides.
- **Adoption tracking for promoted proposals** — a promoted proposal now waits in
  *"Promoted — awaiting adoption"* and flips to **Adopted** automatically when the exact
  change shows up in the agent's live traffic (new prompt/tool version, or calls arriving on
  the proposed model endpoint); auto-adoptions link the detected agent version ("Adopted in
  v{N}"). A **Mark adopted** button covers tweaked or undetectable adoptions.
- **Handoff package on promote** — copy buttons for the proposed prompt / tools JSON / model
  name, a downloadable markdown "apply this change" doc with the A/B evidence, and a
  machine-readable artifact endpoint (`GET /api/proposals/{id}/artifact`) for scripted
  workflows.
- **Generate JSON schema from an example** — the JSON Schema Match evaluator form can now
  infer a draft 2020-12 schema from a pasted example JSON value (every observed key becomes
  required; loosen by hand where needed).

### Changed

- **An invalid license no longer prevents startup** — a malformed/expired/rejected
  `PROXYTRACE_LICENSE` previously crashed the container; Proxytrace now boots with
  Free-tier entitlements, shows a red "license invalid" banner with the rejection reason,
  and the key can be fixed under Settings → License without a restart.
- **The license offline-grace cache is now persisted across container recreations**
  (in the new `appdata` volume), so the offline grace window is anchored correctly.
- **Promote is honest now** — Proxytrace is an observing proxy and cannot change your
  agent's code; the UI no longer claims a promoted change "has been applied to the agent".
  Proposal status changes are validated server-side (illegal transitions return 409).
- **Theory validation is now statistically gated** — an A/B pass-rate improvement only
  produces an optimization proposal when it is significant (two-proportion p-value ≤ 0.05);
  lucky runs on small suites no longer spawn proposals.
- **Model-switch discovery compares against the current model** — the cost/latency margin,
  the no-regression check on the other metric, and the pass-rate gate are all measured
  against the model the agent would actually switch away from (previously parts were
  measured against the runner-up, which could propose switches that regressed the agent).
- **Playground conversation uses the shared message bubbles** — turns in the agent
  playground now render with the same collapsible bubbles as the trace detail drawer
  (role accents, copy button, character count, raw/JSON/markdown views) while keeping
  edit-in-place, delete, and drag-to-reorder.
- **The downloadable install artifact now ships under a constant name** (`proxytrace.zip`)
  instead of a version-stamped filename, so install instructions and automation no longer
  need updating per release.

### Fixed

- **Theory backlog survives restarts** — theories still queued or validating when the
  server stops are re-queued on startup instead of being stranded (previously they also
  permanently consumed the per-project validation quota).
- **Upstream provider API keys are no longer returned to non-admin users** — the by-id
  provider endpoint (used by Tracey's tools) now redacts the upstream key; the setup
  connection-test and model-listing endpoints now require the Admin role.
- Faster evaluator-history queries on large test-result tables (new index).
- **Faster initial load** — the Tracey AI chat stack now loads in its own lazy chunk,
  halving the main JavaScript bundle (gzip 477 kB → 251 kB) and speeding up first paint
  on every page. Tracey's bundled manual-search index is also fetched on demand instead
  of shipping with her tools.
- The sidebar showed a hardcoded "v0.1 · alpha" label; it now shows the actual
  installed release version.
- **Settings → Danger zone:** "Delete all non-model data" left the dashboard and other
  views showing stale pre-wipe numbers until a manual refresh.
- Signing out now lands on a real `/login` URL instead of an undefined route.

### Security

- **Local-mode sessions now use an httpOnly cookie.** The session token is no longer
  persisted in browser `localStorage` (where any injected script could read it); the
  backend issues it as an `HttpOnly`/`SameSite=Strict` cookie and clears it on the new
  logout endpoint. API clients keep using the `Authorization` header unchanged.

## [1.0.0-rc.2] - 2026-06-12

### Added

- **License management in the UI** — a license key can now be activated without a restart:
  the setup wizard's Welcome step offers a *"Have a license key?"* field, and a new
  **Settings → License** page lets admins validate a key (dry run showing tier, customer,
  and expiry), activate it, force a license-server re-check, or remove it. A key activated
  in the UI is stored in the database and takes precedence over the `PROXYTRACE_LICENSE`
  environment variable.
- **Zero-configuration Docker install** — `docker compose up -d` now works without any
  `.env` file: the internal-only Postgres password defaults, the session signing key is
  generated on first start and persisted in a new `appdata` volume (sessions survive
  container recreation), and without a license Proxytrace runs the Free tier. All `.env`
  settings remain available as overrides.
- **Adoption tracking for promoted proposals** — a promoted proposal now waits in
  *"Promoted — awaiting adoption"* and flips to **Adopted** automatically when the exact
  change shows up in the agent's live traffic (new prompt/tool version, or calls arriving on
  the proposed model endpoint); auto-adoptions link the detected agent version ("Adopted in
  v{N}"). A **Mark adopted** button covers tweaked or undetectable adoptions.
- **Handoff package on promote** — copy buttons for the proposed prompt / tools JSON / model
  name, a downloadable markdown "apply this change" doc with the A/B evidence, and a
  machine-readable artifact endpoint (`GET /api/proposals/{id}/artifact`) for scripted
  workflows.
- **Generate JSON schema from an example** — the JSON Schema Match evaluator form can now
  infer a draft 2020-12 schema from a pasted example JSON value (every observed key becomes
  required; loosen by hand where needed).

### Changed

- **An invalid license no longer prevents startup** — a malformed/expired/rejected
  `PROXYTRACE_LICENSE` previously crashed the container; Proxytrace now boots with
  Free-tier entitlements, shows a red "license invalid" banner with the rejection reason,
  and the key can be fixed under Settings → License without a restart.
- **The license offline-grace cache is now persisted across container recreations**
  (in the new `appdata` volume), so the offline grace window is anchored correctly.
- **Promote is honest now** — Proxytrace is an observing proxy and cannot change your
  agent's code; the UI no longer claims a promoted change "has been applied to the agent".
  Proposal status changes are validated server-side (illegal transitions return 409).
- **Theory validation is now statistically gated** — an A/B pass-rate improvement only
  produces an optimization proposal when it is significant (two-proportion p-value ≤ 0.05);
  lucky runs on small suites no longer spawn proposals.
- **Model-switch discovery compares against the current model** — the cost/latency margin,
  the no-regression check on the other metric, and the pass-rate gate are all measured
  against the model the agent would actually switch away from (previously parts were
  measured against the runner-up, which could propose switches that regressed the agent).
- **Playground conversation uses the shared message bubbles** — turns in the agent
  playground now render with the same collapsible bubbles as the trace detail drawer
  (role accents, copy button, character count, raw/JSON/markdown views) while keeping
  edit-in-place, delete, and drag-to-reorder.

### Fixed

- **Theory backlog survives restarts** — theories still queued or validating when the
  server stops are re-queued on startup instead of being stranded (previously they also
  permanently consumed the per-project validation quota).
- **Upstream provider API keys are no longer returned to non-admin users** — the by-id
  provider endpoint (used by Tracey's tools) now redacts the upstream key; the setup
  connection-test and model-listing endpoints now require the Admin role.
- Faster evaluator-history queries on large test-result tables (new index).
- **Faster initial load** — the Tracey AI chat stack now loads in its own lazy chunk,
  halving the main JavaScript bundle (gzip 477 kB → 251 kB) and speeding up first paint
  on every page. Tracey's bundled manual-search index is also fetched on demand instead
  of shipping with her tools.

### Security

- **Local-mode sessions now use an httpOnly cookie.** The session token is no longer
  persisted in browser `localStorage` (where any injected script could read it); the
  backend issues it as an `HttpOnly`/`SameSite=Strict` cookie and clears it on the new
  logout endpoint. API clients keep using the `Authorization` header unchanged.

- The sidebar showed a hardcoded "v0.1 · alpha" label; it now shows the actual
  installed release version.
- **Settings → Danger zone:** "Delete all non-model data" left the dashboard and other
  views showing stale pre-wipe numbers until a manual refresh.
- Signing out now lands on a real `/login` URL instead of an undefined route.

## [1.0.0-rc.1] - 2026-06-11

### Added

- **Brand mark** — new "Scope" logo and app icons: a gold trace pulse over an
  oscilloscope graticule with a teal live cursor.
- **Trace capture** — OpenAI-compatible ingestion proxy that records every LLM
  interaction (requests, responses, tool calls, token usage, cost, latency) with
  zero agent code changes.
- **Projects, agents & API keys** — organize captured traffic per project and agent,
  authenticated with per-agent proxy API keys.
- **First-run setup wizard** — guided onboarding (provider → model → project) ending in
  per-language quick-start examples (Python, TypeScript, C#, curl); clients keep their
  upstream provider API key and swap only the base URL.
- **Dashboard & statistics** — live telemetry, token/cost breakdowns, latency and
  pass-rate trends per agent, model, and project.
- **Test suites & evaluators** — curate captured traces into benchmark suites and
  judge them with configurable evaluators (including agentic and custom evaluators).
- **Test runs** — execute suites against any model endpoint with a live, streaming
  results view and per-evaluator progress.
- **Optimization loop** — data-driven optimization theories, A/B validation runs,
  and reviewable improvement proposals.
- **Tracey** — built-in AI assistant with access to your traces and the manual.
- **Authentication** — local accounts or OIDC single sign-on (Enterprise).
- **Licensing** — Free tier built in; Enterprise features unlocked with a license key.
- **Self-hosted deployment** — versioned container images on GHCR with a downloadable
  Docker Compose artifact; database migrations apply automatically on upgrade.
- **Update notifications** — daily check against the public release feed; admins see a
  dismissible in-app notice when a newer version is available (opt-out via `Updates:Enabled`).
- **User & operator manual** — bundled at `/docs`, searchable, with admin guides.
