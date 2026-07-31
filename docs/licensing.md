# Licensing

`Proxytrace.Licensing` gates product capabilities behind license tiers. `ILicenseService` is the
**single source of truth** for licensing decisions across the application — never hard-code tier
checks or duplicate gating logic elsewhere.

```csharp
public interface ILicenseService
{
    LicenseSnapshot Current { get; }            // never null; defaults to Free
    event Action Changed;                       // fires when the tier changes (e.g. background downgrade)
    bool IsFeatureEnabled(LicenseFeature feature);
    long GetLimit(LicenseLimit limit);          // long.MaxValue == unlimited
    Task ForceRefreshAsync(CancellationToken cancellationToken = default);
}
```

## Directives

- **Gate every premium capability through `ILicenseService`.** Before exposing or executing a gated
  feature, check `IsFeatureEnabled(...)` / `GetLimit(...)`. Current gated features:
  `OptimizationProposals`, `AgenticEvaluators`, `CustomEvaluators`, `SsoOidc`, `AuditLog`, `Tracey`,
  `ScheduledTestRuns`, `CustomAnomalyDetectors`, `CostControls`, `TestCaseSynthesis`.
- **`Current` is never null** and defaults to the **Free** tier — write code that degrades to Free,
  never code that assumes a paid tier or null-guards the snapshot.
- **Treat `long.MaxValue` from `GetLimit` as unlimited** — do not cap or special-case it elsewhere.
- **React to `Changed`** for long-lived state: the tier can change at runtime — downgraded by a
  background license-server check, or **replaced entirely** when an admin sets/removes a license
  key in the UI — so cached feature decisions must be invalidated.
- License tokens are **JWT, verified against bundled public keys** (`LicensePublicKeys`). Never trust
  an unverified tier value; always go through the service.
- The trusted keys are fixed at **compile time**: `LicensePublicKeys.GetActiveKeys()` returns the
  embedded production key unless the build ran with `-p:LicensePublicKey=<base64-spki>[,<more>]`
  (Docker build-arg `LICENSE_PUBLIC_KEY`), which bakes replacement keys into assembly metadata.
  The dev/e2e composes use it to trust the committed test-signed license JWTs; official release
  images pass no override. The `PROXYTRACE_LICENSE_PUBLIC_KEY` **runtime** env override exists only
  in Debug builds — never extend it to Release, or official-image operators could self-sign.
- The repo is public under the **Elastic License 2.0**, whose "may not remove or circumvent the
  license key functionality" limitation is the legal backstop for these gates: building a
  gate-stripped fork is a license violation even though the source is readable.
- When adding a new gated capability, add a `LicenseFeature`/`LicenseLimit` member and assign it to
  the right `TierDefinition` rather than checking the tier enum directly at the call site.

## Runtime license management (set/remove without restart)

The effective license resolves in this precedence order: **database-stored key → environment JWT
(`PROXYTRACE_LICENSE` / `Licensing:License`) → Free**. Kiosk's `OverrideSnapshot` short-circuits
everything and is not user-manageable.

- `LicenseSnapshot` carries a `Source` (`None`/`Environment`/`Stored`/`Override`) and, when a
  configured key fails validation, `Status = Invalid` + `InvalidReason` with **Free entitlements**.
  An invalid license **never crashes the host** (the old fail-fast startup gate is gone); the UI
  surfaces a banner instead.
- `ILicenseActivator` (`Proxytrace.Licensing`) — validation + snapshot swap:
  `Validate` (dry run, throws `InvalidLicenseException`), `Activate` (throws; current license kept
  on rejection), `ActivateOrInvalid` (never throws — applies an Invalid snapshot), and
  `ActivateConfigured` (re-resolves override/env/Free; used on remove).
- `ILicenseKeyManager` (`Proxytrace.Application.Licensing`) — orchestrates persistence:
  `SetAsync` validates → persists via `IStoredLicenseStore` (single-row `StoredLicenseEntity` in
  Storage) → activates as `Stored`; `RemoveAsync` deletes and falls back via `ActivateConfigured`.
