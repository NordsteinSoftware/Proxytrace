# Notifications

This page covers the notification system architecture: the extensibility seam, the two built-in
channels, recipient resolution, the read API and detail drawer, the email settings store and
at-rest encryption, and the SMTP sender. See [`docs/sse-events.md`](sse-events.md) for the SSE
broadcaster and event payload shapes that the Dashboard channel uses.

## Architecture overview

```
AnomalyDetectionService (or any future producer)
        │
        ▼
INotificationService.NotifyAsync(NotificationRequest)
        │
        ├── [dedup check] FindActiveByTargetAsync → return early if duplicate
        ├── [create]      INotification.CreateNew → INotificationRepository.AddAsync
        │
        ├── DashboardNotificationChannel.DeliverAsync(INotification)  ─→ SSE broadcast
        └── EmailNotificationChannel.DeliverAsync(INotification)      ─→ recipients + SMTP send
```

**The notification *is* the record.** For a test-run anomaly there is no `Anomaly` entity — the
persisted `INotification` is the only trace it ever leaves. That is why the service, not a channel,
creates it: every channel then delivers the *same* persisted entity and can reference it by id
(which is what lets the email link to the notification itself).

### `INotificationChannel` — the extensibility seam

`Proxytrace.Application/Notifications/INotificationChannel.cs`

Every delivery channel implements this interface. Autofac registers all implementations and
`NotificationService` receives them as `IEnumerable<INotificationChannel>`. Adding a new channel
(webhook, Slack, etc.) requires only a new `INotificationChannel` registration — no changes to
the service or any caller. Channels only *deliver*; they never create or store the record.

```csharp
public interface INotificationChannel
{
    string Name { get; }
    Task DeliverAsync(INotification notification, CancellationToken cancellationToken = default);
}
```

### `NotificationService` — de-duplication, creation, fan-out

`Proxytrace.Application/Notifications/Internal/NotificationService.cs`

The single `NotifyAsync` entry point takes a producer-facing `NotificationRequest` (no id — the
producer knows nothing about persistence) and:

1. **De-duplicates** — if the request carries a `TargetKind`/`TargetId`, it checks whether an
   active notification already exists for that target. If one does, the request is dropped before
   anything is created or delivered.

2. **Creates** — builds the entity via the `INotification.CreateNew` factory delegate and persists
   it with `INotificationRepository.AddAsync`. Creation previously lived in
   `DashboardNotificationChannel`, which meant the email channel had no id to link to.

3. **Fan-outs** — calls `DeliverAsync(notification, …)` on each registered channel in sequence. A
   channel throwing is caught and logged; remaining channels still run. `OperationCanceledException`
   on the passed token is re-thrown to stop the loop cleanly.

### `DashboardNotificationChannel`

`Proxytrace.Application/Notifications/Internal/DashboardNotificationChannel.cs`

Publishes a `NotificationCreatedEvent` via `INotificationBroadcaster` so open browser sessions
receive the live SSE push. Persistence and de-duplication are both handled upstream by
`NotificationService`, so this channel holds nothing but the broadcaster.

See [`docs/sse-events.md`](sse-events.md) for the `notification-created` /
`notification-status-changed` event shapes and the `/api/notifications/stream` endpoint.

### `EmailNotificationChannel`

`Proxytrace.Application/Notifications/Internal/EmailNotificationChannel.cs`

Resolves recipients and sends one email per recipient with failure isolation:

1. Loads `EmailSettings` from `IEmailSettingsStore`. If settings are absent or `Enabled` is
   false, returns immediately.
2. Checks the operator-wide `MinSeverity` floor — if the notification's severity is below it,
   returns.
3. Resolves candidates:
   - **Project-scoped** (`request.ProjectId` is set): loads the project and uses its `Members`.
   - **Global** (`ProjectId` is null): loads all users via `IRepository<IUser>`.
4. Filters candidates to those whose `EmailNotificationsEnabled` is true, whose
   `EmailNotificationMinSeverity` the notification's severity meets, and who have an email
   address.
5. Builds a multipart HTML+text email (title/message HTML-encoded; a "View details" link added
   when `settings.AppBaseUrl` is set).
6. Calls `IEmailSender.SendAsync` per recipient. A send failure is logged and skipped; remaining
   recipients still receive their email.

