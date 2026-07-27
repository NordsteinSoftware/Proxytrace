# Proxy Setup

Proxytrace captures traffic by acting as an **OpenAI-compatible proxy**. Your agent keeps
talking to what looks like the OpenAI API; Proxytrace records the interaction and forwards
it to the real provider.

**Ingesting** simply means routing your agent's existing OpenAI calls through Proxytrace.
You change **two things** in your client and nothing else:

1. **Base URL** — point it at the Proxytrace proxy endpoint (below) instead of the provider's.
2. **API key** — either a **Proxytrace-issued API key** or your **existing upstream provider
   key**. Proxytrace accepts both and maps the call to the right project and provider.

No SDK swap, no code changes beyond configuration.

## What the proxy endpoint is

The proxy endpoint is the **`base_url` your OpenAI client points at**. It has three parts —
the proxy **host**, your **project slug**, and the fixed `/openai/v1` suffix:

<ProxyEndpoint />

::: tip Where to find your real endpoint
The host above is filled in **automatically when you read this page inside a running
Proxytrace instance** (it reflects the operator's [configured proxy URL](/admin/configuration)).
You can also copy the exact, ready-to-use endpoint straight from the app:

- the **first-run setup wizard**, which ends with copy-paste quick-start snippets (Python,
  TypeScript, C#, curl) for your project;
- the **Providers → API keys** table, which lists the per-project endpoint next to each key;
- the **“How to wire the proxy?”** link on the Traces page while it's still empty.
:::

::: warning The proxy is not the web UI — mind the port
The ingestion proxy runs as its **own service on its own port**. In the standard Docker
deployment the UI is on port `5101` and the proxy on port `5102`; sending OpenAI calls to the
UI port returns `405 Not Allowed`. Always use the host the app advertises, not the address in
your browser's URL bar.
:::

## What you change

### Which key to use

- **Proxytrace-issued key (recommended)** — carries its own project, so attribution is
  automatic and the key can be revoked per client. The project segment in the URL is optional;
  if you include it, it must match the key's project.
- **Upstream provider key** — lets you migrate an existing app onto Proxytrace by changing
  only the base URL. The key identifies the upstream provider; the **project comes from the URL
  path** (see below), so you must include the project segment. If the same upstream key is used
  by several projects, the path is what disambiguates them.
- **Collisions.** If the same string is valid as both a Proxytrace key and an upstream
  provider key, the Proxytrace key wins.

::: warning An upstream provider key is not scoped to a project
This is the deliberate trade-off that makes the "same key, several projects" setup above work, and
it is worth understanding before you rely on it.

When the bearer is the **upstream provider's own key**, Proxytrace has no way to tell which project
the call belongs to except the path segment — so whoever holds that key chooses it, by editing the
URL. Two consequences:

- **Attribution.** Traces can be recorded against any project, so per-project trace counts and costs
  are only as trustworthy as the holders of that key.
- **Blocking rules.** Detectors are configured per project, and the **chosen** project's rules are
  the ones applied. Pointing the path at a project with no detectors therefore means the call is not
  evaluated against any blocking rule.

If either matters — you rely on detectors to block prompts, or on per-project attribution for
chargeback — issue a **Proxytrace key per project** instead. A Proxytrace key carries its own
project, so the path cannot override it: supply a mismatched segment and the request is rejected
rather than re-attributed.
:::

### The project segment

The proxy base URL carries the project as the first path segment:

```
https://your-proxytrace-host/{project}/openai/v1
```

`{project}` is the **slug** of the project name — lower-cased, with non-alphanumeric characters
dropped and spaces turned into hyphens. For example, project **"Showcase Project"** →
`showcase-project`. The slug is derived automatically; there is nothing to configure.

The legacy `https://your-proxytrace-host/openai/v1` form (no project segment) still works for
Proxytrace-issued keys, since the key already carries its project.

## Create an API key

A Proxytrace **API Key** is tied to a **Project** and a **Model Provider**. Create one from
the Providers area of the UI (operators can also manage these — see
[Providers & API Keys](/admin/providers-and-api-keys)).

1. Open **Providers**.
2. Choose the upstream provider the key should route to (e.g. OpenAI).
3. Generate a key and copy it — treat it like a secret.

## Point your client at the proxy

Example using the OpenAI Python SDK:

```python
from openai import OpenAI

client = OpenAI(
    # Project slug + /openai/v1. Works with a Proxytrace key or your upstream provider key.
    base_url="https://your-proxytrace-host/showcase-project/openai/v1",
    api_key="pt-...",  # Proxytrace-issued key, or your upstream provider key
    # Optional but recommended: name your agent for deterministic attribution.
    default_headers={"x-proxytrace-agent": "my-agent"},
)

resp = client.chat.completions.create(
    model="gpt-4o-mini",
    messages=[{"role": "user", "content": "Hello"}],
)
```

The call runs exactly as before — but it is now captured. Open **Traces** to confirm it
arrives. See [Capturing Traces](/guide/capturing-traces).

### Name your agent with a header (recommended)

By default Proxytrace [detects agents](/guide/agents#how-agents-are-detected) by comparing
each call's system prompt and tool-set against known agents. For **deterministic** attribution,
send the **`x-proxytrace-agent`** header with your agent's name on every call (e.g. via your
SDK's default-headers option, as in the snippet above). The call then attaches directly to the
named agent — created on first sight — and the similarity matcher is skipped entirely, so
prompt or tool changes never split your traffic into a separate agent. See
[Naming an agent explicitly](/guide/agents#naming-an-agent-explicitly).

::: warning Requests can be blocked in real time
If the project has [blocking anomaly detectors](/guide/anomaly-dashboard#blocking-detectors)
configured (an Enterprise feature — e.g. a password-pattern guard), the proxy rejects a matching
request **before it reaches the provider** with HTTP `403` and an OpenAI-style error whose `code`
is `proxytrace_blocked`. The blocked call still appears as a flagged trace.
:::

## Header forwarding

The proxy is a transparent swap-in for your provider: **every header your client sends is
forwarded upstream unchanged** — `OpenAI-Beta`, `openai-organization`, idempotency keys, custom
tracing headers, anything your provider expects. Response headers come back the same way. Only
three groups never cross the proxy:

- **Proxytrace control headers** (`x-proxytrace-*`, e.g. `x-proxytrace-agent`) — these steer
  Proxytrace itself and are never sent to the provider.
- **Credentials** — your Proxytrace key (in `Authorization` or `api-key`) is replaced with the
  provider's real key. For Azure OpenAI upstreams the provider key is sent both as a bearer and
  in the `api-key` header, so either Azure auth style works.
- **Hop-by-hop and transport headers** (`Connection`, `Transfer-Encoding`, `Accept-Encoding`,
  `Host`, …) — these describe a single connection and are managed by the proxy on each hop.

## Listing models

`client.models.list()` (`GET /openai/v1/models`) is forwarded to your upstream provider, so
your client sees the provider's model list.

::: tip Azure OpenAI
Azure has no OpenAI-style `/models` route — its usable models are exposed as **deployments**.
Proxytrace detects an Azure upstream and lists its deployments instead, so `models.list()`
returns your deployment names (e.g. `gpt-4o-prod`) rather than an empty list.
:::

## Reaching other upstream endpoints

Some upstreams expose endpoints beyond the chat/completions API — most commonly a **health
check** (`/health`), but also anything else the provider serves at its host. Because your
client's base URL points at Proxytrace, a call to `https://your-proxytrace-host/{project}/health`
would otherwise have nowhere to go.

Proxytrace **transparently forwards any path under `/{project}/…` that is not part of the
`openai/v1` API** straight to the upstream provider's host, using the provider's real key. So
`GET /{project}/health` reaches the upstream's `/health`, `GET /{project}/v1/models` reaches its
`/v1/models`, and so on — no configuration needed.

These pass-through calls are **not captured as traces** (only the `openai/v1` API is). They still
require a valid key for the project, exactly like a traced call. If the upstream answers with a
redirect, Proxytrace relays the `3xx` (including its `Location`) back to your client verbatim
rather than following it server-side — `Location` values are not rewritten to proxy URLs.

::: warning Pass-through needs its own permission, and reaches the whole upstream host
Forwarding is deliberately unrestricted in *path*: **any** method and **any** path under
`/{project}/` is relayed to the provider's host with your real upstream key attached. That is what
makes `/health` and other provider-specific routes work without configuration, but it also means the
reach is the provider account's, not just its inference API — on a provider that serves account or
organization-management routes at the same host, a key that can pass through can reach those too.
Pass-through calls are **not evaluated by detectors, not traced, and not written to the audit log**,
by design, since Proxytrace does not interpret their payloads.

Because that reach differs in kind from capturing traffic, it is a **separate permission**:

- A Proxytrace API key must carry the **Upstream pass-through** capability. A key with only
  **Ingestion proxy** can capture LLM calls but gets `403 Forbidden` on any other path.
- **Keys created before this permission existed keep it**, so an existing `/health` setup is
  unaffected by upgrading. New keys must be granted it deliberately — choose it when creating the
  key under **Settings → Providers**.
- Calls authenticated with the **provider's own upstream key** are not restricted, since anyone
  holding that key can already call the provider directly.

Grant it only to clients that need it, issue keys per client so they can be revoked individually,
and if your provider offers scoped upstream keys, configure the provider in Proxytrace with one.
:::

::: tip Which upstream, which path
The target is the **host** of the project's provider — the same provider your LLM calls resolve
to. The path after `/{project}/` is forwarded to that host's root, so `/{project}/health` maps to
`https://<upstream-host>/health`, alongside (not under) the provider's `/v1` API path.
:::

## Next step

Once traffic flows, learn how [traces are captured and explored](/guide/capturing-traces).
