# Architecture

Strict layered dependency flow — each layer may only depend on layers below it:

```
Proxytrace.Api  →  Proxytrace.Application  →  Proxytrace.Domain  →  Proxytrace.Common
            →  Proxytrace.Infrastructure  →  Proxytrace.Domain + Proxytrace.Serialization  →  Proxytrace.Common
            →  Proxytrace.Storage  →  Proxytrace.Domain
```

> **`Proxytrace.Storage` references only `Domain`** (+ `Serialization`/`Common` transitively) — it does
> **not** reference `Application`. The secondary-port **interfaces** that `Storage` implements live in
> `Domain` alongside the pure DTOs they expose: `ISecretProtector`/`ISecretHasher` (`Domain.Security`),
> the statistics/test-run readers + writer (`Domain.Statistics`, with `TestRunStats` and the result
> records), the outlier readers + `OutlierSettings` (`Domain.Outliers`), `IEmailSettingsStore` +
> `EmailSettings` (`Domain.Notifications`), the stored-license store (`Domain.Licensing`),
> `IDatabaseInitializer` (`Domain.Demo`), `ITestDataReset` (`Domain.TestSupport`) and the audit-emit
> seam `Audit`/`LogAudit`/`AuditState` (`Domain.AuditLog`). The **implementations** (`Internal/*`),
> hosted services, and the audit *capture* pipeline stay in `Application` (issue #270).
>
> `Proxytrace.Infrastructure` depends only on `Domain` + `Serialization` (it has **no** reference to
> `Application`): the kiosk option records it needs (`KioskOptions`, `KioskEndpointOptions`) live in
> `Proxytrace.Domain.Kiosk`, and it now also hosts the at-rest secret seam's Data Protection-backed
> **implementation** + DI module (`Proxytrace.Infrastructure.Security.SecretProtectionModule`) — the
> lowest layer both the API host and the lean proxy can reach without `Application`. **Consequence:** the
> standalone `Proxytrace.Proxy.Api` (and the shared `Proxytrace.Proxy` pipeline lib) reference `Storage`
> (+ `Infrastructure`, `Messaging`, `Domain`) and do **not** load the `Application` assembly at all,
> directly or transitively.

- **Proxytrace.Api** — ASP.NET Core controllers, DTOs, the OpenAI-compatible proxy endpoint, composition root (`Proxytrace.Api.Module`)
- **Proxytrace.Application** — Use-case orchestration: ingestion (`OpenAiCallParser`, `AgentCallIngestor`), test running (`TestRunnerService`), optimization, test-case synthesis (`TestCase/ITestCaseSynthesisService` — proposes cases from a captured conversation), SSE broadcasters (`TraceBroadcaster`, `TestResultBroadcaster`, `ProposalBroadcaster`), demo data seeding (`IDatabaseInitializer`)
- **Proxytrace.Domain** — Business entities, interfaces, value objects, repository contracts. Pure C#, no I/O.
- **Proxytrace.Infrastructure** — External service integration. `ModelClient` wraps `Microsoft.Extensions.AI` + the OpenAI SDK to invoke LLMs.
- **Proxytrace.Serialization** — JSON serializers and output formats (`ISerializer`, `IOutputFormat`, `ObjectToInferredTypesConverter`).
- **Proxytrace.Storage** — EF Core entities, configurations, mappers, migrations. Provider auto-detected (SQLite / PostgreSQL / SQL Server).
- **Proxytrace.Common** — Shared utilities: validation helpers, async/type extensions, DI extensions, randomness.
- **Proxytrace.Proxy** — **Shared pipeline library** (classlib) for the OpenAI-compatible proxy route. Contains the MVC controller (`OpenAiProxyController`), the API-key resolver (`IApiKeyResolver`/`ApiKeyResolver` — **deliberately uncached**, straight from storage on every request so provider-key rotation/revocation takes effect on the next request and the proxy fails closed when the database is unreachable rather than serving stale credentials — #407), the blocking-rule provider (`IBlockingRuleProvider`/`CachedBlockingRuleProvider`), and the request blocker (`IRequestBlocker`/`RequestBlocker`). Its `Module` registers only the pipeline types and their supporting services (IMemoryCache, HTTP clients). References Domain + Infrastructure + Messaging + Storage **+ Licensing** (it does **not** reference Api **or Application** — directly or transitively). The host composition root is responsible for wiring storage, messaging, infrastructure, and licensing; the host also adds the library as an MVC application part so its controller is discovered (the controller assembly is **not** auto-discovered, so a host that does not add the part has no proxy route). This design allows both the standalone `Proxytrace.Proxy.Api` host and, in kiosk mode, `Proxytrace.Api` to mount the proxy route. **In-process kiosk mount:** `Proxytrace.Api` registers `Proxytrace.Proxy.Module` and adds the controller's application part **only when `Kiosk:Enabled` AND a live `Kiosk:Endpoint` is configured** — in production or kiosk-without-endpoint the pipeline services are absent and the `openai/v1/{**path}` routes never resolve. In that single-process kiosk, the controller publishes captured calls to the same in-process `IIngestionStream` (`Messaging__Provider=InProcess`, a shared singleton) that the app's `AgentCallIngestionWorker` consumes — no Redis, no separate container. The controller's kiosk guard refuses (503) only when kiosk has **no** live endpoint; with a live endpoint it serves so a sample client's OpenAI SDK `baseURL` can point at the kiosk API. Kiosk seeding also mints a fixed, config-known demo ingestion key (`Kiosk:DemoApiKey`, default `pk-kiosk-demo`) for the "Showcase Project", attached to the live provider and stored hashed like any operator-minted key (`DemoApiKeySeedScenario`). **Upstream response handling:** both branches send with `HttpCompletionOption.ResponseHeadersRead` and copy the body through in bounded chunks (never `ReadAsStringAsync`), forwarding every byte untruncated while capturing at most `MaxCapturedResponseChars` (16 MiB) for ingestion. Because `HttpClient.Timeout` stops applying the moment the response headers arrive, the buffered branch re-arms that **same** configured timeout (5 min — `Proxytrace.Proxy/Module.cs`, passed in rather than duplicated) as a linked `CancellationTokenSource` around its copy loop, so an upstream that sends headers and then stalls is cut off with a **504** and recorded as such instead of pinning a request, a socket and a thread-pool continuation until the client gives up ([#475](https://github.com/SyntaktikEU/Proxytrace/issues/475)); a client abort stays distinguishable from that timeout and still propagates. The streaming branch splits SSE lines itself (bounded by `MaxForwardedLineChars`, 256 KiB) on **LF, CRLF or a lone CR** — all normalized to LF on the wire, as the `ReadLineAsync` it replaced did ([#480](https://github.com/SyntaktikEU/Proxytrace/issues/480)).
- **Proxytrace.Proxy.Api** — **Standalone** deployable host for the proxy pipeline (own `Program`/`Dockerfile`/`Module`). Loads `Proxytrace.Proxy.Module` for the shared pipeline and adds the host-lifecycle services: it deliberately constructs `Storage.Module` with `registerApplicationServices: false` and never registers `Application.Module`, so **no Application service runs in the proxy** (test runner, optimizer, ingestion worker, search indexing, demo seeder, …). It registers `Proxytrace.Infrastructure.Security.SecretProtectionModule` directly (needed to decrypt upstream provider keys — see docs/security.md), plus small local stubs for the factory delegates the storage model-building graph expects. The licensing module is registered with `ServerCheckEnabled = false` in **both** build flavors — the main app owns the license-server heartbeat and the offline-grace cache file in the shared data dir; the proxy only *consumes* the snapshot for use-time gating and keeps the DB-stored license fresh via a polling `ProxyStoredLicenseService` (host-only, lives in `Proxytrace.Proxy.Api.Internal`).
- **Proxytrace.Messaging** — Ingestion transport between the proxy (producer) and the app's ingestion worker (consumer), via `IIngestionStream`. Backed by **Redis Streams** in production (`StackExchange.Redis`); backed by an in-memory channel in tests and single-process/kiosk runs.
- **Proxytrace.Licensing** — License resolution and feature/limit gating via `ILicenseService`. Tiers, `LicenseFeature`/`LicenseLimit`, JWT public-key verification. See [`licensing.md`](licensing.md).
- **Proxytrace.Testing** — `BaseTest<TModule>` and shared test infrastructure (MSTest + AwesomeAssertions + NSubstitute).
- **Proxytrace.Client.Sample** — Console app demonstrating client-side usage of the API.
- **frontend/** — React 19 + Vite + Tailwind CSS 4 SPA.

## Ingestion flow

Trace capture is **decoupled** from the main app through the messaging stream:

```
Your Agent ──► Proxytrace.Proxy.Api ──► Upstream LLM provider
                      │ (captures call)
                      ▼
               IIngestionStream  (Redis Streams in prod; in-memory otherwise)
                      │
                      ▼
         Application ingestion worker ──► AgentCallIngestor ──► Storage
```

`PublishAsync` is **fire-and-forget on the proxy hot path** — keep it cheap and never rely on it to surface processing errors. "Cheap" is a real constraint, not a wish: the proxy `await`s the publish inside the `finally` of both response paths with `CancellationToken.None`, so anything slow there is added to every proxied response and a client abort cannot release it. The Redis multiplexer is built with `AbortOnConnectFail = false`, which means a command issued while Redis is down is *backlogged* until its async timeout (~5s) instead of failing fast — so `RedisIngestionStream.PublishAsync` short-circuits on `IConnectionMultiplexer.IsConnected` and **drops the capture with a warning** rather than stalling the response (the same guard `GetQueueDepthAsync` uses). Keep that guard in front of any new command you put on this path. The consumer must `AckAsync` each `IngestEnvelope` only after processing succeeds. Recovery from a retryable failure depends on the transport (`IIngestionStream.RedeliversUnacknowledged`): Redis Streams redeliver unacknowledged envelopes, so the worker leaves the entry pending and caps redelivery attempts; the in-process channel drops anything unacked, so the worker retries inline (bounded) instead — otherwise a retryable failure would silently lose the captured call.

**What counts as retryable.** `AgentCallProcessor.IsRetryable` is the single place that decides whether a failed ingest is requeued/retried or dropped as poison, and *dropped means the captured trace is gone for good*. Retryable = anything transient: EF Core's `DbUpdateException`/`DbUpdateConcurrencyException` (unique-index races), ADO.NET `DbException` (connectivity, deadlocks), and the domain-level `OptimisticConcurrencyException` raised by `AbstractRepository`'s own concurrency pre-check before EF sees the write. That last one matters because ingestion *mutates the agent* (endpoint, model parameters, current version), so a burst of calls for one agent genuinely collides — and a lost race is exactly the case a retry fixes. Everything else (malformed payload, validation failure, missing referenced entity) is poison and is dropped. When you add a write to the ingest path, check that the exceptions it can raise land on the right side of this line.

**No duplicate traces from reclaim.** On Redis the consumer runs `XAUTOCLAIM` each round to recover entries pending on a dead consumer. Two guards stop a slow-but-live persist from being reclaimed and double-processed into a duplicate trace row / SSE event / outlier eval (there is no idempotency key on `AgentCall`, and a content-unique index is unsafe because two identical calls must both persist): (1) `MessagingConfiguration.ReclaimIdleMs` is sized far above the worst-case single-envelope persist time so reclaim only ever targets a genuinely dead consumer; (2) `AgentCallIngestionWorker` tracks in-flight transport entry ids and skips any reclaimed duplicate that overlaps the still-in-flight original, which keeps the ack exactly-once. This dedup is per-instance and assumes a single ingestion-worker instance.

**Nothing may fail an ingest after the call row commits.** Redelivery is the only recovery the transport has, and once `AgentCall` is persisted a redelivered envelope produces a *duplicate* trace rather than a retry. So every side effect in `AgentCallProcessor.IngestAsync` after `agentCallRepository.AddAsync` — the session-activity upsert, the `TraceCreatedEvent` broadcast, the blocked-call attribution/notification, the custom-anomaly review enqueue — is **best-effort**: it still gets the real `CancellationToken` so the I/O aborts promptly on shutdown, but its exception is logged and swallowed instead of reaching the outer `catch (OperationCanceledException) { throw; }` / retryable-rethrow. When adding a post-persist step, put it inside that guarded tail; never let it propagate.

**The stream is only for the out-of-process producer.** The worker and an in-process producer share one core, `IIngestionExecutor` (quota → re-hydrate provider/project → `AgentCallProcessor`). The standalone **proxy** (`Proxytrace.Proxy.Api`) is a separate process, so it *must* publish to `IIngestionStream` to hand the call across the process boundary. The **Tracey chat passthrough** (`TraceyChatController`) runs **inside the app**, so it calls `IIngestionExecutor` **directly** — never the stream. Routing an in-process capture through Redis would make it depend on a transport it doesn't need and silently drop every Tracey trace whenever Redis is down. **Rule:** a same-process capture uses `IIngestionExecutor`; only the cross-process proxy uses `IIngestionStream`.

## Dependency Injection (Autofac)

DI is wired with Autofac. Each project ships a `Module : Autofac.Module` (`Proxytrace.Domain.Module`, `Proxytrace.Application.Module`, `Proxytrace.Storage.Module`, `Proxytrace.Infrastructure.Module`, `Proxytrace.Serialization.Module`, `Proxytrace.Common.Module`, `Proxytrace.Api.Module`, `Proxytrace.Proxy.Module` (pipeline lib), `Proxytrace.Proxy.Api.Module` (standalone host), `Proxytrace.Testing.Module`). `Proxytrace.Domain.Module` and `Proxytrace.Storage.Module` discover entities, generators, configurations, and repositories by reflection — no manual registrations for the standard entity pattern. The API serves the compiled React app from `wwwroot/` in production.

**Bridging to `IServiceCollection`.** Modules that need Microsoft-DI extension methods (`AddHttpClient`, `AddMemoryCache`, …) call `builder.RegisterServiceCollection(services => …)`, which fills a fresh `ServiceCollection` and `Populate`s it into Autofac. Those extension methods share their plumbing through `TryAdd`/`TryAddEnumerable`, which dedupes only **within one collection** — so every caller re-adds it and `Populate` faithfully registers each copy. Four modules calling `AddHttpClient` (Api, Application, Licensing, Proxy) therefore put four `IHttpMessageHandlerBuilderFilter`s in the container, and each one's logging handler wrapped every outgoing request: one upstream LLM call, logged four times ([#451](https://github.com/SyntaktikEU/Proxytrace/issues/451)). `RegisterServiceCollection` now drops descriptors whose (service, implementation, lifetime) triple an earlier call already populated into the same container — an identical type-based registration can never mean two different things, while genuine multi-registrations use distinct implementation types and instance/factory descriptors are left alone.

`Proxytrace.Application.Module` registers the hosted services for ingestion + test running plus the optimization, statistics, playground, test-case and search sub-modules. `Proxytrace.Storage.Module` takes a `Func<IServiceProvider, StorageConfiguration>` (the configuration is auto-detected by `Proxytrace.Api.Module`) plus a `registerApplicationServices` flag (default `true`).

**The `registerApplicationServices` flag.** When `true` — the API/app host and the test/perf harnesses (`Storage.Tests`, `Domain.Tests`, `Application.Tests`, the perf harness) — `Storage.Module` registers Storage's own startup/initialization hosted services: the DB-initializer (`IDatabaseInitializer`) plus the secret/preview backfill services. The standalone **proxy host** (`Proxytrace.Proxy.Api`) passes `false`: it attaches to an already-migrated database read-only and runs no schema init or backfills. Since [#270](https://github.com/SyntaktikEU/Proxytrace/issues/270), `Storage.Module` no longer references or registers `Application.Module` (the flag's name is historical) — each composition root that needs the Application graph (the API host plus the four `Storage.Tests` / `Domain.Tests` / `Application.Tests` / perf harnesses) registers `Application.Module` **and** the at-rest secret seam (`Infrastructure.Security.SecretProtectionModule`) explicitly. The API root's registrations are idempotent (the `IfNotRegistered`/`builder.Properties` guards make any double registration a no-op).

## Multi-tenant list scoping (`IProjectAccessGuard`)

Every resource belongs to an `IProject`; users belong to projects via `Project.Members`, and the
`Admin` role bypasses membership. Controllers never trust a raw route/query id — they resolve the
owning project of what they are about to read or mutate and ask
[`IProjectAccessGuard`](../Proxytrace.Api/Auth/IProjectAccessGuard.cs) (the IDOR fix, #193).

For a **single resource**, use `CanAccessProjectAsync` and hide a denial behind a `404`.

For a **list endpoint** with an optional `projectId` filter, use the
`ResolveListScopeAsync(projectId)` extension in
[`Proxytrace.Api/Auth/ProjectListScope.cs`](../Proxytrace.Api/Auth/ProjectListScope.cs) — **not**
`GetAccessibleProjectIdsAsync` directly. It returns the projects the query must be restricted to:

| Result | Meaning | What the endpoint does |
|---|---|---|
| `null` | every project (an admin, unfiltered) | run the unscoped query |
| empty | nothing | return an empty result without querying |
| one id | that project | run the existing indexed by-one-project query (`SingleProject()`) |
| several ids | the caller's memberships | filter the query by the set |

The set case is what makes an **unfiltered** request from a non-admin return that caller's own rows
instead of an empty page (#482 — the callers who hit it were REST API keys and MCP, which are
confined to one project and so have no reason to send a `projectId`). It must be applied *inside*
the query: paging has to be computed over the union, so a paged endpoint needs a set-aware
repository method (`GetByProjectsPagedAsync`) rather than merging per-project pages. `ToFilterScope()`
splits a scope into the `(ProjectId, ProjectIds)` pair `AgentCallFilter` takes, so the hot traces
query keeps its equality predicate whenever the scope names a single project.

An endpoint that can also be narrowed by a **related** entity (`agentId`, `suiteId`) resolves the
scope first and then checks that entity against it — it never replaces the scope, and a named entity
the caller may not reach collapses the scope to empty. `TestRunGroupsController.ListScopeAsync` and
`ProposalsController.ListScopeAsync` are the shape to copy. An endpoint with *no* `projectId`
parameter at all still resolves a scope (`ResolveListScopeAsync(requestedProjectId: null)`) — that is
how `GET /api/test-runs` answers a non-admin with their own runs instead of nothing.

Aggregates built on `StatisticsFilter` take the same `(ProjectId, ProjectIds)` pair (#483), so the
traces overview aggregates over a multi-project scope like the lists beside it. `ToFilterScope()`
feeds both filters, so a scope naming one project keeps the single-project predicate in both. The set
must be applied in **both** of the filter's translation paths — the LINQ chokepoint
`AgentCallStatsQueries.Query()` and the hand-built raw-SQL `BuildLatencyWhere()` behind the latency
percentiles, where it goes over as one `uuid[]` parameter (`= ANY(@projectIds)`), never interpolated.
`StatisticsFilterParityTests` fails if a new filter member reaches only one of them.
`StatisticsController`'s dashboard is unchanged: it refuses an unscoped non-admin request with `403`
(an explicit contract, not a silent empty page).

**No controller re-implements the check.** `ProjectsController` — where the listed resource *is* the
project, so there is no `projectId` filter — used to do its own `User.IsInRole` + membership test.
That is exactly what the guard exists to prevent: a role claim says nothing about the REST API key
the request came in on, so a key minted for project A could read project B's detail and its members
(PII) whenever the key's *owner* was a member of B (#474). It now calls
`ResolveListScopeAsync(requestedProjectId: null)` for `GET /api/projects` and `CanAccessProjectAsync`
for the by-id reads, like every other controller. If you need a role or membership decision in a
controller, extend the guard — never inline it.
