# Audit Log

The audit log records **system actions with user context** — who did what, to which target, in
which project — for compliance and operational review. It is separate from the error/warning
[Error Log](architecture.md): the error log captures `Error`/`Critical` `ILogger` entries; the audit
log captures deliberate, semantic actions (a test run started, an API key minted, a project deleted,
a license changed).

Read this before touching audit emission, the capture pipeline, or the read API.

## Emitting an audit event (ILogger-native)

Audit events are emitted through `ILogger`, not a bespoke service. Inject the marker-typed logger
`ILogger<Audit>` (`Audit` is `Proxytrace.Domain.AuditLog.Audit`) and call the `LogAudit`
extension after the action has succeeded:

```csharp
audit.LogAudit(
    AuditAction.ApiKeyMinted,        // the semantic action (also the log EventId)
    nameof(IApiKey),                 // target type
    saved.Id,                        // target id (nullable)
    saved.Name,                      // human label snapshot (nullable)
    projectId: project.Id,           // null => instance-wide (global) action
    details: JsonSerializer.Serialize(new { scopes, ownerEmail })); // optional JSON
```

Call sites supply **only** the action and its target — the actor (user / API key / system) is
enriched automatically (see below). Emit *after* the mutation persists, so a failed/no-op action is
not audited (e.g. archive-based deletes only audit when `ArchiveAsync` actually transitioned a live
row — a repeated delete of an already-archived entity is a 404 no-op and records nothing). Pass
`projectId` for project-scoped actions; leave it null for global actions (e.g. license changes).
`details` is a pre-serialized JSON string of any action-specific context.

Most actions record `Success`; the deliberate `outcome: AuditOutcome.Failure` producers are
**failed sign-in** (`AuditAction.LoginFailed`) and **failed second factor** (`MfaChallengeFailed`),
so brute-force / credential-stuffing is visible, plus **denied access** (`AuditAction.AccessDenied`):
`AuditDeniedAccessMiddleware` records a failure when a state-changing request (POST/PUT/PATCH/DELETE)
is rejected with **403 Forbidden** — an authenticated caller attempting something beyond their
permission (non-admin hitting an admin route, a member acting on another project, a license-gated
feature). It is attributed to the caller (their id was stashed at authentication time), so
privilege-probing is visible. **Scope/limits:** only `403` is captured — `401` (unauthenticated)
carries no actor and overlaps `LoginFailed`, so it is intentionally skipped; and access checks that
hide existence behind a `404` (the `IProjectAccessGuard` paths) are indistinguishable from genuine
404s at the response layer and are **not** recorded. The middleware sits *before* `UseAuthorization`
in the pipeline so it still runs when authorization short-circuits the request.

Several actions are recorded by background/system work with **no request context** ⇒ the System
actor: the test-run scheduler (`TestRunStarted`), the theory A/B validation pipeline
(`TheoryValidated` / `TheoryInvalidated` / `TheoryValidationFailed` / `ProposalGenerated`), automatic proposal adoption
(`ProposalAutoAdopted`), and the one-time secrets backfill (`SecretsBackfilled`).

The **password-reset** actions follow the same shape: `PasswordResetRequested` is emitted for every
forgot-password attempt (the submitted email is the target, even when no account matches — like
`LoginFailed`, it records the attempt without revealing whether the address exists), `PasswordResetCompleted`
when a reset token is redeemed, and `PasswordResetLinkIssued` when an admin mints a reset link from the
Users page. The first two come from `AuthController` (`[RequireLocalMode]`); the third from `UsersController`
(admin-only).

The **MFA (TOTP)** actions are `MfaEnabled` (a user confirms enrollment), `MfaDisabled` (a user disables
their own MFA, or an admin clears it for lockout recovery via `UsersController`), and `MfaChallengeFailed`
(a wrong second-factor code — the `AuditOutcome.Failure` analog of `LoginFailed`). A **successful** MFA
login emits the normal `UserLoggedIn`, so there is one uniform "logged in" signal regardless of factor.
See [`docs/mfa.md`](mfa.md).

