# Server-Sent Events (SSE)

Real-time updates flow from the API to the SPA over **Server-Sent Events**. The backend publishes
domain events onto in-process channels via the broadcasters in
`Proxytrace.Application/Streaming/`; SSE controller actions in `Proxytrace.Api/Controllers/`
subscribe a client to a channel and write each event as an SSE frame
(`event: <name>\ndata: <json>\n\n`). The SPA consumes them through `useEventStream` and the typed
hooks in `frontend/src/api/event-stream.ts`.

> Keep this file, the broadcaster records, the controller event-name switches, and the frontend
> hooks **in lockstep** — the event `name`, the payload shape, and the client `addEventListener`
> name must all match or the update silently never arrives.

## Event catalog

| Endpoint (GET) | Scope | Event name(s) | Payload record | Use case |
|---|---|---|---|---|
| `/api/agent-calls/stream` | global broadcaster, server-filtered to the caller's member projects | `trace-created` | `TraceCreatedEvent` | New trace lands → live Traces list + dashboard counters |
| `/api/agents/{id}/proposals/stream` | one agent (membership-checked) | `proposal-created`, `proposal-status-changed` | `ProposalEvent` subtypes | A validated theory spawned a Draft proposal; a proposal was promoted / dismissed / adopted (incl. auto-detected adoption) → Proposals board |
| `/api/agents/{id}/theories/stream` | one agent (membership-checked) | `theory-changed` | `TheoryStatusChangedEvent` | Theory moves through Proposed→Validating→Validated/Invalidated → board columns |
| `/api/test-runs/{id}/stream` | one run (membership-checked) | `test-case-started`, `inference-done`, `evaluation-arrived`, `test-result-arrived`, `run-complete`* | `TestRunEvent` subtypes | Live single-run progress (per-case, per-evaluator) |
| `/api/test-run-groups/{id}/stream` | one group (all its runs; membership-checked) | the five run events above + `group-run-complete`* | `TestRunEvent` subtypes | Live multi-endpoint comparison run |
| `/api/notifications/stream?projectId=` | per project (global broadcaster, server-filtered per connection) | `notification-created`, `notification-status-changed` | `NotificationEvent` subtypes | Anomaly detection raised an alert / a notification was read or dismissed → the top-bar notifications inbox (`NotificationsMenu`, mounted in `Shell`). The broadcaster is global, but the controller writes a frame only when `evt.ProjectId == projectId` or the event is global (null project), so another project's notification content never reaches the connection. |
| `/api/anomalies/stream?projectId=` | global broadcaster (`ICustomAnomalyBroadcaster`), server-filtered per frame to the caller's member projects | `anomaly-flagged` | `AnomalyFlaggedEvent` (carries `Blocked: bool` — `true` when the proxy rejected the call in real time via a blocking detector, `false` for an LLM-review verdict) | A user-defined anomaly detector's LLM review flagged a call, or the proxy blocked one before it reached the provider → live Anomaly dashboard (`/anomalies`: recent-flagged list + timeline). Filtered per frame against the caller's accessible project ids (`AnomalyStreamController`; admins receive all). |

`*` = **terminal** event. The client closes the `EventSource` on the terminal event and the run/group
views are **pure-SSE (no polling)**; on terminal they invalidate the relevant TanStack queries to
heal any dropped events. Do **not** reintroduce `refetchInterval` on these views.

**Cross-tenant scoping (every stream).** Streams are tenant-scoped via `IProjectAccessGuard`
(`Proxytrace.Api/Auth/IProjectAccessGuard.cs`; admins bypass). Per-resource streams
(`…/agents/{id}/…`, `…/test-runs/{id}/…`, `…/test-run-groups/{id}/…`) resolve the resource's owning
project and return `404` before subscribing if the caller isn't a member. The two global-broadcaster
streams filter each frame instead: `/api/agent-calls/stream` by `evt.ProjectId` against the caller's
member projects, and `/api/notifications/stream` likewise (global null-project notifications are
admin-only). A new stream over a global broadcaster MUST carry the owning `ProjectId` on its event
payload so it can be filtered the same way.

The runs list is **light** (`TestRunGroupListItemDto`, no per-case results), so `useRunGroupStream`
patches the selected group's **detail** query (`QUERY_KEYS.testRunGroup(id)`) — the fat group fetched
when a run is opened — via `setQueryData`, **not** the list cache. The patch no-ops until the detail
query resolves (an SSE event can arrive before the detail GET). On the terminal `group-run-complete`
it first patches the cached group's own `status`/`completedAt` straight from the event (so the header
badge flips immediately, not after the refetch round-trip), then invalidates the whole
`test-run-groups` namespace (`testRunGroupsRoot`), healing both the detail matrix and the left-rail
list (whose pass rates are static mid-run by design). It also invalidates `testRunSchedulesRoot` (a
schedule's recent-runs change) and `testSuitesRoot` (the owning suite's aggregates and windowed
run-stats change once a run finishes). See `frontend/src/features/runs/`.