The link is `{AppBaseUrl}/notifications/{notification.Id}` — the notification itself, not its
target. The target is a soft reference that may already be deleted, whereas the notification always
exists, so this link never dead-ends. `/notifications/:id` is a redirect route
(`app/AppRoutes.tsx`) onto `/dashboard?notification=<id>`, which opens the detail drawer.

> **Default thresholds.** A new user's `EmailNotificationMinSeverity` defaults to `Info` (surfaced as
> the **All** option in the account-menu control). Net email volume is bounded by the operator-level
> `EmailSettings.MinSeverity` floor (step 2), which the admin form defaults to `Warning` — so a
> default install emails only `Warning` + `Critical`. The per-user control exposes **All** /
> **Critical** / **None** only (there is no per-user `Warning`); see
> `frontend/src/components/layout/EmailNotificationMenuItems.tsx`.

## Read API and the detail drawer

`Proxytrace.Api/Controllers/NotificationsController.cs`

| Endpoint | Purpose |
|---|---|
| `GET /api/notifications` | List for a scope. Excludes `Dismissed`; non-admins never see global (null-project) rows. |
| `GET /api/notifications/{id}` | One notification. **Not** derivable from the list — see below. |
| `PATCH /api/notifications/{id}/read` | `Unread → Read`. **409** if the row is already `Dismissed`. |
| `PATCH /api/notifications/{id}/dismiss` | Terminal dismiss. |
| `GET /api/notifications/stream` | SSE (see [`sse-events.md`](sse-events.md)). |
| `POST /api/notifications/seed` | **Test-only** (`[TestOnlyEndpoint]`, 404 in real deployments). Writes a row directly, bypassing de-duplication, so the e2e suite can seed several rows per target and point one at a target that does not exist. |

The by-id endpoint exists because the list cannot always resolve a deep link: it hard-excludes
dismissed rows and hides global rows from non-admins. Both "missing" and "out of scope" return
**404, never 403** — a 403 would confirm the id exists. Access is the same
`CanAccessNotificationAsync` check the mutating endpoints use.

### The detail drawer (`frontend/src/features/notifications/`)

The bell popover (`NotificationsMenu.tsx`) mounts a right-side `Drawer` as a *sibling* of the
`Popover`, and which notification it shows lives in the URL (`?notification=<id>`, via
`useSelectedId`). Because the menu sits in the topbar, that deep link works on every route.

- Opening a row **closes the popover** — `Popover` content is `z-[80]` and `DetailPanel` is `z-50`,
  so the drawer would otherwise render behind it. Do not raise `DetailPanel`'s z-index; every
  drawer in the app shares it.
- The open notification is taken from the `useNotifications` list when present, otherwise fetched
  by id (`useNotification`, silent 404). Prev/next step through that list and are unwired when the
  open notification isn't in it (a deep-linked dismissed one).
- **Opening marks it read** (`useMarkReadOnOpen`) — an effect, because a deep link opens the drawer
  with no click to hang it off. The mutation silences 409 (a concurrent dismiss is an expected
  outcome, not an error).
- **Target preview** — `NotificationTargetPreview` switches on `TargetKind` to one child per kind,
  so each owns its own by-id query instead of a component switching hooks. `TargetId` is a
  deliberate soft reference (`INotification.TargetId`), so those queries use
  `silentStatuses: [404]`, `retry: false`, `throwOnError: false` and render a "no longer available"
  state with no CTA.
- Opening a notification also **clears any page-level drawer** (`?trace=`, `?error=`) in the same
  history replace. This drawer is global chrome painted over the page, and two live `DetailPanel`s
  both bind `document` keydown — Esc and the arrows would drive both, and their two
  `setSearchParams` updaters (each derived from the pre-update URL) would clobber one another.
  Master/detail `?id=` is a pane, not an overlay, so it is left alone.

### Why the topbar is defensive

Two rules in this feature exist because the bell renders in the masthead, which is a **sibling of
the router `Outlet`** — a route-level boundary structurally cannot catch a throw there:

- `notificationsMeta.ts`'s `TARGET_ROUTE` is a **`Partial<Record<…>>`**. `NotificationTargetKind` is
  a backend enum, and a member the frontend build doesn't know must degrade to "no link" rather than
  throw.