- `StoredLicenseStartupService` (Application hosted service, registered **after** the database
  initializer) applies the stored key once migrations have run. Failures are logged, never fatal.
- `LicenseCheckService` reacts to `Changed` — a license activated at runtime starts revocation
  checks; it no longer latches onto the startup snapshot.
- API: `GET /api/license` (anonymous; includes `source`/`invalidReason`),
  `POST /api/license/validate` (anonymous dry run), `PUT /api/license` (admin, **or anonymous while
  no users exist** — the setup wizard's gate), `DELETE /api/license` (admin),
  `POST /api/license/refresh` (admin). Manage endpoints 409 under a kiosk override.
- The license offline-grace cache and the auto-generated signing key live in
  `PROXYTRACE_DATA_DIR` when set (the Docker deployment mounts the `appdata` volume there).

## Offline-only licenses

An **offline-only** license is for air-gapped installs that cannot reach the license server. The
JWT carries one extra claim, `offline` (a JSON boolean), and the server emits it **present and
`true`** only on these keys — a normal online license omits the claim entirely.

- `JwtLicenseValidator` parses `offline` by JSON type onto `LicenseSnapshot.Offline` (true only when
  present and exactly `true`; absent / `false` / any other value ⇒ online). Everything else about the
  token — ES256 signature, `iss`/`aud`/`exp` validation against the bundled public keys — is
  unchanged, so an offline token still verifies fully offline.
- `LicenseCheckService` **skips the periodic `/licenses/check` call entirely** for an offline
  snapshot (both the background loop and the admin "Re-check now" / `ForceRefreshAsync` path). The
  offline-grace state machine (the `OfflineGracePeriodDays` window that degrades a *normal* license
  when the server is unreachable) therefore never runs for these keys — they do not degrade just
  because there is no network.
- With no server check, **`exp` is the only thing that ends an offline license**, and it is enforced
  locally: an already-expired token is rejected at validation (`ValidateLifetime`), and a token that
  expires *while running* is downgraded to `Expired`/Free by `EnforceOfflineExpiry`. The loop wakes
  at the sooner of the check interval and the moment `exp` lands so the license ends on time.
- **Security:** an offline key cannot be revoked (the client never contacts the server, so
  `revoke`/`reissue` and key rotation have no effect on it) and is a bearer credential that works on
  unlimited installs until `exp`. The server caps offline `exp` at ≤365 days; that time bound is the
  only containment lever. Prefer the shortest viable lifetime.
- `Offline` flows through to the API (`GET /api/license` → `LicenseDto.offline`,
  `POST /api/license/validate` → `ValidateLicenseResultDto.offline`); the settings License page
  surfaces it (an "offline license" note and no "Re-check now" button, since there is nothing to
  ask the server).

## Notes on specific gates

- **`MaxTracesPerMonth`** is an **installation-wide** limit whose *enforcement* is **per project**
  (`TraceQuotaGuard` / `ITraceQuotaGuard.IsOverQuota`). Enforcing it as one global switch — the
  original shape — meant a single busy project could consume the whole month's allowance and
  silently stop capture for every other project. The guard therefore also counts per project and
  drops only for projects at or above their **equal share** of the cap; because the total is at or
  over the cap, at least one project must be above its share, so the licence still binds while quiet
  projects keep capturing.
  Two further properties are load-bearing and should not be dropped in a refactor:
  - **A drop is never silent.** The captured call is still acked to the client (failing the proxied
    request would take the caller's application down over a billing limit), so the guard raises a
    `NotificationKind.TraceQuotaReached` notification per project as it starts being throttled, and
    logs at **Error** — the level the operator Error Log captures. Entering the state is reported
    once per month, not on every recompute.
  - **Polling tightens near the cap.** The recompute interval drops from 5 minutes to 30 seconds
    once usage passes 90% of the limit; at the flat 5-minute interval a busy install could ingest a
    long way past its cap before the flag flipped.
- **`AgenticEvaluators`** is enforced at *use* time, not creation time. Default agentic evaluators
  are provisioned for every project regardless of tier (`IDefaultEvaluatorProvisioner`); the gate
  applies in two places: (1) `TestRunnerService` skips agentic evaluators during a run when the
  feature is disabled — they are silently not run, never errored, so the pass rate is computed over
  judged evaluators; (2) the suite editor (`EvaluatorsPanel`) locks unattached agentic evaluators on
  the frontend. An agentic evaluator attached while licensed simply stops running after a downgrade.
- **`ScheduledTestRuns`** (Enterprise) is enforced both at *creation* and at *use*. The
  `TestRunSchedulesController` decorates create/update/delete/run-now with
  `[RequiresFeature(LicenseFeature.ScheduledTestRuns)]` (returns **402** when unlicensed); listing
  stays ungated so existing schedules remain visible. At use time `TestRunSchedulerService` skips
  unlicensed schedules without deleting them, so a downgrade pauses scheduled runs and a re-upgrade
  resumes them — the feature degrades gracefully rather than destroying configuration.
- **`CustomAnomalyDetectors`** (Enterprise) gates the user-defined LLM-based anomaly detectors. The
  whole `CustomAnomalyDetectorsController` (all CRUD, including list) carries
  `[RequiresFeature(LicenseFeature.CustomAnomalyDetectors)]` and returns **402** when unlicensed, so
  the management UI is hidden without a license. The anomaly *dashboard* itself (timeline,
  recent-flagged list) is ungated — it also surfaces the built-in statistical outliers, which every
  tier gets. The same feature also covers **real-time blocking** (`BlockUpstream` detectors enforced
  in the proxy): the proxy references `Proxytrace.Licensing` and its `CachedBlockingRuleProvider`
  checks `IsFeatureEnabled(CustomAnomalyDetectors)` at use time — unlicensed, no requests are
  blocked but the configuration is preserved (graceful degrade, mirroring `ScheduledTestRuns`). The
  proxy runs the licensing module with `ServerCheckEnabled = false` (the main app owns the
  license-server heartbeat and the offline-grace cache) and applies the DB-stored license via a
  polling `ProxyStoredLicenseService` (~5 min, configurable `Licensing:StoredLicensePollSeconds`);
  accepted consequence: a revoked-but-unexpired license keeps blocking active in the proxy until it
  expires or the stored key is removed.
- **`CostControls`** (Enterprise) gates **changing** a monthly cost budget, not seeing one. The Costs
  page, `GET /api/statistics/cost-overview` and `GET /api/cost-limits` are ungated so the page reads
  identically on every tier; only `POST`/`PUT`/`DELETE /api/cost-limits` carry
  `[RequiresFeature(LicenseFeature.CostControls)]` (**402**) on top of the `Admin` role. At use time
  both enforcement paths degrade: `CostBudgetGuard` returns early and `CachedBudgetBlockProvider`
  reports no blocks, so an unlicensed install keeps its budget configuration, fires nothing and
  blocks nothing — re-licensing restores enforcement on the next guard tick without re-entering
  anything (mirroring `ScheduledTestRuns` / `CustomAnomalyDetectors`). See
  [`cost-controls.md`](cost-controls.md).
- **`TestCaseSynthesis`** (Enterprise) gates the agent that proposes test cases from a captured
  conversation — the trace detail's *Generate tests* action and Tracey's `propose_test_cases`.
  `POST /api/agent-calls/{id}/test-case-proposals` carries
  `[RequiresFeature(LicenseFeature.TestCaseSynthesis)]` (**402**); the button stays visible on Free
  and routes to the upgrade modal instead of opening the panel, so the capability is discoverable
  without being usable. Promoting a trace by hand (*Add test*) is ungated and unaffected — only the
  agent-proposed path is premium. Attaching the judge it may suggest additionally requires
  `AgenticEvaluators`, and the new-suite path additionally consumes the `MaxTestSuites` limit.
