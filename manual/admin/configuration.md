# Configuration

Proxytrace is configured through standard ASP.NET Core settings files in the
`Proxytrace.Api` project.

## Settings files

- `Proxytrace.Api/appsettings.json` — default configuration.
- `Proxytrace.Api/appsettings.development.json` — development overrides.

Environment variables and the usual ASP.NET Core configuration providers also apply and
override file values.

Licensing is configured separately, primarily through environment variables — see
[Licensing](/admin/licensing).

## Common settings

### Database connection string

Persistent storage is PostgreSQL only. Set it under `ConnectionStrings:Default`:

```json
{
  "ConnectionStrings": {
    "Default": "Host=localhost;Port=5432;Database=proxytrace;Username=proxytrace;Password=proxytrace"
  }
}
```

In kiosk mode (`Kiosk:Enabled=true`) the connection string is ignored and in-memory storage is
used instead. See [Database](/admin/database) for details.

### Frontend origin (CORS)

The API allows the frontend origin for CORS. By default it is `http://localhost:4201`;
override with `Frontend:AllowedOrigin`:

```json
{
  "Frontend": {
    "AllowedOrigin": "https://your-frontend-host"
  }
}
```

### Advertised ingestion proxy URL

The ingestion proxy runs as its own service with its own port (or hostname), so the web UI
cannot derive its address. `Proxy:PublicBaseUrl` is the base URL the UI advertises to users
as the OpenAI `base_url` — in the setup wizard, on the API-keys table, and behind the
"How to wire the proxy?" link on the traces page:

```json
{
  "Proxy": {
    "PublicBaseUrl": "http://localhost:5102"
  }
}
```

Set it to wherever your agents reach the proxy (scheme + host + port, no path). In the
Docker deployment this is the `PROXYTRACE_PROXY_PUBLIC_URL` variable in `.env`, defaulting
to `http://localhost:5102`; update it whenever you change `PROXY_PORT` or serve the proxy
behind a reverse proxy or its own domain. When unset, the UI falls back to its own origin —
which is only correct if your own reverse proxy routes ingestion paths
(`/{project}/openai/v1/…`) to the proxy service.

### Data directory (`PROXYTRACE_DATA_DIR`)

`PROXYTRACE_DATA_DIR` is the writable directory holding the state that must outlive the
container: the **Data Protection key ring** (which encrypts your stored provider API keys, TOTP/MFA
enrollments and the SMTP password), the session signing key, and the offline license-status cache.

The official images already default it (`/app/data` in the split shape, `/data/appdata` in the
single-container shape), and the supplied compose files mount a persistent volume there — so a
standard installation needs no action. **Every process that reads or writes secrets must point at
the same directory**: in the split shape the API *and* the ingestion proxy mount the shared
`appdata` volume, because the proxy decrypts the upstream provider key the API encrypted.

If you write your own deployment manifest (for example a Kubernetes Deployment) and leave the
variable unset — or point it at a path that is not persisted — the key ring lives only in memory.
The application still starts and looks healthy, but after every restart the encrypted secrets fail
to decrypt and are read back as unset, which shows up only as upstream `401`s from your provider
and rejected authenticator codes. To make that impossible to miss, an unset `PROXYTRACE_DATA_DIR`
is reported at **Critical** at startup, so it appears in [Error Log](/admin/error-log).

### Model pricing feeds

Proxytrace auto-fetches model prices when a provider is added (and when its **Reload models &
prices** button is used) — see [Providers & API Keys](/admin/providers-and-api-keys). The
source feeds are configurable under `Pricing`:

```json
{
  "Pricing": {
    "LiteLlmFeedUrl": "https://raw.githubusercontent.com/BerriAI/litellm/main/model_prices_and_context_window.json",
    "FxApiUrl": "https://api.frankfurter.app/latest",
    "RefreshIntervalHours": 1
  }
}
```