**Authentication events are local-mode only.** The auth-lifecycle actions (`UserLoggedIn`,
`LoginFailed`, `UserLoggedOut`, `UserSignedUp`, `AdminBootstrapped`, `LegacyAccountClaimed`,
`UserInvited`, `InviteRevoked`) are emitted from `AuthController`, whose endpoints are
`[RequireLocalMode]`. Under OIDC, interactive sign-in of an **existing** user happens at the IdP and
is not re-recorded here (it lives in the IdP's logs). **Just-in-time provisioning of a new user is
audited**, though: `JitUserProvisioner` emits `AdminBootstrapped` for the first provisioned user and
`UserSignedUp` for each subsequent one (the new user is the target; System actor, since the user's id
is not yet stashed at provisioning time). All *non-auth* actions are recorded in OIDC mode as usual.

`LogAudit` packs the fields into a strongly-typed `AuditState` and logs at `Information` with
`EventId = (int)action`. The strong typing — rather than a `{Placeholder}` message template — is what
keeps required fields compile-time-checked while staying "just `ILogger`" at the call site.

## Capture pipeline

Mirrors the error-log pipeline, with deliberate divergences:

1. **`AuditChannelLoggerProvider`** (an `ILoggerProvider`, auto-discovered by the logging framework)
   returns a capturing `AuditChannelLogger` **only** for the `Audit` category; every other category
   gets a `NullLogger`. A defensive `AddFilter` pins the category at `Information` so log-level config
   can never silence auditing. Audit logs at `Information`, so the error-log provider (which captures
   `>= Error`) never double-captures them.
2. **`AuditChannelLogger.Log`** captures only `AuditState` payloads (ordinary logs on the category are
   ignored). It resolves the actor **synchronously** via `IAuditActorAccessor` and stamps the event
   time, then enqueues an `AuditCapture` — all before returning, because the background writer has no
   request context.
3. **`AuditChannel`** is an **unbounded** channel that never drops on write (`TryWrite` always
   succeeds), unlike the error log's bounded drop-oldest channel — audit events are low-frequency and
   should all persist. (Note: "never drops" is a write-time property; see the shutdown caveat below.)
4. **`AuditWriter`** (a `BackgroundService`) drains the channel and persists each entry as an
   `IAuditLogEntry`, using the captured event time as `CreatedAt` (not the later drain time).
   Persistence failures go to `Console.Error` only. On graceful shutdown it makes a **best-effort
   drain** of the buffered backlog (via `IAuditChannel.TryRead`, with `CancellationToken.None`) before
   exiting — far better than the error log (which loses its backlog on stop). It is *not* a hard
   guarantee: the channel writer is never completed, so an entry enqueued by an in-flight request
   *after* the drain loop observes the channel empty, or one still in flight if the drain outruns the
   host shutdown timeout, can be lost. Treat losslessness as best-effort, not absolute.
5. **`AuditLogCleanupService`** applies age-based retention only — there is **no count cap** (the log
   is lossless). Default retention is **365 days** (`AuditLogCleanupConfiguration`).

### Actor enrichment (`IAuditActorAccessor`)

The audit logger is a singleton and cannot take the request-scoped `ICurrentUserAccessor`, so actor
resolution is abstracted behind `IAuditActorAccessor` (in `Proxytrace.Application.AuditLog`,
implemented by `HttpContextAuditActorAccessor` in the API layer over `IHttpContextAccessor`). It
reads, with no DB hit:

- **User** — the user id stashed at auth time in `HttpContext.Items["Proxytrace.UserId"]` (JWT,
  cookie, and MCP API key all set it), plus the email from the `email`/`ClaimTypes.Email` claim.
- **API key** — detected by the `McpApiKey` auth scheme; attributed to the key's owner id, with the
  key id from `HttpContext.Items`. (No email claim on API-key requests — `ActorEmail` is null.)
- **System** — no request context (scheduler, background services) ⇒ `AuditActor.System`.

Only genuinely non-HTTP hosts — the test composition that loads `Application.Module` without the API
layer — have no accessor registered; the provider resolves it optionally (`sp.GetService`) and falls
back to the System actor. The **kiosk runs the full API host**, so it *does* register the accessor and
attributes actions to the seeded kiosk user.

## Storage (FK-free, immutable)

`IAuditLogEntry` follows the immutable [five-file pattern](domain-entities.md) (modelled on
`IApplicationError`). Its storage row is **denormalized and has no foreign keys**: `ActorUserId`,
`ActorApiKeyId`, `ProjectId`, and `TargetId` are plain `Guid?` columns, and `ActorEmail` /
`TargetLabel` are snapshot strings. This is the whole point — a recorded action **survives deletion**
of the user / key / project / target it refers to (the "project deleted" row must outlive the
project). Do **not** add `HasOne`/`HasForeignKey` to `AuditLogEntryConfig`. Indexes: `CreatedAt`,
`Action`, `ProjectId`.

`IAuditLogRepository.RemoveOlderThanAsync` branches relational vs in-memory for `ExecuteDelete` (the
in-memory provider does not support it — see [database.md](database.md)).

## Read API & visibility

`GET /api/audit-log` (and `/{id}`) is `[Authorize]` (any authenticated user) and **scopes results**:

- **Admins** see every entry, including instance-wide (null-`ProjectId`) global rows.
- **Project members** see only the entries of projects they belong to, and **never global rows**.
  A member querying a project they are not in gets an empty page; an out-of-scope `/{id}` returns 404
  (existence is not leaked).

Filters: `action`, `actor` (case-insensitive email infix), `projectId`, `targetType`, `targetId`,
`from`/`to`, plus pagination. Scoping is enforced in `AuditLogController` (admin ⇒ no project
restriction + global; member ⇒ their `IProjectRepository.GetByMemberAsync` ids, no global) and applied
in the repository's `GetPagedNewestFirstAsync(projectIds, includeGlobal, …)`.

## Adding a new audited action

1. Add a value to `AuditAction` (append — never renumber; the value is persisted and used as the
   `EventId`).
2. Emit `audit.LogAudit(AuditAction.New, …)` at the action's seam — the controller for
   user-initiated actions, the service for background/system-initiated ones (e.g. the test-run
   scheduler emits `TestRunStarted` as the System actor).
3. Resolve the owning project for `projectId` (e.g. `IAgentRepository.GetProjectIdAsync` /
   `IEvaluatorRepository.GetProjectIdAsync` are cheap FK projections). A **test case** has no FK to a
   project of its own — suites reference test cases by a serialized JSON `Guid[]` column with no
   queryable reverse FK — so `TestCasesController.Update` resolves the project through the
   suite→agent reverse projection `ITestSuiteRepository.GetProjectIdByTestCaseAsync` (it reads the
   low-volume suite rows and tests membership in memory, so it is portable across the relational and
   in-memory providers; do not emit with `projectId: null`, which would wrongly make the edit a
   global/admin-only row project members cannot see).
4. Add the new action's label to the frontend audit-log filter and `manual/admin/audit-log.md`.

Currently recorded ranges worth knowing about: **71–75** are the cost-budget actions —
`CostLimitCreated`/`Updated`/`Deleted` (admin-initiated, from `CostLimitsController`) and
`CostBudgetSoftLimitReached`/`CostBudgetHardLimitReached` (System actor, from `CostBudgetGuard`).
The two crossings are audited *in addition to* their notification, so the record survives
notification acknowledgement and retention. See [`cost-controls.md`](cost-controls.md).
