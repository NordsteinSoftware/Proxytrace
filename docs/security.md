# Secrets at Rest

How Proxytrace protects persisted secrets. Read this before adding a new secret-bearing field, or
before touching encryption, hashing, the Data Protection key ring, or the secrets backfill.

## The rule: encrypt vs hash

A secret's treatment is decided by **how it is used**, not by what it is:

- **Replayable secret** — Proxytrace must recover the plaintext and send it to a third party (or
  recompute against it). These are reversibly **encrypted** (`ISecretProtector`). Example:
  `ModelProvider.ApiKey` (the upstream provider credential, replayed on every proxied/outbound call),
  the SMTP password, and `UserTotpEnrollment.Secret` (the TOTP/MFA shared secret — the server must
  reproduce it to verify each authenticator code, so it is encrypted in the storage mapper exactly
  like `ApiKey`; see [`docs/mfa.md`](mfa.md)).
- **Verify-only credential** — only ever compared against a presented value, never replayed. These
  are one-way **hashed** (`ISecretHasher`); the plaintext is shown once at creation and is
  unrecoverable afterwards. Example: `ApiKey.KeyHash` (inbound Proxytrace keys), `Invite.TokenHash`,
  `PasswordResetToken.TokenHash` (the forgot-password / admin reset link — 32-byte CSPRNG, 1-hour TTL,
  single-use; the raw token is emailed, logged for the operator, or returned as an admin link exactly
  once), and `MfaBackupCode.CodeHash` (the one-time MFA recovery codes — shown once at enrollment,
  each consumed independently).

Never hash a replayable secret (you could not replay it) and never reversibly encrypt a verify-only
credential (a database dump would then yield usable credentials).

## The two seams (interfaces in `Proxytrace.Domain.Security`; implementation in `Proxytrace.Infrastructure.Security`)

