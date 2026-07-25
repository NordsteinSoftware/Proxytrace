# Sessions

A **session** groups related traces that belong to one app run or user session — even when
they span several agents and several conversations. Where a
[conversation](/guide/capturing-traces#multi-turn-conversations) is a single thread of turns,
a session is the bigger picture around it: one checkout flow, one support case, one background
job. Send the same session key on every call your run makes and Proxytrace collects them into a
live, chronological timeline you can watch as it happens.

Sessions are **auto-created** — the first trace that arrives with a session key Proxytrace
hasn't seen before creates the session; every later trace with the same key joins it. There is
nothing to set up in the UI and no configuration; sessions work on **every license tier**.

## Sending a session key

Tag your calls with the **`x-proxytrace-session-id`** request header. Its value is any string
that identifies the run — a UUID you generate at the start, a job id, a checkout id. All calls
carrying the same value land in the same session.

```bash
curl https://your-proxytrace-host/showcase-project/openai/v1/chat/completions \
  -H "Authorization: Bearer pt-..." \
  -H "x-proxytrace-agent: my-agent" \
  -H "x-proxytrace-session-id: checkout-run-42" \
  -H "Content-Type: application/json" \
  -d '{"model":"gpt-4o-mini","messages":[{"role":"user","content":"Hello"}]}'
```

With an SDK, send it through the client's default-headers option, the same way you set
[`x-proxytrace-agent`](/guide/proxy-setup#name-your-agent-with-a-header-recommended):

```python
client = OpenAI(
    base_url="https://your-proxytrace-host/showcase-project/openai/v1",
    api_key="pt-...",
    default_headers={
        "x-proxytrace-agent": "my-agent",
        "x-proxytrace-session-id": "checkout-run-42",
    },
)
```

Like every `x-proxytrace-*` control header, the session key steers Proxytrace only and is
**never forwarded upstream** to your provider (see
[Header forwarding](/guide/proxy-setup#header-forwarding)).

A few details worth knowing:

- Keys are **opaque and case-sensitive** — `Run-42` and `run-42` are two different sessions.
- A key longer than **200 characters** is truncated to 200.
- An **empty or whitespace** value is ignored — the call is captured as a trace with no session.
- Blocked calls (rejected in real time by a
  [blocking anomaly detector](/guide/anomaly-dashboard#blocking-detectors)) still count toward
  their session.

## Sessions and conversations together

A session can contain several conversations. To model that, pair the session key with the
**`x-proxytrace-conversation-id`** header, which sets the conversation (thread) key — all calls
sharing it group into one thread, and threads that share a session key sit together under the
session.

```bash
# Two threads inside one session
-H "x-proxytrace-session-id: checkout-run-42" -H "x-proxytrace-conversation-id: search"
-H "x-proxytrace-session-id: checkout-run-42" -H "x-proxytrace-conversation-id: payment"
```

Send only the session key and Proxytrace still detects conversations on its own, exactly as it
always has.

::: warning Behavior change for existing `x-proxytrace-session-id` users
`x-proxytrace-session-id` **used to set the conversation (thread) key**. It now names the
broader *session*, and the new `x-proxytrace-conversation-id` header takes over thread grouping.

You don't need to change anything: when no `x-proxytrace-conversation-id` is sent, the session
key **also drives conversation grouping**, so a client that only sends `x-proxytrace-session-id`
keeps grouping its calls into conversations byte-for-byte as before — and now gets a session
view on top for free. Send the new conversation header only when you want one session to hold
several distinct threads.
:::

## The session view

Open a session's page (`/sessions/:sessionId`) to see all of its traces in one place. You get
there by clicking the **Session** link in a [trace's detail panel](/guide/capturing-traces#the-trace-detail-panel),
or from the traces **Session** filter (below).

The page header shows the session's **key**, when it was **first seen** and its **last
activity**, and running **trace** and **token** counters. A **Live** indicator appears while the
session is still active — that is, it saw activity within the last **5 minutes**.

Below the header, the session's traces are laid out as a **chronological timeline** — oldest
first, so new calls append at the bottom — across every agent and conversation in the session.
It uses the same trace rows and [detail panel](/guide/capturing-traces#the-trace-detail-panel)
as the Traces page, so you can drill into any call end to end. The whole page updates in **real
time** as new calls arrive: the counters climb and fresh traces stream in without a refresh.

## Filtering traces by session

On the [Traces page](/guide/capturing-traces#filtering-search-and-paging), add a **Session**
filter from the **+ Filter** button. Pick from the project's recent sessions (most recently
active first) and the trace table — and its timeline — narrow to that one session. It composes
with every other filter, search, and the time range, just like the other filter chips.

Each trace row and the trace detail panel also carry a **Session** badge/link when the trace
belongs to a session, so you can jump straight from a single call to its whole session and back.

## Next step

Sessions are built from your captured traffic — see
[Capturing Traces](/guide/capturing-traces) for how traces are recorded and explored, and
[Proxy Setup](/guide/proxy-setup) for wiring your client to the proxy.
