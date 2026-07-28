# Providers & API Keys

Proxytrace routes captured traffic to upstream LLM providers and identifies clients with
its own API keys. This page covers the operator's view; for the client-side setup, see
[Proxy Setup](/guide/proxy-setup).

**Providers live in the admin-only Settings hub.** Open **Settings** from the sidebar, then
choose **Providers** under the *Workspace* group (direct link: `/settings/providers`). The whole
Settings area — and every provider/model/API-key write endpoint behind it — is restricted to
users with the **Admin** role.

## Model providers, models, and endpoints

- **Model Provider** — an upstream OpenAI-compatible API such as OpenAI.
- **Model** — a specific model offered by a provider.
- **Model Endpoint** — a **model paired with a provider**, plus per-token costs
  (`InputTokenCost`, `OutputTokenCost`). Endpoints can calculate the cost of a call's token
  usage, which feeds the cost figures shown on traces and runs.

Manage these from the **Providers** area of the UI.

**Deleting a model endpoint** hides it from the model list but keeps its captured calls and test
runs intact — their costs and history stay correct. (Deleting the whole **provider**, by contrast,
still removes its endpoints and their data.)

## Adding a provider — models & prices auto-load

When you add a provider, Proxytrace **discovers its models and fetches their prices
automatically**, so you usually start with a populated, priced model list. Each provider's
detail view shows its **Models** section (above its **API keys** section) with a per-model
input/output price. The **Reload models & prices** button re-runs discovery and **refreshes the
price of every model** (new and existing) from the catalogue, and a background service does the
same automatically on a configurable interval (default hourly — see
[Configuration](/admin/configuration)). Prices are managed entirely by Proxytrace — there is no
manual price entry.

The **Endpoint URL** accepts a plain host too — `https://` is assumed when you omit the
scheme, so `api.openai.com/v1` and `https://api.openai.com/v1` are equivalent (use an
explicit `http://` for a plain-HTTP endpoint, e.g. a local model server).

The upstream **endpoint** is shown in the provider header only when it differs from the
provider kind's default (for example, the canonical `https://api.openai.com/v1` is hidden; a
custom or self-hosted endpoint is shown). The model
list is **pulled from the provider** — there is no manual "add model" control; use reload (or wait
for the periodic refresh) to pick up newly deployed models.

### Viewing an upstream API key

The **Upstream API key** row shows the stored credential masked — a few leading and trailing
characters, enough to tell two keys apart and to confirm a rotation took effect.

Choosing **Reveal** (or **Copy**) fetches the key itself. Only administrators can do this, and
**every reveal is recorded in the audit log** as *Provider Key Revealed*, naming the administrator
and the provider. The key value itself is never written to the audit log.

The key is fetched only at the moment you ask for it. It is not part of the providers page's data,
so simply opening the page — or leaving it open — does not put any provider credential in front of
the browser.

### Rotating an upstream API key

To replace the credential Proxytrace uses when forwarding requests to a provider:

1. Open **Settings**, choose **Providers**, and select the provider.
2. In the **Upstream API key** row, choose **Edit**.
3. Enter the replacement key. The field starts empty so the existing secret is never copied into
   an editable form control.
4. Choose **Save**. Proxytrace tests the replacement against the provider's configured endpoint
   before storing it.

If the provider rejects the key or cannot be reached, Proxytrace leaves the existing key unchanged
and shows the reason. A successful provider response that reports no models is shown as a warning,
but the replacement is saved because a zero-model response can be valid. The rotation takes effect
immediately: the proxy resolves credentials on every request, so the next proxied call forwards the
replacement key, and the replaced key stops authenticating at the proxy at the same moment.

### Azure OpenAI

A provider whose endpoint host contains `azure.com` is treated as **Azure OpenAI**. For Azure
providers, discovery loads only the models you have actually **deployed** (it never falls back to
the full upstream model list).

### Where prices come from

All providers are priced from the **LiteLLM** model catalogue (quoted in USD) and converted to EUR
using **European Central Bank (ECB)** exchange rates. Azure providers prefer the catalogue's
`azure/<model>` entry, falling back to the bare model name. A model that isn't in the catalogue
loads without a price (shown as `—`).

