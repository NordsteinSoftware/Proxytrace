# Scopes

A **scope** groups the agents of a project that work together on one use case. A project
`translogica` might run a crew of **support agents** (triage, knowledge lookup, reply drafting)
next to a crew of **billing agents** — two scopes inside one project. Every trace records the
scope it was sent under, so you can look at one use case at a time.

Where a [session](/guide/sessions) groups the calls of *one run* and an agent is *one prompt*,
a scope is the *use case* around many agents and many runs.

Scopes are **auto-created**: the first trace that names a scope Proxytrace hasn't seen before
creates it. There is nothing to set up first, and scopes work on **every license tier**.

## Sending a scope

There are two ways to name the scope of a call. Use whichever fits your client.

### In the base URL

Add the scope as a second path segment between the project and `/openai/v1`:

```
https://your-proxytrace-host/translogica/support-agents/openai/v1
```

This is the simplest option when one client serves one use case: you configure it once, in the
base URL, and every call that client makes lands in the scope.

```python
from openai import OpenAI

client = OpenAI(
    base_url="https://your-proxytrace-host/translogica/support-agents/openai/v1",
    api_key="pt-...",
)
```

### With a header

Send the **`x-proxytrace-scope`** header when one client serves several use cases and you want
to choose per request:

```bash
curl https://your-proxytrace-host/translogica/openai/v1/chat/completions \
  -H "Authorization: Bearer pt-..." \
  -H "x-proxytrace-scope: billing-agents" \
  -H "Content-Type: application/json" \
  -d '{"model":"gpt-4o-mini","messages":[{"role":"user","content":"Hello"}]}'
```

When a call carries **both**, the header wins. Like every `x-proxytrace-*` header it is never
forwarded to your provider.

## How scope names work

- **Normalised like project slugs.** Names are lower-cased; spaces, hyphens and underscores
  become single hyphens; other punctuation is dropped. `Support Agents`, `support_agents` and
  `support-agents` all name the same scope.
- **Up to 64 characters.** Longer names are cut to 64.
- **Reserved:** `openai` and `v1` are part of the proxy's own URLs and never become scopes.
- **A bad scope never breaks a call.** If nothing usable is left after these rules, the call
  still goes through and is recorded — just without a scope.
- **Keep `/openai/v1` at the end** of a scoped base URL. Only the traced OpenAI routes understand
  the scope segment; anything else under your project is forwarded to the provider unchanged.

## Agents in several scopes

Scopes don't change how agents are [detected](/guide/agents#how-agents-are-detected). An agent
is a member of every scope it has sent traces in — a shared "translator" agent that both crews
call shows up in both scopes. Membership follows the traces: once an agent's last trace in a
scope ages out of trace retention (see [Licensing](/admin/licensing)), it drops out of that scope.

## Limits

A project can hold up to **200 scopes**. A call that names a new scope beyond that is still
recorded, just without a scope. If you hit the limit, you are most likely putting a per-user or
per-run value into the scope — use a [session](/guide/sessions) for that instead.
