# Multi-Factor Authentication (TOTP)

How the optional TOTP (RFC 6238 authenticator-app) second factor works. Read this before touching
the two-step login, the MFA endpoints, or the enrollment/backup-code entities.

## Scope & decisions

- **TOTP only** (authenticator apps — Google Authenticator, Authy, 1Password). No email/SMS OTP.
- **Opt-in per user.** Each user enables MFA from their own account page; there is no org-wide
  enforcement toggle.
- **Backup codes** (10, single-use) are issued at activation for device-loss recovery.
- **Not license-gated** — available on every tier.
- **Local auth only.** All MFA endpoints are `[RequireLocalMode]`; OIDC delegates MFA to the IdP and
  the kiosk has no real sessions. "MFA is active for a user" is **derived** — a *confirmed*
  `UserTotpEnrollment` exists — not a flag on `IUser`.

## Design principle: a session is issued only after the full two-step flow

The password step never issues a session for an MFA account; it returns a short-lived challenge, and
only `mfa/verify` (after a valid code) issues the session. Because of this, **the session JWT carries
no `mfa_verified` claim and the auth middleware is untouched** — everything funnels through
`LoginService` and the MFA verify endpoint.

## Two-step login

1. `POST /api/auth/login` verifies the password (`LoginService.LoginAsync`). It returns a
   `LoginOutcome`: `LoginSucceeded` (no MFA → session issued) or `MfaRequired` (carries an in-memory
   challenge token). The controller shapes this into `LoginResponseDto`
   (`{ token | mfaRequired + mfaChallengeToken }`).
2. `POST /api/auth/mfa/verify` `{ challengeToken, code }` → `MfaService.VerifyChallengeAsync` consumes
   the ticket and accepts **either** a current TOTP code **or** an unused backup code, then issues the
   session (cookie + token) and audits `UserLoggedIn`. A failed attempt audits `MfaChallengeFailed`.

The **challenge ticket** is an in-memory, single-use, ~5-minute entry in `MfaChallengeService` (the
exact pattern of `StreamTicketService`), with a per-ticket failed-attempt cap (5). Losing tickets on
restart is harmless — the user just re-enters their password. The verify endpoint is also rate-limited
per IP (`auth-mfa` policy in `Program.cs`) because the 6-digit code space is small.

Password reset honors the same rule: `PasswordResetService.CompleteResetAsync` returns a `LoginOutcome`
too, so resetting the password of an MFA account still requires the second factor (a reset proves email
control, not device possession). The frontend `MfaChallengeForm` is shared by the login and
reset-password pages.

## Enrollment (authenticated, from the account page)

- `POST /api/auth/mfa/setup` → `MfaService.SetupAsync` creates/replaces a **pending** enrollment
  (`ConfirmedAt == null`) and returns `{ secret, otpAuthUri }`. The QR code is rendered **client-side**
  from the otpauth URI (`qrcode.react`) — no backend QR dependency. Returns 409 if MFA is already
  confirmed (disable first). The per-user unique index is the source of truth for "one enrollment per
  user": two concurrent setups race on it, and the loser catches the `23505` unique-violation,
  re-reads, and returns the pending enrollment that landed (so the response never 500s and the
  returned secret matches the stored row) — see `MfaService.SetupAsync`.
- `POST /api/auth/mfa/activate` `{ code }` → verifies the first code against the pending secret, sets
  `ConfirmedAt`, generates + returns the 10 backup codes once (transactional), audits `MfaEnabled`.
- `POST /api/auth/mfa/disable` `{ password }` → re-auth with the password, then remove the enrollment
  and all backup codes, audit `MfaDisabled`.
- `POST /api/users/{id}/mfa/disable` (admin) → lockout recovery; removes a user's MFA unconditionally,
  audits `MfaDisabled`. Mirrors the admin password-reset-link endpoint.

`GET /api/auth/me` carries `MfaEnabled`; the admin user list (`UserDto.MfaEnabled`) is populated from a
single `IUserTotpEnrollmentRepository.ListConfirmedUserIdsAsync` query.

## TOTP verification details (`TotpService`, `Otp.NET`)

- 160-bit Base32 secret; verification window ±1 time-step for clock drift.
- **Replay guard:** the enrollment stores `LastUsedStep`; a code whose matched step is ≤ the last used
  step is rejected even within its window. `Confirm`/`RecordUsedStep` advance it.

## Entities (the five-file pattern — see [`docs/domain-entities.md`](domain-entities.md))

- **`IUserTotpEnrollment`** — `User`, `Secret` (encrypted at rest via `ISecretProtector` in the storage
  mapper, like `ModelProvider.ApiKey`), `ConfirmedAt`, `LastUsedStep`. Unique index on `User`
  (one enrollment per user), FK cascade-deletes with the user.
- **`IMfaBackupCode`** — `User`, `CodeHash` (**salted PBKDF2 via `IPasswordService`**), `ConsumedAt`.
  10 per user, unique index on `CodeHash`, FK cascade.

### Backup codes are hashed like passwords, not like Proxytrace-generated secrets

`ISecretHasher` (unkeyed single-round SHA-256) is correct for the 256-bit CSPRNG secrets Proxytrace
mints itself, and **wrong** here. A backup code is 10 characters over a 32-symbol alphabet ≈ **50
bits** — deliberately small enough for a human to read off a card and type. Under one unsalted
single-round hash, 50 bits is within reach of a GPU rig, and because every installation hashed with
the same function, a single dump could be attacked against **every user's codes at once**. They
therefore go through `IPasswordService` (PBKDF2, per-code salt): the salt removes the shared work,
the iteration count removes the throughput.

Two consequences follow from the salt:

- **There is no hash to look a code up by.** `MfaService` loads the user's unconsumed codes and
  verifies them one at a time (bounded by the 10-code batch, and only reached after a TOTP code has
  already failed — never on a normal login). `IMfaBackupCodeRepository.FindByCodeHashAsync` is
  meaningful only for the legacy scheme.
- **Codes issued before the change cannot be upgraded in place.** The raw code is unrecoverable, so
  a stored hash could only be re-hashed at the moment it is redeemed — and redeeming consumes it.
  `MfaService` detects the legacy 64-char hex form and verifies against it, so existing codes keep
  working; they age out as they are used, or when the user re-enrolls. Every newly issued batch gets
  the stronger hash.

**Disable removes rows via per-row `RemoveAsync`, never a bulk `ExecuteDelete`** — the in-memory
provider (tests + kiosk) breaks on bulk deletes.

## Audit actions

`MfaEnabled`, `MfaDisabled`, `MfaChallengeFailed` (appended to `AuditAction`). A successful MFA login
emits the normal `UserLoggedIn`. See [`docs/audit-log.md`](audit-log.md).