Every stored price is normalised to **EUR per 1M tokens** and is refreshed from the catalogue on
each reload.

If either feed is unreachable — the catalogue **or** the exchange-rate feed — models still load, they
simply load without prices. Proxytrace makes **one** attempt against the failing feed and then pauses
for about half a minute before trying it again, instead of retrying once per discovered model. A
reload during a feed outage therefore finishes promptly rather than appearing to hang; run it again
once the feed is back to fill the prices in. A successfully fetched exchange rate is reused for the
rest of the day, so a healthy day needs a single rate lookup.

Operators can point the pricing feeds at different sources via the `Pricing` section of
`appsettings` (see [Configuration](/admin/configuration)): `Pricing:LiteLlmFeedUrl` and
`Pricing:FxApiUrl`.

## API keys

A Proxytrace **API Key** is the credential clients use against the OpenAI-compatible proxy.
Each key is tied to:

- a **Project** — so captured traffic lands in the right tenant, and
- a **Model Provider** — so the proxy knows which upstream to forward to.

The proxy also accepts the **upstream provider's own API key** as the inbound bearer, so
existing clients can migrate by changing only the base URL. The upstream key identifies only
the provider — the project is taken from the **project slug in the request path**
(`/{project}/openai/v1/…`, where the slug is derived from the project name). This means the
same upstream key can be reused across projects, disambiguated by the path. If the same string
is valid as both a Proxytrace key and an upstream key, the Proxytrace key wins.

Each key also carries explicit **capabilities** (least privilege), chosen when you create it:

- **Ingestion proxy** — authenticate clients at the OpenAI-compatible proxy (the classic use).
- **MCP read** — read the key's project over the [MCP server](/guide/mcp-server) (`list_*`/`get_*` tools).
- **MCP write** — additionally curate suites, start/cancel runs and change proposals over MCP.
- **REST API read** — read the key's project over the REST API (`/api/*` `GET` requests), so a service
  can call the API directly with a scoped key instead of a long-lived user login.
- **REST API write** — additionally create and change data over the REST API (`POST`/`PUT`/`PATCH`/
  `DELETE`). A REST key acts as its owner and, like an MCP key, can never reach admin-only endpoints.

A REST key is confined to its own project, so list endpoints return that project's rows whether or
not you pass an optional `projectId` — there is no need to repeat the project on every call, and a
key can never widen its reach by omitting it. Endpoints without a project filter at all, such as
`GET /api/test-runs`, are scoped the same way. The confinement holds for the projects
themselves: `GET /api/projects` lists only the key's project, and reading any other project (or its
members) answers `404`, regardless of what the key's owner could see when signed in.

A key works only on the surfaces it was granted: an ingestion-only key cannot drive MCP or the REST API,
an MCP-only key cannot proxy LLM traffic or drive REST, and a REST key cannot drive MCP. Keys issued
before these capabilities existed are **ingestion-only**.

### Issuing a key

1. Open **Providers**.
2. Select the provider the key should route to.
3. Tick the **capabilities** the key needs (Ingestion proxy is on by default; add MCP read/write to let
   an external agent use the [MCP server](/guide/mcp-server), or REST API read/write to let a service
   drive `/api/*` directly).
4. Choose the **owner** — the user every MCP call made with the key is attributed to. Leave it as
   *Yourself (creator)* or assign the key to a specific teammate.
5. Generate the key. The **full key is shown once, right after creation** — copy it then and
   distribute it to the client team. Treat it as a secret.

Clients then set this key plus the proxy base URL in their OpenAI-compatible client — see
[Proxy Setup](/guide/proxy-setup).

::: tip Secrets are protected at rest
Inbound API keys are stored as one-way hashes, so Proxytrace cannot show a key again after it is
created — the key list shows only a short, non-secret prefix to help you identify each key. If a key
is lost, revoke it and issue a new one. Upstream provider credentials are stored **encrypted** (they
must be replayed to the provider). Existing keys and credentials are protected automatically when you
upgrade; nothing changes for clients already using a key.
:::

## Project system endpoint

Each **Project** references one **system endpoint** — the model endpoint used by built-in
system agents (for example, agent-name generation and optimizers). Configure it so those
internal features have a model to call.
