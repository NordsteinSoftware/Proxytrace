# Audit Log

The **Audit Log** is a durable, user-attributed record of significant actions taken in Proxytrace —
*who* did *what*, to *which* target, and *when*. Unlike the [Error Log](/admin/error-log) (which
captures backend exceptions), the audit log captures deliberate system actions for compliance and
operational review.

Admins open it from the admin-only **Settings** hub — open **Settings** from the sidebar, then choose
**Audit log** (direct link: `/settings/audit-log`). Project members get a project-scoped view from the
**Audit Log** entry in the main sidebar (`/audit-log`), showing only the **currently selected
project's** trail (see *Visibility* below).

## What gets recorded

Each entry captures:

- **Action** — the kind of action (see the list below).
- **Actor** — *who* performed it: the signed-in **user**, the owner of the **API key** used (for
  actions driven over the proxy / MCP), or the **system** for unattended work such as scheduled test
  runs.
- **Project** — the project the action belongs to, or *(global)* for instance-wide actions.
- **Target** — the type, id, and a human-readable label of the thing acted on.
- **Details** — optional action-specific context (e.g. the scopes granted to a new API key).
- **Outcome** — whether the action **succeeded** or **failed**. Most entries are successes; failed
  sign-ins, failed second-factor (MFA) attempts, and denied access are recorded as **failures** so
  abuse and probing are visible.
- **When** — when the action occurred.

The actions recorded today:

| Area | Actions |
| --- | --- |
| Authentication | Signed in, failed sign-in, signed out, first-admin setup, legacy account claimed |
| Users & access | User invited, invite revoked, sign-up, role changed, user deleted; **access denied** (a forbidden attempt to change something) |
| Test runs | Test run started (manual, scheduled, or via MCP); run cancelled; run, run group, or optimization request; run/run-group deleted |
| Scheduled runs | Schedule created, updated, deleted, or run now |
| API keys | API key minted, API key deleted |
| Projects | Project created, renamed, deleted; member added/removed |
| Agents | Agent endpoint changed, agent deleted, agent version moved |
| Scopes | Scope display name or description edited (scopes themselves are created by traffic and not audited) |
| Traces | Trace deleted |
| Test suites | Test suite created, updated, deleted; test case added, edited, or removed |
| Evaluators | Evaluator created, updated, deleted |
| Anomaly detectors | Anomaly detector created, updated, deleted |
| Providers | Provider created/updated/deleted, upstream provider key rotated, model endpoint created/updated/deleted |
| Optimization | Theory submitted, reset, or rejected; theory validated/invalidated by A/B run; proposal generated; proposal status changed (approved / rejected / adopted); proposal auto-adopted |
| Licensing | License set, license removed |
| Operations | Non-model data purged; secrets backfilled at rest |

::: info Authentication events are local-mode
Sign-in, sign-out, sign-up, setup, and legacy-claim events are recorded only when Proxytrace runs its
**built-in (local) authentication**. Under OIDC, interactive sign-in happens at your identity
provider, so those events live in the IdP's logs. The **first-time provisioning of a new OIDC user**
*is* recorded (as first-admin setup or sign-up); all other actions above are still audited in OIDC
mode.
:::

::: info Records survive deletion
Audit entries are independent snapshots — they keep the actor email, project id, and target label as
plain values. So a *"project deleted"* entry (and everything that happened in that project) **remains
in the log after the project is gone**.
:::

## Visibility

Who sees an entry depends on the viewer's role:

- **Admins** see the **entire** trail across all projects, including instance-wide *(global)* actions
  such as license and provider changes.
- **Project members** see only the entries for projects they belong to, and **not** global actions.

This is enforced both in the UI and in the underlying API (`/api/audit-log`), so a member can never
read another project's — or the instance's — audit trail.

## Using the page

- The list is newest-first and paginated. Use the **Per page** selector and the pager to navigate.
- The **action filter** narrows to a single kind of action.
- The **actor search** does a case-insensitive infix match on the actor's email.
- The **time-range picker** restricts the list to a window (relative quick ranges or an explicit
  From/To). Times are shown in your local timezone.
- Selecting a row opens a detail panel with the full actor, target, and details.

## Retention

The audit log is **lossless** — entries are never dropped to make room (there is no count cap). A
scheduled background service removes entries only once they exceed the retention window. Configure it
under the `AuditLogCleanup` section of `appsettings.json`:

```json
{
  "AuditLogCleanup": {
    "RetentionDurationDays": 365,
    "CleanupIntervalHours": 24
  }
}
```

| Setting | Default | Meaning |
| --- | --- | --- |
| `RetentionDurationDays` | `365` | Entries older than this are removed. |
| `CleanupIntervalHours` | `24` | How often the cleanup pass runs. |

See [Configuration](/admin/configuration) for how settings files and environment variables are
applied.