**Subscription lifetime is keyed off the *group* status, not its runs.** `GroupDetail` subscribes
while `isActive(group.status)` and unsubscribes only once the group itself settles — *not* once every
run is non-active. Keying off the runs tore the stream down on the last `run-complete`, which can land
before `group-run-complete`, so `handleDone` (the terminal flip + invalidate) never ran and the header
stuck on "Running" until a manual refresh. The per-run `run-complete` events still patch each run's
status (the matrix/leaderboard spinners), but only the group-level terminal event flips the group.

The backend marks an individual **run** `Running` only once its first case finishes and never streams
that `Pending → Running` transition, so a freshly-opened run would otherwise read `Pending` (and show
a "—" duration) for its whole life. To fix that, `useRunGroupStream` flips a cached run out of
`Pending` on its first `test-case-started` event (`markRunRunning`, idempotent), so the run reads as
running — with a live ticking duration — the moment a case starts, not when the first one finishes.

**Sampling is read-side only.** Running each endpoint N times (a cohort of sample runs) adds **no new
events and no payload changes** — events stay keyed by `runId`/`testCaseId`, one per sample run, and the
group stream fires `group-run-complete` once when all N×M runs finish. The cohort averaging (per-case
pass fraction, per-endpoint columns) is a pure client-side derive over the per-run cache
(`frontend/src/features/runs/cohorts.ts`), so live updates keep working unchanged as each sample run
patches independently.

The Playground (`/api/playground/...`) and Tracey chat also stream over `text/event-stream`, but they
stream raw model tokens for one request rather than domain broadcaster events — not part of this catalog.

## Payload shapes

Records live in `Proxytrace.Application/Streaming/`. Serialized with `ApiJsonOptions.Sse`
(camelCase). The client wraps each frame as `{ type: <event-name>, ...payload }`; TypeScript mirrors
in `frontend/src/api/models.ts`.