- `useNotifications` sets **`throwOnError: false`**, overriding the global default in
  `app/queryClient.ts`. A failing inbox degrades to an empty bell (the error still toasts).

`components/layout/Shell.tsx` now also wraps the rail, the masthead and the page area in their own
`ErrorBoundary`s (reset on `location.key`), so a future chrome bug degrades one region instead of
unmounting the React root. See `frontend/docs/BEST_PRACTICES.md` §9.1 — that backstop is for bugs;
a query that can fail routinely should still not rely on it.

## Email settings store

### `EmailSettings` record

`Proxytrace.Domain/Notifications/EmailSettings.cs`

An immutable record holding the operator SMTP configuration. `Password` is plaintext **in
memory**; it is encrypted at rest by the store. The store mirrors the `StoredLicense` single-row
pattern: at most one row exists in the database.

Fields: `Enabled`, `SmtpHost`, `SmtpPort`, `Security` (`SmtpSecurity` enum — maps to MailKit's
`SecureSocketOptions`), `Username`, `Password`, `FromAddress`, `FromName`, `AppBaseUrl`,
`MinSeverity`.

### `IEmailSettingsStore`

`Proxytrace.Application/Notifications/IEmailSettingsStore.cs`

```csharp
Task<EmailSettings?> GetAsync(CancellationToken cancellationToken = default);
Task SaveAsync(EmailSettings settings, CancellationToken cancellationToken = default);
```

`GetAsync` returns the password already decrypted. `SaveAsync` encrypts before writing.
Implemented by `Proxytrace.Storage/Internal/Entities/EmailSettings/EmailSettingsStore.cs`.

### `ISecretProtector` — at-rest encryption

`Proxytrace.Domain/Security/ISecretProtector.cs`

A reusable seam for reversible at-rest encryption, backed by ASP.NET Data Protection:

```csharp
public interface ISecretProtector
{
    string Protect(string plaintext);
    string Unprotect(string ciphertext);
}
```

`DataProtectionSecretProtector` (`Proxytrace.Infrastructure/Security/Internal/`) creates a
protector with purpose `"Proxytrace.Secrets.v1"`. The key ring is stored under
`PROXYTRACE_DATA_DIR` in container deployments — without a persistent volume the keys are
ephemeral and a restart invalidates any stored ciphertext.

`ISecretProtector` is a general seam. The email SMTP password is the first secret encrypted
through it. Retrofitting at-rest encryption to **other existing secrets** is tracked as
[GitHub issue #181](https://github.com/Proxytrace/Proxytrace/issues/181).

## SMTP sender

### `IEmailSender`

`Proxytrace.Application/Notifications/IEmailSender.cs`

```csharp
Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
```

`EmailMessage` is a simple record: `To`, `ToName?`, `Subject`, `HtmlBody`, `TextBody`.

### `SmtpEmailSender`

`Proxytrace.Application/Notifications/Internal/SmtpEmailSender.cs`

Implemented with **MailKit**. Reads `EmailSettings` per call via `IEmailSettingsStore`, so SMTP
configuration changes take effect immediately without a restart. Opens a fresh `SmtpClient`
connection per send (30 s connect timeout), authenticates if `Username` is set, sends, and
disconnects cleanly.

`SmtpSecurity` values map to MailKit's `SecureSocketOptions`:

| `SmtpSecurity` | `SecureSocketOptions` |
|---|---|
| `None` | `None` |
| `StartTls` | `StartTls` |
| `SslOnConnect` | `SslOnConnect` |
| `Auto` | `Auto` |

## Adding a new notification channel

1. Create a class implementing `INotificationChannel` in
   `Proxytrace.Application/Notifications/Internal/`.
2. Register it in `Proxytrace.Application/Module.cs` (alongside the existing Dashboard and Email
   registrations).
3. De-duplication and fan-out are handled by `NotificationService` — no changes needed there.
4. If the channel needs per-user opt-in fields, add them to the `IUser` domain interface and the
   `User` entity (following the `EmailNotificationsEnabled` / `EmailNotificationMinSeverity`
   pattern added in `Proxytrace.Storage/Migrations/20260621070146_AddUserEmailNotificationPreferences`).
5. Update the user manual (`manual/guide/notifications.md`) and, if it requires operator config,
   add an admin page under `manual/admin/`.
