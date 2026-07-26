# Notifications

Open the **Notifications** inbox from the bell icon in the top bar — a badge shows the unread
count, and clicking it opens a panel listing alerts and updates for the current project in one
place. Today it is driven by **anomaly detection** — Proxytrace automatically watches your test
runs and raises an alert when something goes wrong — but the inbox is multi-purpose and will
carry other kinds of notifications over time.

![The notifications inbox open from the top-bar bell icon, listing anomaly alerts and a ready optimization proposal](/screenshots/notifications/inbox.png)

## Anomaly alerts

After every test run, Proxytrace compares the result against that suite's recent history and
raises a notification when it detects a **negative anomaly**:

- **Test run failed** — the run could not complete, most often because the model endpoint was
  unavailable. Flagged as **Critical**.
- **Pass-rate drop** — the suite's pass rate fell sharply compared with its recent baseline.
  Flagged **Warning**, or **Critical** for a large drop.
- **Latency increase** — average response latency rose well above the recent baseline.
  Flagged **Warning**.

The pass-rate and latency checks only fire once a suite has enough run history to form a
reliable baseline, so a brand-new suite won't produce false alarms — a failed run is always
reported.

## Cost budget alerts

If a [monthly cost budget](/guide/costs) is configured for the project, crossing one of its
thresholds raises a notification here:

- **Soft limit reached** — a **Warning**. Nothing is blocked; spend is simply running high.
- **Hard limit reached** — a **Critical**. Proxied calls for that scope are now being rejected
  until the month resets or an administrator raises the limit.

Each threshold alerts **once per calendar month**, so an ongoing overspend does not flood the
inbox — and the counters re-arm by themselves on the 1st.

## Working with notifications

- Notifications appear **live**, without refreshing the page.
- Each alert is colour-coded by **severity** (Info, Warning, Critical) and shows when it was
  raised.
- **Click a notification** to open its details (see below). Opening it also marks it read.
- **Mark as read** (the tick) keeps a notification in the list but clears it from the unread count.
- **Dismiss** (the ✕) removes it from the list.

An unread count is shown on the bell badge and the panel header so you can see at a glance
whether anything needs attention. When there's nothing to report the panel shows a short
placeholder explaining where notifications will appear.

![Close-up of the notifications panel — severity-coded alerts, relative timestamps, and per-item mark-read and dismiss actions](/screenshots/notifications/inbox-panel.png)

## Notification details

The panel shows only the first two lines of a message, so click any notification to open its
**details** in a panel on the right. That's where the whole alert lives:

- The **full message** — anomaly alerts explain exactly what changed (which suite, how far the
  pass rate fell, which model got slower), and that rarely fits in the list.
- Its **kind**, current **status**, the **project** it belongs to, and when it was raised and last
  updated.
- A **summary of what it's about** — the test run, agent, proposal, or trace the alert points at,
  with a link straight to it. The summary is live: it shows the item's current status, not a
  snapshot from when the alert was raised.
- **Dismiss** in the panel header, and **‹ ›** to step through the rest of the inbox without
  going back to the bell.

![The notification details panel — a Critical anomaly's full message, its kind, status, project and timestamps, and a live summary of the failed test run it points at with a link to open it](/screenshots/notifications/detail-drawer.png)

For most alerts this panel *is* the record — an anomaly is not stored anywhere else — so the
details stay readable even when the thing they describe is gone. If the run, agent, proposal, or
trace has since been deleted, the summary says so plainly instead of linking nowhere.

Every notification also has its own **shareable link**. Opening the details puts
`?notification=…` in the address bar, so you can copy the URL to a colleague and it will open the
same panel for them (subject to their project access). Notification **emails** link to the
notification this way too, rather than dropping you on a list page.

## Scope

Notifications are scoped to the currently selected **project** (plus any system-wide
notifications). Switch projects from the sidebar selector to see another project's
notifications.

## Email notifications

When an operator has configured email, you can opt in to receive notification alerts by email
as well as seeing them in the in-app inbox. New accounts are set to *All* by default, so you may
start receiving emails as soon as an operator enables email — change the level or turn email off
from the account menu below. The in-app inbox always shows every notification regardless of this
setting.

Open the account menu (your avatar or initials in the top-right corner) and choose
**Notifications**. Pick one of three levels — a short hint below the selector spells out what
each delivers:

- **All** (default) — every notification, including informational ones, is emailed.
- **Critical** — only the most urgent alerts are emailed.
- **None** — no email alerts; you'll still see everything in the in-app inbox.

The **Notifications** entry in the account menu only appears once the operator has enabled and
configured email for the installation. If you do not see it, email delivery has not been set up
yet — contact your administrator.

::: tip Delivery scope
Email recipients for a notification are the project's members (for project-scoped
notifications) or all users (for global notifications), filtered to those who have opted in
and whose chosen severity threshold the notification meets. The operator can also set an
installation-wide minimum severity floor that applies before any per-user setting.
:::