- **`TraceCreatedEvent`** — `Id, AgentId, ProjectId, AgentName, Model, Provider, CreatedAt, ConversationId?, SessionId?` (`ProjectId` lets the global `/api/agent-calls/stream` filter each frame to the caller's member projects — see below)
- **Proposal events** (`ProposalEvent` carries `Id`, `AgentId`):
  - `ProposalCreatedEvent` — `+ Kind, Priority, Rationale, CreatedAt`
  - `ProposalStatusChangedEvent` — `+ Kind, Status, AdoptedAt?, AdoptedAgentVersionId?, AdoptedAgentVersionNumber?, AdoptedManually?, UpdatedAt` (published by `ProposalsController.UpdateStatus` and `ProposalAdoptionService`)
- **`TheoryStatusChangedEvent`** — `Id, AgentId, Kind, Status, Source, Priority, Rationale, ResultingProposalId?, UpdatedAt`
- **Notification events** (`NotificationEvent` carries `Id`, `ProjectId?`):
  - `NotificationCreatedEvent` — `+ Kind, Severity, Title, Message, Status, TargetKind?, TargetId?, CreatedAt` (published by `DashboardNotificationChannel`; de-duplication handled upstream by `NotificationService`)
  - `NotificationStatusChangedEvent` — `+ Status, UpdatedAt` (published by `NotificationsController` on mark-read / dismiss)
- **`AnomalyFlaggedEvent`** — `AgentCallId, AgentId, ProjectId, DetectorId, DetectorName` (published by the custom-anomaly review pipeline on an anomalous verdict; `ProjectId` lets the global `/api/anomalies/stream` filter each frame to the caller's member projects). Consumed by `useAnomalyStream` (`frontend/src/api/event-stream.ts`) on the Anomaly dashboard.
- **Run events** (`TestRunEvent` carries `RunId`, `GroupId`):
  - `TestCaseStartedEvent` — `+ TestCaseId`
  - `InferenceDoneEvent` — `+ TestCaseId`
  - `EvaluationArrivedEvent` — `+ TestCaseId, Evaluation` (`EvaluationEventData`: evaluator id/kind/name, score, reasoning, error)
  - `TestResultArrivedEvent` — `+ TestCaseId, OverallScore, Evaluations[], DurationMs, CostEur?, TokensIn?, TokensOut?, CachedTokensIn?` (the per-case usage rides along so the live run cards can sum a running run's cost/tokens as each case lands — `CalculateCost` is linear, so the client sum equals the run-level `TestRunTotals`; `null` when the provider reported no usage)
  - `RunCompleteEvent` — `+ Status, CompletedAt?`
  - `GroupRunCompleteEvent` — `GroupId, GroupStatus, GroupCompletedAt?`; **`RunId` is `Guid.Empty`** for this group-level event.

## Authentication

`EventSource` cannot set an `Authorization` header, so the credential rides in the query string.
The client prefers a **short-lived single-use stream ticket** from `GET /api/auth/stream-ticket`
(passed as `stream_ticket`, redeemed once by the backend) and falls back to the session JWT
(`access_token`) only if the ticket endpoint is unreachable. See `resolveStreamCredential` in
`event-stream.ts`.

**Reconnection.** Because the ticket is single-use and baked into the URL, the browser's native
`EventSource` reconnect would replay a *consumed* ticket and 401 — silently killing the stream after
the first drop (backend restart, proxy blip, laptop sleep) so the UI stops updating until a full page
reload. `subscribeShared` therefore owns reconnection: on `onerror` it closes the source and reopens
with a **freshly-minted ticket** (re-running `withAuth`) after an exponential backoff (1s→30s cap,
reset on any delivered frame). This is auth-mode-agnostic — it does not rely on the cookie fallback,
which OIDC mode lacks.

SSE responses set `Content-Type: text/event-stream`, `Cache-Control: no-cache`, and
`X-Accel-Buffering: no` (disables proxy buffering so frames flush immediately).

## Liveness & subscriber limits

**Heartbeat (every stream).** A half-open TCP connection never raises `RequestAborted`, so a quiet
stream would otherwise leak its subscriber slot forever. Every controller action therefore drains its
channel through `SseWriter.ReadWithHeartbeatAsync` (`Proxytrace.Api/SseWriter.cs`): after 20s of
inactivity it yields a `null` tick and the action writes a comment frame (`:\n\n`). On a dead socket
that write fails, the action unwinds, `RequestAborted` fires, and the broadcaster's cancellation
registration removes the subscription. Mirror this exactly when adding a stream — emit the heartbeat
on the `null` tick, real events otherwise.

**Subscriber cap (every broadcaster).** Each broadcaster bounds total live subscriptions at
`MaxSubscribers = 2000` so an authenticated client can't exhaust memory/sockets by opening unbounded
streams; past the cap `Subscribe` returns an immediately-completed reader (the SSE request closes
cleanly). `Subscribe` also rejects a non-cancellable token (`!cancellationToken.CanBeCanceled`) so a
subscription can never be registered without a disconnect path. This applies to the per-resource
broadcasters and the two global ones (`TraceBroadcaster`, `NotificationBroadcaster`) every client
subscribes to.

## Connection sharing & the HTTP/1.1 budget

The SPA is served over **HTTP/1.1** (nginx `listen 80`), where browsers cap concurrency at
**~6 connections per host**. An `EventSource` is a *long-lived* connection that never returns to
the pool, so every open stream permanently spends one of those six slots. Several hooks legitimately
want the same stream at once — the agent-detail page alone mounts `/api/agent-calls/stream` **four**
times (`useAgentStats` + the recent-traces, outliers, and distributions widgets). Opening one
`EventSource` per subscriber would burn four slots on identical streams (plus the proposal and
notification streams = six), leaving **zero** for ordinary fetches: a delete/save fired while the
detail is open never gets a socket, so the request silently never leaves the browser — no response,
no server-side effect (this was the agent-delete bug).

So `useEventStream` **multiplexes**: it keeps **one shared `EventSource` per `(url, events)`** and
fans each frame out to every subscriber, ref-counted — the first subscriber opens it, the last
closes it. The four trace-stream hooks therefore share a single connection. Terminal streams (the
single-run / group views that self-close on a `completeEvent`) stay per-instance — each has a unique
URL and its own close-on-complete lifecycle, so sharing buys nothing.

Practical rules:

- **Always consume a stream through the typed hooks** (`useTraceStream`, `useProposalStream`, …) /
  `useEventStream` — never `new EventSource(...)` directly, or you lose the sharing and re-introduce
  the connection-exhaustion bug.
- **Subscribing to the same stream from N components is cheap** — they collapse to one connection.
- **Keep a single page's distinct long-lived streams comfortably under six.** Adding a fifth or
  sixth *different* stream type to one view is the warning sign; reuse an existing one or reconsider.

## Adding a new SSE stream

1. Add an event `record` + broadcaster interface in `Proxytrace.Application/Streaming/` (mirror
   `IProposalBroadcaster`): a `Subscribe(...)` returning a `ChannelReader<T>` and a `Publish(...)`.
2. Publish from the producing service (e.g. the ingestor, `TestRunnerService`, the validation
   pipeline).
3. Add a controller action that sets the SSE headers, subscribes, and writes
   `event: <name>\ndata: <json>\n\n` per item (serialize with `ApiJsonOptions.Sse`).
4. Add a typed hook in `frontend/src/api/event-stream.ts` and the payload type in `models.ts`.
5. Update this file and the user manual if the change is user-visible.