- `LiteLlmFeedUrl` — the LiteLLM model-price catalogue (USD) used for all providers (Azure providers prefer its `azure/<model>` entries).
- `FxApiUrl` — the ECB exchange-rate source used to convert USD prices to EUR.
- `RefreshIntervalHours` — how often the background service re-resolves prices for every provider's models (default `1`; minimum 1 hour).

All prices are stored as EUR per 1M tokens. A background service refreshes them on the configured
interval, and the **Reload models & prices** button triggers a refresh on demand.

### Update notifications

Proxytrace checks once a day whether a newer release is available and, if so, shows admins a
dismissible notice linking the release notes. The check polls the public GitHub releases feed
(`GET /api/updates` exposes the result to admins) and never sends any data beyond the request
itself; the only thing the feed's host observes is your server's IP address. Failures are
silent and never affect the application.

```json
{
  "Updates": {
    "Enabled": true,
    "ManifestUrl": "https://api.github.com/repos/SyntaktikEU/Proxytrace/releases/latest",
    "CheckIntervalHours": 24
  }
}
```

- `Enabled` — set `false` to disable the check entirely (no outbound requests, e.g. for
  air-gapped installs).
- `ManifestUrl` — endpoint returning the latest release in the GitHub `releases/latest` shape.
- `CheckIntervalHours` — poll interval (default `24`, minimum 1).

The check is automatically disabled in kiosk mode and for development builds. The running
version is also reported by `GET /api/config` (unauthenticated) and shown on the dashboard.

### Emergency password-reset link logging

