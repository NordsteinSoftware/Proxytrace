# Capturing Traces

A **trace** (internally an *Agent Call*) is one fully captured LLM interaction. Once your
client routes through the [proxy](/guide/proxy-setup), every call is recorded
automatically.

## What a trace contains

- The full **message history** sent to the model (system, user, assistant, tool messages).
- The **tool definitions** available to the agent for that call.
- **Model parameters** (model name, temperature, etc.).
- The **provider** the call was routed to.
- **Latency** and **token usage** (with per-token cost from the model endpoint).
- The model's **response**, including any tool requests.

## Exploring traces

Open **Traces** in the sidebar to browse captured calls. Real-time updates stream in via
Server-Sent Events, so new traces appear as your agents run — no refresh needed.

Until the first call arrives, the Traces page doubles as a quick-start: it shows this project's
OpenAI `base_url` and a copy-paste snippet (Python, TypeScript, C#, curl) so you can wire up the
[proxy](/guide/proxy-setup) without leaving the page.

![The Traces view: the capture timeline, the page-summary stat cards, and the table of captured calls.](/screenshots/traces/list.png)

Typical things you can do:

- Inspect a single trace end to end: the conversation, the tools offered, and the response.
- Filter and search across captured calls to find specific behaviors or regressions.
- Identify traces worth promoting into a benchmark — see
  [Test Suites & Cases](/guide/test-suites-and-cases).

### Filtering, search, and paging

- **Page summary** is the band of stat cards just above the table: number of traces on the
  current page, total tokens (with the input/output split), total cost, average latency (with
  its ± spread), and the error rate (share of non-2xx calls). It rolls up only the traces on
  the *current page* and recomputes as you page, filter, or change the time range.
- **Cached input.** Many providers serve part of a prompt from cache at a much lower rate.
  Wherever input tokens are shown — the page summary, each row's token cell, and the trace
  detail panel — Proxytrace adds a muted **"(N% cached)"** hint showing what share of the input
  was cache-served. The cached portion is priced at the provider's cheaper cached-input rate (when
  it's known), so the displayed **cost** already reflects the discount.
![The composable filter bar: an Agent chip and an "Any anomaly" chip stacked above the table, with the timeline and page summary following the filtered set.](/screenshots/traces/filters.png)

- **Filters** compose through the **+ Filter** button on the toolbar line, beside search and
  the time range. Pick a field, pick a value, and the filter appears as a removable chip on the
  row below; add as many as you need — they combine (a trace must match every chip). Click a
  chip to change its value or remove it; **Clear all** drops every chip at once. Available
  filters:
  - **Agent** — focus on one agent. Only agents that actually have traces in the selected
    time range are listed.
  - **Anomaly** — only flagged traces: **Any anomaly**, or a specific reason (high tokens,
    high latency, low cache hit, many tool calls, or a
    [custom detector](/guide/anomaly-dashboard#custom-anomaly-detectors) hit).
  - **Tool** — traces whose response requested a given tool, picked from the tools seen in
    this project. With an **Agent** filter active, the list narrows to just the tools that
    agent actually used, so you never pick a tool that would return nothing.
  - **Model** — model name contains the text you enter.
  - **Status** — the HTTP status class (2xx / 4xx / 5xx).
  - **Tokens** / **Latency** — numeric bounds (min, max, or both), e.g. every call over
    2,000 ms or above 10k total tokens.
  - **System traces** — a toggle rather than a value filter: turn it on to *include* traces
    from Proxytrace's own system agents (hidden by default). While on it shows as a
    **System traces** chip; remove the chip (or **Clear all**) to hide them again.
- **Search** matches anywhere inside captured message content (and the response), not just
  at the start of a word — searching `efund` finds a trace mentioning `refund`. You can also
  search by model name or the short trace ID.
- **Per page** lets you choose how many traces to show at once (20, 50, 100, or 200). The
  total trace count for the current filter is shown alongside the pager.

### Sorting the table

Click a column header to sort the table by that column: **Latency**, **Tokens**, **Tools**
(tool-call count), **Cached** (cache-hit rate), or **Time**. The first click sorts descending
(largest first — the slowest call, the heaviest token bill); clicking the active column again
flips the direction. An arrow on the header shows the active sort, which is remembered in your
browser. Sorting is server-side, so "slowest call in the window" means across *all* matching
traces, not just the visible page. While sorted by anything other than Time, multi-turn
conversations are shown as individual calls rather than grouped rows — grouping only makes
sense in time order.

### The timeline

![The traces timeline strip — each bar's height is the trace count for that time slice, with failed calls marked in red along the bottom.](/screenshots/traces/timeline.png)

Above the table a **timeline strip** plots how many traces were captured over the selected
range, so you can spot ingestion hotspots at a glance. Each bar's height is the trace count
for that slice of time; the **red** portion at the bottom marks failed calls (HTTP errors),
making error spikes easy to find. A **time axis** along the bottom labels the window, and
hovering a bar shows its exact time, count, and error count.

- **Drag across the timeline** to zoom into that window. The view re-spans to your selection —
  the bars redraw at higher resolution *and* the table narrows to the same range, so you can
  drill straight into a spike.
- **Scroll up** over the timeline to zoom in toward the cursor; **scroll down** to step back
  out one level (each zoom-in is remembered, so you can drill in repeatedly and reverse out).
- **Click a bar** to focus its time bucket directly.

If you zoom into a window with no traces, the strip stays put (showing *"No traces in this
range"*) so you can always scroll back out.
- The timeline reflects the **same filters** as the table — search, the time range, and every
  filter chip (including **System traces**) — so its shape always matches what you're looking at.

### The time-range picker

The **time-range picker** (the clock button in the toolbar) controls the window. It offers
one-click **quick ranges** (last 15 minutes through last 30 days) and a **custom range** with
explicit From/To date-times — the same picker used on the Error Log. Drag-zooming the timeline
simply sets a custom range, which the picker then shows. Use **Clear** (or the ✕) to return to
all-time.

When you **first** open Traces, the range automatically snaps to the smallest quick range that
still contains data, so you never land on an empty view. After that, your filter bar — time
range, search, the **System traces** setting, the column sort, and every filter chip — is
**remembered in your browser**, so it survives a refresh or navigating away and back. The
auto-snap only runs until you have a saved range; once you've picked one it is always restored.
The value filter chips are remembered **per project** (agents and tools belong to a project),
while the time range, search, sort, and the System traces setting are shared across projects.

### The trace detail panel

![The trace detail panel: latency, token and cost metrics above the Messages tab, which lays out the system, user, and assistant conversation.](/screenshots/traces/detail.png)

Click a trace to open its detail panel. The header leads with the **agent** that made the
call (click the name to jump to its [agent page](/guide/agents)), followed by the **model**
and the call's **HTTP status**. The line below shows the full **trace ID** with a **copy**
button that puts it on your clipboard, and the exact **capture time** (date and time, to the
second). The header also holds the panel's actions: **Ask Tracey** opens a question box where
you can ask something specific about this call (for example, “Why was the refund approved?”),
then hands the trace ID and your question to the [AI assistant](/guide/tracey). **Add test** promotes it into a
[test case](/guide/test-suites-and-cases).

If the call was [flagged as an outlier](/guide/outliers) or by a
[custom anomaly detector](/guide/anomaly-dashboard#custom-anomaly-detectors), an **Anomalous
trace** warning banner appears right below the header, so a problematic call is unmissable the
moment you open it. The banner lists the statistical reasons as chips (high latency, high token
count, …) and, for detector hits, names the detector, shows the trigger that matched, and quotes
the reviewer's reasoning.

The **Messages** tab lays out the conversation as a stack of expandable blocks:

- **System messages** and **tool calls** start **collapsed** to keep long traces scannable —
  click a block's header to expand it. User and assistant messages start expanded.
- **Hover any message block** to reveal a **copy** button that puts that block's content on
  your clipboard (the message text, or the tool call's name, arguments, and result).
- **Switch how a message body is displayed** with the format dropdown in the top-right of
  each expanded user/assistant/system message header:
  - **RAW** — the captured text exactly as sent, preserving whitespace.
  - **JSON** — pretty-printed and syntax-highlighted (for JSON payloads).
  - **Markdown** — rendered prose (headings, lists, tables, code, links).
  - **HTML** — the content rendered as HTML. Markup is always sanitized first (scripts,
    event handlers, and unsafe links are stripped); a warning appears when anything was
    removed.

  When you expand a message, Proxytrace **auto-picks** the most useful format — valid JSON
  opens as JSON, content with Markdown syntax opens as Markdown, everything else as RAW
  (HTML is opt-in). If a body isn't valid for the chosen format (e.g. JSON that won't parse),
  a warning shows and the raw text is displayed instead.

Close the panel by pressing `Esc` or clicking outside it. Use the arrow buttons (or the
`←`/`→` keys) to step to the previous/next trace.

While a trace is open, its ID is written to the page URL (`?trace=…`), so the link is
shareable and the same trace re-opens after a refresh — even if it isn't on the current page,
Proxytrace fetches it by ID.

### Multi-turn conversations

![A multi-turn conversation expanded in the traces table — a "turns" group opened into its individual Turn 1, Turn 2, and Turn 3 calls.](/screenshots/traces/conversation.png)

Calls that share a conversation are grouped into a single collapsible row. The group's
leading **turns** badge shows how many calls it contains; click the row to expand the
individual turns. The group's status column shows the exact code (e.g. `200`) when every
turn shares it, `2xx` when all turns succeeded with differing 2xx codes, and `mixed` when
the turns disagree.

## From traces to everything else

Traces are the raw material for the rest of Proxytrace:

- **Agents** are detected from traces — see [Agents](/guide/agents).
- **Test cases** are curated from traces — see
  [Test Suites & Cases](/guide/test-suites-and-cases).
- **Optimization proposals** are grounded in trace and evaluation data — see
  [Optimization Proposals](/guide/optimization-proposals).
