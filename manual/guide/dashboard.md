# Dashboard

The **Dashboard** is the home screen for a project. It summarizes the health and activity
of your agents at a glance and links out to the detailed views.

![The Proxytrace project dashboard — a full-width live pulse band charting per-minute call volume, the live trace feed, key metric tiles including token volume, trace counts, latency percentiles, queue depth and the evaluation pass-rate gauge, plus the agent fleet roster and the per-endpoint latency spectrum.](/screenshots/dashboard/overview.png)

## What you'll find

- **Live pulse** — a full-width activity line showing per-minute call volume over the last
  hour. It beats in real time as traces arrive, alongside live counters for traces per
  minute, tokens per second, and error rate.
- **Live trace feed** — the newest captured traces front and center, each row showing the
  agent, model, tokens, latency, and age, flashing briefly as new traffic arrives.
- **Key metrics** — token volume with a model split, trace counts, latency percentiles,
  ingestion queue depth, and the evaluation pass rate.
- **Agent fleet** — a roster of every detected agent, ranked by activity: each row shows the
  agent's endpoint, its own activity sparkline over the selected time range, token total and
  share of the project, trace count, and when it was last active. Click a row to open the
  agent, or use the cyan chip in the header — it shows how many optimization
  [proposals](/guide/optimization-proposals) are waiting for review.
- **Latency spectrum** — each endpoint's latency spread drawn as a min→max span with p50,
  p95, and p99 markers on a shared scale, so a slow endpoint stands out at a glance.
- **Time-range selector** — choose the window (last hour, 24 hours, 7 days, 30 days, or
  **all** time) that all metrics are computed over. New visitors start on **all** time;
  once you pick a window your choice is remembered in your browser, so it survives a
  refresh or navigating away and back.
- **Scope selector** — when the project has [scopes](/guide/scopes), a selector next to the
  clock narrows the whole dashboard — pulse, metrics, live feed, and agent fleet — to one use
  case. The evaluation pass rate is hidden while a scope is selected: test runs aren't tied to a
  scope, so a scoped pass rate would be misleading. Your choice is remembered per project.
- **Notifications** — the bell icon in the top bar opens an inbox of alerts and updates for
  the project, including automatically detected anomalies from your test runs. It's available
  on every page, not just the dashboard. See [Notifications](/guide/notifications).
- **Quick links** — jump into [Traces](/guide/capturing-traces),
  [Agents](/guide/agents), [Test Runs](/guide/running-tests), and
  [Proposals](/guide/optimization-proposals).

## Projects

Everything on the dashboard is scoped to the currently selected **project**. Switch
projects from the selector in the sidebar to see another project's agents, suites, runs,
and keys.
