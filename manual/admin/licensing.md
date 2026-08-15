# Licensing

Proxytrace ships with a built-in licensing subsystem that gates premium features and
enforces tier limits. **You do not need a license to run Proxytrace** — it runs on the
**Free** tier out of the box.

## Source availability

Proxytrace's source code is public at
[github.com/NordsteinSoftware/Proxytrace](https://github.com/NordsteinSoftware/Proxytrace) under the
[Elastic License 2.0](https://github.com/NordsteinSoftware/Proxytrace/blob/master/LICENSE) —
you can audit exactly what your deployment runs. The license does not permit offering
Proxytrace as a managed service to third parties or circumventing the license-key
functionality described on this page.

## Free by default

With no license configured, Proxytrace starts on the **Free** tier with no further
configuration. The Free tier allows:

- **1** project
- **1** user
- **2** agents
- **2** test suites
- **14-day** trace retention
- **10,000** traces per month

The single seat means user management is effectively disabled on Free: once the first admin
account exists, further invites are rejected until you upgrade.

On startup you will see an informational log line confirming the tier, for example:

```
info: Proxytrace.Licensing.LicenseService[0]
      No license configured — running in Free tier.
```

To unlock higher limits and premium features, activate an Enterprise license key (see
[Where to buy](#where-to-buy)).

## Activating a license key

There are three ways to supply a license key; all accept the same token (a JWT):

1. **First-run setup wizard** — the Welcome step shows the active tier and offers a
   *"Have a license key?"* field. Paste the key, validate, and activate it before
   creating the first admin account.
2. **Settings → License** (admins) — paste a key, **Validate** it (a dry run that shows
   the tier, customer, and expiry it would activate), then **Activate**. The key is
   verified offline, stored in the database, and applied **immediately — no restart**.
   The same page shows the current tier, status, where the active license came from, and
   offers **Re-check now** (force a license-server check) and **Remove stored license**.
3. **`PROXYTRACE_LICENSE` environment variable** — set it in the deployment `.env` and
   restart. Useful for infrastructure-as-code setups.

### Precedence

A key activated in the UI is stored in the database and **takes precedence** over the
`PROXYTRACE_LICENSE` environment variable. Removing the stored key (Settings → License →
*Remove stored license*) falls back to the environment-supplied license, or the Free tier
when none is set.

## An invalid license never blocks startup

If a configured license fails validation — malformed, signed with an unknown key, issued
for the wrong audience, or already expired — Proxytrace **still starts**, running with
Free-tier entitlements and the license status **Invalid**. A red banner appears above the
top bar (and on the setup wizard's Welcome step) with the rejection reason and a link to
**Settings → License**, where an admin can paste a corrected key — no container restart
needed.

The log shows the reason:

```
warn: The configured license is invalid (Expired); running with Free-tier entitlements until it is corrected
```

The reason is one of `Malformed`, `BadSignature`, `WrongIssuer`, `WrongAudience`,
`Expired`, or `MissingClaim`.

::: tip
The token is trimmed before validation, so surrounding whitespace is tolerated, but
embedded newlines from a bad copy/paste are a common cause of `Malformed`. Paste the JWT as
a single unbroken line.
:::

## Environment variables

| Variable | Purpose | Default |
|---|---|---|
| `PROXYTRACE_LICENSE` | The license token (a JWT). Trimmed before use. Takes precedence over the `Licensing:License` appsettings value, but a key activated in the UI (stored in the database) overrides both. Unset (and no config value) → Free tier. | _unset_ |
| `PROXYTRACE_LICENSE_SERVER_URL` | Override the license server base URL. **Debug builds only** — Release builds ignore this and always use the default. | `https://license.proxytrace.dev` |
| `PROXYTRACE_LICENSE_PUBLIC_KEY` | Override the signature-verification public keys (comma-separated, base64). **Debug builds only** — Release builds use the keys baked into the binary. | _embedded keys_ |
| `PROXYTRACE_LICENSE_CACHE_PATH` | Path to the offline license-status cache file. | `$PROXYTRACE_DATA_DIR/license-cache.json` when `PROXYTRACE_DATA_DIR` is set (the Docker deployment's `appdata` volume), else `<LocalApplicationData>/proxytrace/license-cache.json` |

::: warning Release builds ignore the Debug-only overrides
`PROXYTRACE_LICENSE_SERVER_URL` and `PROXYTRACE_LICENSE_PUBLIC_KEY` exist only to support
local development and testing. In a Release build they are ignored entirely; the license
server URL and public keys are fixed at compile time.
:::

### appsettings

These values live in `Proxytrace.Api/appsettings.json` under the `Licensing` section:

```json
{
  "Licensing": {
    "License": "eyJ...",
    "ServerCheckEnabled": true,
    "CheckIntervalHours": 24,
    "OfflineGracePeriodDays": 7
  }
}
```

- `License` — the license token (a JWT), as an alternative to the `PROXYTRACE_LICENSE`
  environment variable. The environment variable **wins** when both are set. Intended mainly
  for local debugging and testing — set it in `appsettings.local.json` (which is
  git-ignored) rather than committing a token. Unset in both → Free tier.
- `ServerCheckEnabled` — **Debug builds only.** Whether the background service contacts the
  license server for periodic revocation/grace checks. When `false`, the startup license
  snapshot is kept as-is and **no network calls** are made — useful so local dev does not need
  the license server reachable. Defaults to `false` in Debug. **Release builds ignore this
  setting entirely and always perform the server check.**
- `CheckIntervalHours` — how often the running app re-validates the license against the
  license server. Default **24**.
- `OfflineGracePeriodDays` — how long the app keeps operating on its last-known-good license
  status when the license server is unreachable, before downgrading to Free. Default **7**.

## Tiers

| | Free | Enterprise |
|---|---|---|
| Projects | 1 | Unlimited |
| Users | 1 | Unlimited |
| Agents | 2 | Unlimited |
| Test suites | 2 | Unlimited |
| Traces / month | 10,000 | Unlimited |
| Trace retention | 14 days | 365 days |
| Optimization Proposals | — | Yes |
| Agentic Evaluators | — | Yes |
| Custom Evaluators | — | Yes |
| SSO (OIDC) | — | Yes |
| Audit Log | — | Yes |
| Tracey AI assistant | — | Yes |
| Scheduled test runs | — | Yes |
| Custom anomaly detectors | — | Yes |

::: tip Keep this table in sync
The authoritative limits and feature sets live in
`Proxytrace.Licensing/LicensePolicy.cs`. If that file changes, update this table to match.
:::

::: info Kiosk mode is always Enterprise
Kiosk mode ignores `PROXYTRACE_LICENSE` entirely and runs on a built-in, perpetual
**Enterprise** license, so the public demo can showcase every feature.
:::

When you exceed a Free-tier limit (for example, trying to create a second project, invite
a second user, or create a second test suite), the request is rejected with HTTP **402** and
the UI raises an **upgrade dialog** explaining which limit was hit, with a link to the
Enterprise plans. When you access a premium feature without a license that includes it, the
API responds with HTTP **402** as well and the same dialog appears framed as a feature gate.

The current tier is always visible as a chip in the top bar — a muted, sparkle-marked **Free**
chip (which links to the upgrade page) on an unlicensed install, or a crowned tier chip on a
licensed one: cyan when the license is active, amber while a re-check is pending. When the
monthly trace quota is exhausted, a banner appears above the top bar warning
that new traces are being dropped until the quota resets.

### What happens when the monthly trace limit is reached

The trace limit applies to the **whole installation**, but reaching it does not stop every project
from capturing.

Each project is measured against an equal share of the limit. Once the installation is at its cap,
only projects **above** their share stop being captured; a project that has used little of its share
carries on. So one busy project can no longer consume the month's allowance and take every other
project's tracing down with it.

When a project starts being throttled you get told, rather than having to notice traces going
missing:

- A **notification** is raised for that project ("Trace limit reached"), naming how many traces it
  has captured and what its share is.
- An entry appears in the **Error Log** recording that the installation reached its cap and that
  dropping has begun.

Proxied calls themselves keep working throughout — only the *recording* of them stops. Proxytrace
will not fail a customer's LLM request because of a billing limit, which is why the drop has to be
reported rather than returned as an error.

The **agent** limit is enforced differently because agents are discovered automatically from
captured traces rather than created by an explicit request. Once the agent limit is reached,
traces that would create a **new** agent are silently dropped (a warning is logged); traces
for agents that already exist continue to be captured normally. System agents (used
internally by optimizers and agentic evaluators) do not count toward the limit.

## Air-gapped and offline operation

Proxytrace contacts the license server (`https://license.proxytrace.dev`) **on startup** and
then **every 24 hours** (`CheckIntervalHours`) to confirm the license is still valid and has
not been revoked. Signature verification itself is fully offline — it never needs the
network — so the periodic check is only about revocation and updated entitlements.

If the license server cannot be reached, the app continues normally using its last
known-good status. This tolerance window is the **7-day offline grace period**
(`OfflineGracePeriodDays`):

| Time since last successful check | Behaviour |
|---|---|
| Within 7 days | Full Enterprise operation. |
| Grace period active | Full operation continues, and a **grace banner** is shown in the UI warning that the license has not been re-validated. |
| After 7 days | The app **silently downgrades to Free**. Premium features stop working and limits revert to Free values. |

If the server explicitly reports the license as **revoked**, the app downgrades to Free
immediately rather than waiting out the grace period.

The last-known-good status is persisted to the cache file
(`PROXYTRACE_LICENSE_CACHE_PATH`) so that a restart during an outage does not reset the grace
window. The grace window above applies to a **normal** license that simply cannot reach the
server. For a permanently disconnected install, ask sales for an **offline-only license**
(below) instead of operating within the grace period.

### Offline-only licenses

For genuinely air-gapped installs, Proxytrace supports an **offline-only license**: a key that
is **never** checked against the license server, so it needs no outbound connectivity at all.
Ask sales for one (see [Where to buy](#where-to-buy)) — it activates exactly like a normal key
(setup wizard, **Settings → License**, or `PROXYTRACE_LICENSE`).

How it differs from a normal key:

- **No server contact, ever.** The periodic 24-hour check and the offline grace window do not
  apply — an offline key does not degrade just because there is no network. The **Settings →
  License** page shows an "offline license" note and hides **Re-check now** (there is nothing to
  ask the server).
- **Expiry is the only thing that ends it.** With no server check, the key works until its
  built-in expiry date, then downgrades to Free. Offline keys are capped at **365 days** and are
  typically issued for shorter — plan to obtain a fresh key before the current one expires.
- **It cannot be revoked.** Because the install never calls home, a leaked offline key keeps
  working until it expires, and key rotation does not retire it early. Treat it as a sensitive
  bearer credential, and prefer the shortest lifetime that fits your renewal cadence.

::: tip Online vs offline
A normal (online) key is the default and is preferable when the install has even occasional
connectivity — it can be revoked and re-issued, and it picks up entitlement changes. Choose an
offline-only key only for installs that will never reach `https://license.proxytrace.dev`.
:::

## Where to buy

Enterprise licenses are available from the Proxytrace website. To purchase or to discuss
offline / air-gapped licensing, see the pricing page or contact sales:

- **Pricing:** <https://proxytrace.dev/#pricing>

## Key-rotation FAQ

Proxytrace verifies license signatures against a set of public keys embedded in the binary.
A license is accepted if **any** active key validates its signature. This lets us rotate
signing keys without invalidating licenses already in the field, using a staged
dual-key release playbook:

- **Release N — introduce the new key.** Ship a build that trusts **both** the current key
  and the next key. The new key is published but not yet used to sign anything. Existing
  licenses (signed by the old key) keep validating.
- **Release N+1 — sign with the new key.** New licenses are issued signed by the new key.
  Builds still trust both keys, so old and new licenses both validate during the transition.
- **Release N+2 — retire the old key.** Once enough time has passed for outstanding licenses
  to be re-issued, ship a build that trusts **only** the new key. The old key is dropped.

Because every release in the sequence trusts at least the key your license was signed with,
customers never experience a forced re-issue, and there is no flag day. If you operate in an
air-gapped environment, upgrade across these releases in order so you are never more than one
key generation behind.

**Q: I upgraded Proxytrace and now my license is rejected with `BadSignature`.**
Your license was likely signed with a key that the new build no longer trusts (you skipped
past Release N+2 of a rotation). Obtain a re-issued license — see
[Where to buy](#where-to-buy) — or, to keep running in the meantime, unset
`PROXYTRACE_LICENSE` to operate on the Free tier.

## Related

- [Configuration](/admin/configuration) — settings files and environment variables.
- [Installation](/admin/installation) — getting Proxytrace running.