When a user requests a password reset and email can't deliver it (no SMTP, or the send fails),
Proxytrace logs only a **redacted** warning by default — never the live reset link. The
`Authentication:EmergencyLogResetLink` flag is a break-glass switch for the one scenario where that
isn't enough: a **sole Admin locked out of an instance with no SMTP** (see
[User Management → Resetting a password](/admin/user-management#resetting-a-password)).

```json
{
  "Authentication": {
    "EmergencyLogResetLink": false
  }
}
```

- `false` (default) — the fallback log line carries only a non-reversible token reference and the
  expiry, so a log reader cannot take over the account.
- `true` — the **full** one-time reset link is written to the server log on the next reset request.

::: danger Turn it back off
While `EmergencyLogResetLink` is `true`, anyone who can read the server log within the link's 1-hour
lifetime can hijack the account. Enable it only to recover a locked-out admin, then set it back to
`false`.
:::

## Kiosk mode

Kiosk mode (`Kiosk:Enabled=true`) runs Proxytrace in-memory and auto-seeds a rich demo
dataset on startup — the "Showcase Project" with sample agents (including a deliberately
defective email-triage agent whose test suite has just regressed), two weeks of trace history
with statistics, anomaly-flagged outlier calls (token blow-ups, latency spikes, a runaway tool
loop, a prompt-cache collapse), live anomaly notifications produced by the real detection
engine, and a populated optimization loop: test suites, run history, theories in every
lifecycle state with A/B evidence, and proposals from draft through adopted. It is intended
for demos and walkthroughs.

Kiosk is single-process: it does **not** require Redis. Captured-call ingestion runs over an
in-process channel (`Messaging:Provider=InProcess`, forced automatically in kiosk regardless of
config), and the ingestion worker runs in-process to persist those calls into the in-memory demo
DB. The split/Redis transport is only for the standalone-proxy production deployment.

```json
{
  "Kiosk": {
    "Enabled": true
  }
}
```

### Interactive kiosk (real LLM endpoint)

By default, kiosk is **read-only**: the seeded providers carry no credentials, so you can
browse the demo dataset but cannot run test suites, generate optimization proposals, or use
Tracey chat. To switch kiosk into **interactive mode**, add a `Kiosk:Endpoint` section with a
real provider endpoint, API key, and model — typically in
`Proxytrace.Api/appsettings.local.json` so the secret stays out of source control:

```json
{
  "Kiosk": {
    "Enabled": true,
    "Endpoint": {
      "BaseUrl": "https://api.openai.com/v1",
      "ApiKey": "sk-...",
      "Model": "gpt-4o",
      "Kind": "OpenAi",
      "ProviderName": "Kiosk Provider",
      "InputTokenCost": 2.50,
      "OutputTokenCost": 10.00
    }
  }
}
```

When `Kiosk:Endpoint` is present, kiosk becomes fully read-write for a single user:

- **Run test suites and evaluations** against the demo agents (real model calls are made).
- **Generate optimization proposals** and inspect the Tracey-produced rationale.
- **Create, edit, and delete** agents, test suites, and providers — the full UI is unlocked.
- **Chat with Tracey** — the AI assistant becomes visible and usable. Each exchange is
  captured through the in-process ingestion pipeline and shows up as a new trace attributed
  to the Tracey system agent.
- **Accept and trace live LLM calls** — the kiosk mounts an OpenAI-compatible proxy at
  `/openai/v1` that forwards to the configured endpoint. Any OpenAI SDK client pointed at
  `http://<host>:5200/openai/v1` with the demo API key will have its calls captured as
  traces in real time. The bundled showcase stack uses this route automatically.

All data remains **in-memory only** and is lost when the process restarts. Interactive kiosk
is intended for a single user or a private hands-on demo — it is not designed for a shared
public instance.

The seeded model provider, model, and endpoint are created from the `Kiosk:Endpoint` values
and become the project's **system endpoint** powering all interactive features.

- `BaseUrl`, `ApiKey` and `Model` are **required** together. Leave **all three** blank (the showcase
  compose's env-less default) and the endpoint is treated as absent — the kiosk runs in read-only mode
  and the OpenAI proxy route is not mounted. Set **some but not all** of them and the API fails fast on
  startup with a clear error (an invalid `BaseUrl` or `Kind` fails fast too).
- `Kind` is one of `OpenAi` or `OpenAiCompatible` (default `OpenAi`).
- `ProviderName`, `InputTokenCost` and `OutputTokenCost` are optional. Token costs are
  **EUR per 1M tokens** (e.g. `2.50` for €2.50 per million input tokens). When omitted, the
  endpoint falls back to a small-model rate (0.15 in / 0.60 out) so traces still display a cost.

`Kiosk:DemoApiKey` controls the fixed ingestion key seeded in the demo project (default:
`pk-kiosk-demo`). The showcase stack's sample chat client uses this key to authenticate calls
through the in-process proxy. Override the default only if you need a custom key — and if you
do, set the same value on both the API and the sample client (the showcase `.env` handles this
via the single `KIOSK_DEMO_API_KEY` variable).

If `Kiosk:Endpoint` is omitted, the kiosk seeds the full demo dataset with credential-less
providers (LLM calls will not succeed); interactive features including Tracey stay hidden
since there is no real model to call.

## Demo data

Local dev mode does not auto-seed in every flow. Use the **`/setup`** page (or the setup
endpoint) to populate demo data into an empty database.

## Optimization theory validation

Every [optimization theory](/guide/optimization-theories) is validated by running its target
test suite with the proposed change applied — i.e. **real LLM calls that cost money and
time**. Because theories can be submitted by users, Tracey AI, and external API callers, the
validation pipeline is rate-limited so an open submission endpoint cannot run away with
spend:

- **Deduplication** — a theory identical to one already in flight, or to an already-decided
  proposal (until the 3-completed-group "fresh evidence" threshold), is suppressed before any
  run starts.
- **Per-project backlog cap** — validation runs one theory at a time, so the queue is what
  grows under load. Each project may have at most a fixed number of **in-flight** theories
  (queued *or* validating — currently **20**) at once. Submissions beyond that are rejected
  with HTTP `429 Too Many Requests` and should be retried once the backlog drains.

Validation runs are flagged as system runs, so they never recursively trigger further
optimization. Keep an eye on provider spend when many theories are submitted in a short
window.

## Security headers

The API emits a strict Content-Security-Policy and related headers on every response (the
nginx deployment sets equivalent headers). This is why the bundled manual is served from a
path the CSP explicitly allows — see [Deployment](/admin/deployment).

The policies default to safe values but can be overridden per environment under
`SecurityHeaders`. `ContentSecurityPolicy` applies to the app/API; `DocsContentSecurityPolicy`
applies to the bundled manual at `/docs` (which needs a slightly relaxed `script-src`):

```json
{
  "SecurityHeaders": {
    "ContentSecurityPolicy": "default-src 'self'; ...",
    "DocsContentSecurityPolicy": "default-src 'self'; script-src 'self' 'unsafe-inline'; ..."
  }
}
```

Empty values are rejected on startup.

## Running behind your own reverse proxy

The released all-in-one image needs nothing here: its bundled nginx and the API share a
container and talk over the loopback interface, which Proxytrace trusts out of the box. Two
things then work correctly on their own — the session cookie is marked `Secure`, and the
sign-in, password-reset and two-factor rate limits apply per client.

If you instead run the API **behind your own reverse proxy or load balancer**, tell Proxytrace
which hop to trust. Until you do, it ignores the `X-Forwarded-For` and `X-Forwarded-Proto`
headers your proxy sends, and every request looks like it came from the proxy itself — so the
per-client rate limits collapse into a single shared bucket that one caller can exhaust for
everyone.

| Setting | What it does |
|---|---|
| `ForwardedHeaders__KnownProxies` | The address of your proxy. Comma-separated for several. |
| `ForwardedHeaders__KnownNetworks` | A CIDR range to trust instead, e.g. `10.1.0.0/16`. |

```yaml
environment:
  - ForwardedHeaders__KnownProxies=10.1.0.7
```

::: danger Trust only the proxy itself
Only list addresses that **exclusively** belong to your proxy. Anything in the trusted set can
choose its own apparent client address, which lets it sidestep the rate limits entirely — so a
range that untrusted clients can also originate from is worse than leaving this unset. In
particular, if you publish the API's own port so it is reachable without going through the
proxy, do not trust the network that reaches it.
:::

If you terminate TLS at your proxy and deliberately serve Proxytrace over plain HTTP on a
non-`localhost` address, the session cookie's `Secure` flag will stop the browser from sending
it. Prefer fixing the TLS path; as a last resort set
`Authentication__SessionCookie__Secure=false`, which sends session tokens in the clear.

## Endpoint tuning

A few API limits are configurable; the defaults are sensible and rarely need changing. Invalid
values (e.g. a minimum above its maximum) fail fast on startup.

Search request validation bounds live under `Search:Requests`:

```json
{
  "Search": {
    "Requests": {
      "MinQueryLength": 2,
      "MaxQueryLength": 200,
      "MinSnippetLength": 20,
      "MaxSnippetLength": 1000
    }
  }
}
```

Dashboard statistics page sizes (used when a request omits `recentTraceCount`/`agentLimit`, and
as upper clamps) live under `Statistics`, along with the dashboard cache TTL:

```json
{
  "Statistics": {
    "DefaultRecentTraceCount": 6,
    "MaxRecentTraceCount": 50,
    "DefaultAgentLimit": 10,
    "MaxAgentLimit": 100,
    "DashboardCacheTtlSeconds": 10
  }
}
```

`DashboardCacheTtlSeconds` controls the server-side cache in front of the dashboard's statistics
queries: while an entry is fresh, every viewer of the same dashboard (same project and time range)
is served one shared result instead of each re-running the full set of aggregate queries. It bounds
how stale the dashboard can be — with the default of `10`, numbers may lag reality by up to ten
seconds. Set it to `0` to disable the cache entirely; values must stay below `30` (the dashboard's
poll interval), so data never appears frozen across two consecutive refreshes.

## Next step

Choose and configure a database — see [Database](/admin/database).