The seam **interfaces** (`ISecretProtector`, `ISecretHasher`) live in `Proxytrace.Domain.Security`, so the
storage layer can consume them without referencing `Application` (issue #270). Their Data Protection-backed
**implementations** (`DataProtectionSecretProtector`, `Sha256SecretHasher`) and the DI module
(`SecretProtectionModule`) live in `Proxytrace.Infrastructure.Security` — the lowest layer both the API host
and the lean ingestion proxy can reach without loading `Application`.

- **`ISecretProtector`** — `Protect`/`Unprotect`, backed by ASP.NET Core Data Protection
  (`DataProtectionSecretProtector`, purpose `"Proxytrace.Secrets.v1"`). The seam and its key ring are
  registered together in `Proxytrace.Infrastructure/Security/SecretProtectionModule.cs` (application name
  `"Proxytrace"`, persisted to `PROXYTRACE_DATA_DIR/dataprotection-keys`); without it the ring is
  ephemeral and ciphertext does not survive a restart. **Both hosts that touch encrypted secrets — the
  API (writer) and the lean ingestion proxy (reader, which decrypts the upstream provider key before
  replaying it) — load this module and must mount the *same* `PROXYTRACE_DATA_DIR` volume**, or the
  proxy cannot decrypt what the API wrote (the deploy/e2e compose files wire the shared `appdata`
  volume into both). Reads degrade gracefully on a `CryptographicException` (treat the secret as unset
  + log) rather than crashing a hot path — see `ModelProviderConfig.Decrypt` and
  `EmailSettingsStore.DecryptPassword`.
- **`ISecretHasher`** — `Hash(value)` → hex SHA-256 (`Sha256SecretHasher`, delegating to the shared
  `Proxytrace.Common.Security.Sha256.HexHash`). Deterministic and **key-ring-independent**, so the
  verify paths keep working even if `PROXYTRACE_DATA_DIR` is lost. Unkeyed SHA-256 is safe here
  because the secrets are 256-bit CSPRNG values — a dump cannot reverse or forge them. **Not for
  passwords** (use `IPasswordService`, which is salted + slow).

`Sha256` lives in `Proxytrace.Common` so the `Domain` layer (entity generators) can hash without
referencing `Application`.

### A missing `PROXYTRACE_DATA_DIR` must be loud

An unset variable is not an error — Development and every test harness legitimately run on an
in-memory ring — but in a real deployment it silently destroys every encrypted secret on each
restart, and the three decrypt paths (`ModelProviderConfig`, `UserTotpEnrollmentConfig`,
`EmailSettingsStore`) degrade the resulting `CryptographicException` at **Warning** only. The
operator Error Log captures `>= Error`, so the outage would present purely as upstream 401s and
"invalid authenticator code" with nothing to look at. Two things close that hole:

- `KeyRingPersistenceCheck` (an `IHostedService` in `SecretProtectionModule.cs`) logs at
  **Critical** at startup when the variable is unset, and at Information with the resolved path
  when it is. It never throws.
- Both split-shape Dockerfiles (`Proxytrace.Api/Dockerfile`, `Proxytrace.Proxy.Api/Dockerfile`)
  set `ENV PROXYTRACE_DATA_DIR=/app/data` — the path the composes already mount the shared
  `appdata` volume at — so a hand-written manifest inherits the safe default. The single-container
  image keeps its own `/data/appdata`. An explicit environment value still overrides.

## Blind-index lookup

Encryption is non-deterministic (random IV), so an encrypted column cannot be looked up or indexed by
value. Where a replayable secret also needs a by-value lookup, store a deterministic **blind-index
hash** column alongside the ciphertext and query that:

- `ModelProvider.ApiKey` (ciphertext) + `ModelProviderEntity.ApiKeyLookupHash` (indexed) —
  `FindByApiKeyAsync` hashes the presented key and matches the hash, then decrypts the row.

Hashed secrets need no separate column: the stored value *is* the hash, so the repository hashes the
presented raw value before the equality lookup (`ApiKeyRepository.FindByKeyAsync`,
`InviteRepository.FindByTokenAsync`).

## Where each transform lives

- **Encrypt** happens at the **storage mapper boundary** (`ModelProviderConfig.Map`): encrypt + set
  the lookup hash on write, decrypt on read. This round-trips safely through edits (load decrypts →
  save re-encrypts). The protector is injected as `Lazy<ISecretProtector>` so design-time migration
  tooling can build the model without a key ring.
- **Hash** happens at the **creation site** (the value entering the domain is already the hash:
  `ModelProvidersController.CreateKey`, `InviteService.CreateAsync`) and at **lookup time** in the
  repository. It must **not** happen in the mapper — a hashed entity that is re-saved (e.g.
  `Invite.MarkConsumedAsync`) would otherwise be double-hashed.

## Secrets must not survive `ToString()`

A `record`'s generated `ToString()`/`PrintMembers` prints **every** member, so a secret-bearing
record leaks its plaintext the first time anyone writes `logger.LogX("… {Settings}", settings)`,
interpolates it into an exception message, or looks at it in a debugger. **Every record that carries
a replayable secret must override `PrintMembers` and render that member as `***`** — the rest of the
members stay, so the type is still useful in a log line. The member itself stays public and part of
record equality; only its textual rendering is masked.

Current overrides: `ModelProvider` (`ApiKey`, the reference implementation), `EmailSettings`
(`Password`), `UserTotpEnrollment` (`Secret`), `KioskEndpointOptions` and `ResolvedKioskEndpoint`
(`ApiKey`). Note the accessibility differs: a sealed record deriving from `object` must declare
`private bool PrintMembers(StringBuilder)`, one deriving from another record
`protected override bool PrintMembers(StringBuilder)` (and must chain to `base.PrintMembers`).
`Proxytrace.Domain.Tests/SecretRedactionToStringTests` pins all five.

## Backfill of pre-existing rows

The product shipped with plaintext secrets, so `SecretsBackfillService` (an `IHostedService`
registered after the database initializer) protects existing rows in place on first boot. It is
idempotent and per-row (a partial run resumes; a re-run is a no-op), keyed on a marker per table:

| Table | "Not yet protected" marker | Action |
|---|---|---|
| `ModelProvider` | `ApiKeyLookupHash IS NULL` | encrypt `ApiKey`, set the lookup hash |
| `ApiKey` | `KeyHash` length ≠ 64 | hash the plaintext in `KeyHash`, set `KeyPrefix` |
| `Invite` | `TokenHash` length ≠ 64 | hash the plaintext token |

The two hash markers read the **protected column itself** (a hex SHA-256 is always 64 chars; the
pre-retrofit plaintexts never are). Do not move a marker into a companion column: it is then only as
durable as that column's mapper. `ApiKey` used `KeyPrefix IS NULL` until it turned out `ApiKeyConfig`
collapses that null (`stored.KeyPrefix ?? string.Empty`) and writes `""` back on every round trip, so
one save of an un-backfilled row would have hidden it from the pass forever — leaving the plaintext in
`KeyHash`, where `FindByKeyAsync` (which hashes the presented value) can never match it.

The accompanying migration (`ProtectSecretsAtRest`) **renames** the verify-only columns
(`ApiKey`→`KeyHash`, `Token`→`TokenHash`) rather than dropping and re-adding them, so the existing
plaintext survives into the new column for the backfill to read. A drop+add would destroy live keys
and pending invites. When changing these columns, hand-check the generated migration emits
`RenameColumn`, not drop+add.

**Failure handling.** Each table's pass is retried a few times for transient faults; a persistent
failure is logged at **Critical** (surfaced in the operator Error Log) instead of crashing boot. The
impact is real: until a row is backfilled its lookup column still holds the pre-retrofit plaintext,
so the hashed/encrypted lookup cannot match it — the affected existing API keys, provider auth, and
pending invites **do not authenticate until a restart re-runs the backfill to completion**.
Credentials created after the upgrade are unaffected.

## Credential freshness at the ingestion proxy (no positive credential cache)

The proxy resolves inbound credentials (`Proxytrace.Proxy/Internal/ApiKeyResolver.cs`, shared lib) **from
storage on every request** — deliberately uncached. A cached `ResolvedApiKey` carries the decrypted
upstream provider key, so any TTL becomes a window in which a rotated key keeps being forwarded and
a revoked inbound credential keeps authenticating, independently per proxy replica (#407; a
cross-process invalidation broadcast was rejected because a disconnected/restarting replica can miss
it). Rotation and revocation therefore take effect on the **next request**; when the database is
unreachable the proxy **fails closed** (the request errors) rather than serving stale credentials.
The per-request cost is a few indexed point lookups plus one Data Protection decrypt, guarded by the
`proxyResolve*` budgets in `perf/perf-budgets.json`. Do not reintroduce positive caching on this
path; the freshness guarantee is pinned by `ApiKeyResolverRotationTests`.

## Threat model

Protects **database dumps and backups**: the encryption key ring lives outside the database (in
`PROXYTRACE_DATA_DIR`), and the hashes are one-way over high-entropy secrets. It does **not** defend
against an attacker who holds **both** the database and the data directory — acceptable for a
self-hosted, single-deployment product.

## Password-reset link logging (emergency recovery)

`PasswordResetService` (`Proxytrace.Application/Auth/Local/Internal/`) issues a single-use, 1-hour,
hash-stored reset token. The link is normally emailed; when SMTP is unconfigured **or** the send
fails, the service falls back to the operator log so a locked-out user — including a **sole admin** —
can still recover. That fallback is gated:

- **`Authentication:EmergencyLogResetLink` (default `false`)** — the warning is **redacted**: it
  carries only a truncated, one-way **token hint** (the first 12 hex chars of the stored `TokenHash`,
  enough to correlate the log line with the DB row, useless for reconstructing the live token), the
  expiry, and a one-line instruction telling the operator how to enable emergency logging. The live
  token/URL is **never** written, so a reader of the operator log cannot take over the account within
  the TTL.
- **`Authentication:EmergencyLogResetLink = true`** — break-glass: the full one-time reset URL is
  logged at Warning. Use this only while actively recovering a locked-out sole admin with no working
  SMTP, then turn it back off — anyone with log read access within the 1-hour TTL can take over the
  account.

Operators always have two non-logged recovery paths that do **not** require this flag: configure SMTP
([`/admin/email`](../manual/admin/email.md)), or, if another admin exists, mint a link directly from
**Settings → Users → Reset password** (shown once in the UI, never logged). The flag exists only for
the genuine sole-admin-plus-no-email lockout.

## Reverse-proxy trust: forwarded headers

The documented topology terminates TLS at a reverse proxy (`frontend/nginx.conf`) and forwards to the
API **over plain HTTP**. Without processing `X-Forwarded-*` the API therefore sees `http` as the
scheme and the proxy container's address as the client for *every* request. `Program.cs` runs
`UseForwardedHeaders` as its **first** middleware, processing `XForwardedFor | XForwardedProto`, so
rate-limit partition keys, audit trails and generated absolute URLs describe the real client.

Those headers are attacker-controlled unless the peer that sent them is trusted, and an unrestricted
`X-Forwarded-For` is *worse* than none: it turns the rate-limit partition key into a value the client
picks, defeating throttling entirely. The trust set is therefore operator-declared under the
**`ForwardedHeaders`** config section (`TrustedProxyConfiguration` in `Program.cs`):

| Key | Default | Meaning |
|---|---|---|
| `ForwardedHeaders:Enabled` | `true` | Set `false` to skip the middleware entirely. |
| `ForwardedHeaders:KnownProxies` | *(none)* | Trusted proxy addresses. Comma-separated or an array. |
| `ForwardedHeaders:KnownNetworks` | *(none)* | Trusted CIDR ranges, e.g. `172.16.0.0/12`. |
| `ForwardedHeaders:ForwardLimit` | `1` | Number of trusted proxy hops. |

Both this section and `RateLimiting` below are read from the **host** configuration
(`appsettings.json` + environment variables), not from `appsettings.local.json` — they are deployment
settings, and the middleware/limiters are wired before the Autofac container (which owns the
`appsettings.local.json` view) exists.

**With nothing declared the trust set is the framework default — loopback only.** That is the
fail-safe choice: it never trusts a forged header, but it also means that behind a *containerised*
proxy (whose address is not loopback) the forwarded headers are ignored and the per-IP limiters below
collapse into one global bucket. **An operator running the split deployment must declare the proxy**,
e.g. on the `api` service in `docker-compose.yml`:

```yaml
- ForwardedHeaders__KnownNetworks=172.16.0.0/12   # the compose bridge network
```

Narrow this to the proxy's own address (`ForwardedHeaders__KnownProxies=<nginx ip>`) where the
address is stable. Do **not** widen it to a range that untrusted clients can originate from — anyone
inside it can spoof their client address. Note that publishing the API port on the host (`5100:8080`
in the shipped compose file) lets a client reach the API *around* nginx from the bridge network, so
keep that port unpublished in any deployment where the trust set covers it.

## Session cookie `Secure` is configuration-driven

The 7-day `proxytrace_session` JWT cookie (`Proxytrace.Api/Auth/SessionCookie.cs`) is `HttpOnly`,
`SameSite=Strict`, and `Secure` **from configuration** — never inferred from `Request.IsHttps`, which
is `false` on the plain-HTTP hop behind the TLS-terminating proxy and would strip `Secure` from every
HTTPS installation's cookie, leaking the full session token on any plaintext request the browser can
be induced to make.

- Default: **on** everywhere except the `Development` environment (which `dev.sh` and
  `launchSettings.json` set), where the SPA and API are plain `http://`.
- Override: `Authentication:SessionCookie:Secure`. Set it to `false` only for a deliberate
  plain-HTTP deployment on a host that is **not** `localhost` — browsers treat `http://localhost` as
  a secure context and accept `Secure` cookies there, so the local Docker/e2e/kiosk stacks
  (`http://localhost:5101`, `:5103`) work with the default.

## In-process auth/MFA/rate-limit state is single-instance by design

Several auth defenses keep their state **in process memory**, not in a shared store:

- **MFA challenge tickets** (`MfaChallengeService`) — the short-lived two-step-login tickets and their
  per-ticket failed-attempt cap (see [`docs/mfa.md`](mfa.md)).
- **SSE stream tickets** (`StreamTicketService`).
- **Per-IP rate limiters** (`AuthRateLimiterConfigurator` in `Proxytrace.Api/Program.cs` — the
  `auth-login`, `auth-reset` and `auth-mfa` fixed-window policies). They partition on
  `Connection.RemoteIpAddress`, so they are only genuinely *per-IP* once the forwarded-header trust
  set above is declared; otherwise every client shares one bucket, which both weakens brute-force
  protection and lets one noisy client exhaust the window for everyone. Limits are overridable under
  `RateLimiting:{Login,PasswordReset,Mfa}:{PermitLimit,WindowSeconds}`:

  | Policy | Default | Endpoints |
  |---|---|---|
  | `auth-login` | 30 / minute | `login`, `claim-legacy`, `signup`, `invites/by-token/{token}` |
  | `auth-reset` | 10 / 15 min | `forgot-password`, `reset-password` |
  | `auth-mfa` | 10 / 15 min | `mfa/verify` |

  There is **no per-account failed-attempt counter or lockout**; the `auth-login` window is the only
  bound on online password guessing, so it is sized to swallow a human fumbling a password (and a
  shared-NAT office signing in) while cutting an unthrottled attack by three-plus orders of magnitude.

This is correct for the **documented single-instance topology**: the API runs as exactly one replica
(both the split and kiosk deployment shapes run a single API process — see
[`../manual/admin/deployment.md`](../manual/admin/deployment.md)). It is **not** a bug. If you ever
scale the API horizontally these caps/limiters/tickets would partition per replica (each instance
enforces its own counters, and a ticket minted on one replica is unknown to another), weakening the
brute-force and attempt-cap guarantees. Scaling out is therefore **not supported as-is**; it would
require moving this state into the shared **Redis** the split deployment already runs (the event
broker), and is deliberately out of scope today. Do **not** silently add a second API replica behind a
load balancer without that work.

## Repository secret hygiene (gitleaks)

The repo is scanned for committed credentials with [gitleaks](https://github.com/gitleaks/gitleaks);
config lives in `.gitleaks.toml` (default rules + an allowlist of the fake fixture credentials that
are committed on purpose — the test-signed e2e/perf license JWT, demo-data keys, test strings).

- **Pre-commit hook** — `scripts/git-hooks/pre-commit` scans staged changes and blocks the commit on
  a finding. Enable once per clone with `./scripts/install-git-hooks.sh` (sets `core.hooksPath`);
  requires gitleaks on `PATH` (skips with a warning otherwise). One-off bypass:
  `GITLEAKS_SKIP=1 git commit ...`.
- **CI** — the `secrets` job in `.github/workflows/ci.yml` scans the full history on every push/PR
  and at the release gate.
- A finding is a real problem: remove the secret and rotate it. Only extend the `.gitleaks.toml`
  allowlist for deliberately committed fakes, never to silence a real credential.

## Code scanning & dependency updates

- **CodeQL** — `.github/workflows/codeql.yml` runs GitHub code scanning on every PR, push to
  master, and a weekly cron (so new query-pack releases surface findings without a commit).
  Languages: C# (build-mode `none` — no compilation), JavaScript/TypeScript, and GitHub Actions
  workflows. Alerts land in the repo's Security tab; a PR fails its CodeQL check when it
  introduces new alerts.
- **Dependabot** — `.github/dependabot.yml` opens weekly version-update PRs for nuget (`/`),
  npm (`frontend/`, `e2e/`, `manual/`), github-actions, and the three Dockerfiles. Minor+patch
  updates are grouped into one PR per ecosystem; majors arrive individually. Dependabot security
  updates (vulnerability-driven PRs) are enabled in the repo settings. Every Dependabot PR runs
  the normal ci + e2e gates.

## Out of scope

`StoredLicense` JWT is left plaintext: it is a signed license token, not a credential, so encrypting
it adds migration + decrypt-on-startup cost for no real secrecy gain. `User.PasswordHash` is already
hashed via `IPasswordService`. Key rotation / re-encryption tooling and a keyed-HMAC blind index are
deliberately not implemented.
