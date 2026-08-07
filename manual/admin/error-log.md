# Error Log

The **Error Log** is an admin-only page that shows the latest application errors captured
across the backend — exception message, type, source, and full stacktrace — so operators can
diagnose failures without shelling into a container or wiring an external log stack.

It lives in the admin-only **Settings** hub — open **Settings** from the sidebar, then choose
**Error log** under the *Workspace* group (direct link: `/settings/error-log`). The whole
Settings area is visible only to users with the **Admin** role, and the underlying API
(`/api/error-log`) is likewise restricted to admins, because stacktraces can contain sensitive
runtime detail.

## What gets captured

Every log entry at **Error** or **Critical** severity is captured, from anywhere in the
backend — request handling, background services, the ingestion consumer, the optimizer, and so
on. This is broader than just failed HTTP requests: errors that never surface as a response are
still recorded.

Expected cancellations (client disconnect or shutdown, i.e. `OperationCanceledException` /
`TaskCanceledException`) are **not** recorded — they are normal, not faults. The same holds for a
client that hangs up in the middle of a streaming response (closing a browser tab that had live
updates open, for example): the failed write is recognised as a disconnect and skipped, so normal
navigation never fills this page with entries.

Each entry stores:

- **Message** — the log/exception message.
- **Level** — `Error` or `Critical`.
- **Source** — the logger category (typically the fully-qualified type that logged it).
- **Exception type** and **Stacktrace** — present when an exception was logged.
- **Timestamp** — when the error occurred.

Errors are persisted to the database, so they survive restarts and are shared across replicas.
Entries from Entity Framework Core and the error-log pipeline itself are deliberately excluded
to avoid feedback loops.

::: warning A failed background service is recorded here — and stays down until a restart
Proxytrace runs a number of background loops: trace ingestion, scheduled test runs, retention
cleanups, search indexing, the license check. If one of them fails unexpectedly it stops **on its
own** — the API and every other loop keep running, so the deployment stays up — and the failure is
recorded on this page with `BackgroundService failed` and the loop's stacktrace.

That is deliberate: losing one feature beats losing the whole API. But the loop does **not** come
back by itself, so whatever it was doing (say, consuming captured traces) stays stopped until the
API container is restarted. An entry like this is worth acting on rather than filing away: restart
the API, then check that what the loop feeds — new traces arriving, scheduled runs firing — has
resumed.
:::

::: tip API responses are sanitized — the Error Log is not
Outside development, an unexpected server error returns only a generic message to the client
(database conflicts surface as a friendly 409). The full exception message and stacktrace are
still captured here, so the Error Log is the place to diagnose what actually happened.
:::

::: info No file/line numbers in released containers
The official container images ship without debug symbols, so stacktraces show the full call
chain (namespaces, classes, methods) but no source file or line numbers. When reporting an
issue, include the Proxytrace version alongside the stacktrace — together they pinpoint the
location.
:::

## Using the page

- The list is newest-first and paginated. Use the **Per page** selector to change how many
  rows are shown at once, and the pager to move between pages.
- The **search box** does a case-insensitive infix match against both the **message** and the
  **stacktrace**, so you can find an error by any substring of either (typing starts filtering
  after two characters).
- The **All / Error / Critical** toggle filters by severity.
- The **time-range picker** (the clock button next to the search box) restricts the list to
  errors captured within a window. Open it to pick a relative **quick range** (Last 15
  minutes, hour, 6 hours, 24 hours, 7 days, 30 days) for a one-click filter, or enter an
  explicit **From**/**To** under *Custom range* and press **Apply** — either end may be left
  blank for an open-ended bound. Times are entered and shown in your local timezone. The
  **×** beside the button clears the filter back to *All time*.
- The **When** column shows the absolute date and time the error occurred.
- Selecting a row opens a detail panel with the full stacktrace (copyable).
- The page refetches when you return to the tab; there is no live stream.

::: tip Jump straight here from an error toast
When a request fails anywhere in the app, the red error toast in the corner is **clickable** for
admins. Selecting it opens this page with that exact error already selected, so you go straight
from the failure to its stacktrace. (The toast is only clickable for errors the backend captured,
and only for users who can see the Error Log.)
:::

## Automatic rotation & cleanup

A scheduled background service prunes the table automatically so it never grows unbounded:

- **Age-based rotation** — errors older than the retention window are deleted.
- **Count cap** — only the newest *N* errors are kept, bounding the table during an error storm.

Configure it under the `ErrorLogCleanup` section of `appsettings.json`:

```json
{
  "ErrorLogCleanup": {
    "RetentionDurationDays": 14,
    "CleanupIntervalHours": 6,
    "MaxRetained": 10000
  }
}
```

| Setting | Default | Meaning |
| --- | --- | --- |
| `RetentionDurationDays` | `14` | Errors older than this are removed. |
| `CleanupIntervalHours` | `6` | How often the cleanup pass runs. |
| `MaxRetained` | `10000` | Hard cap on the number of newest errors kept. |

See [Configuration](/admin/configuration) for how settings files and environment variables are
applied.
