<p align="center">
  <img src="frontend/public/icon.svg" width="80" alt="Proxytrace logo" />
</p>

<h1 align="center">Proxytrace</h1>

<p align="center">
  <strong>See every LLM call. Turn failures into tests. Prove the next version is better.</strong>
</p>

<p align="center">
  A self-hosted OpenAI-compatible proxy and web app for debugging, testing, and improving AI agents.
</p>

<p align="center">
  <a href="https://github.com/SyntaktikEU/Proxytrace/releases"><img alt="Latest release" src="https://img.shields.io/github/v/release/SyntaktikEU/Proxytrace?display_name=tag&sort=semver&style=for-the-badge&logo=github&logoColor=white&label=latest&labelColor=0a0f14&color=57c4d3"></a>
  <a href="https://github.com/SyntaktikEU/Proxytrace/actions/workflows/ci.yml"><img alt="Build status" src="https://img.shields.io/github/actions/workflow/status/SyntaktikEU/Proxytrace/ci.yml?branch=master&style=for-the-badge&logo=githubactions&logoColor=white&label=build&labelColor=0a0f14&color=5aba80"></a>
</p>

<p align="center">
  <a href="https://proxytrace.dev">Website</a> &nbsp;|&nbsp;
  <a href="manual/guide/getting-started.md">User guide</a> &nbsp;|&nbsp;
  <a href="manual/admin/installation.md">Installation</a> &nbsp;|&nbsp;
  <a href="CHANGELOG.md">Changelog</a>
</p>

Proxytrace sits between your agent and its LLM provider. It forwards the request, captures the
complete interaction, and makes it available as an inspectable trace. From there you can detect
anomalies, turn production failures into regression tests, compare agent versions, and automate the
workflow through its REST API or MCP server.

<img src="docs/assets/readme/hero-band.png" width="900" alt="Proxytrace dashboard showing live agent traffic, token throughput, errors, and recent traces" />

## Install in 30 seconds

You need Docker. One container includes the web UI, API, ingestion proxy, PostgreSQL, and Redis:

```bash
docker run -d --name proxytrace \
  -p 5101:80 -p 5102:8081 \
  -v proxytrace:/data \
  ghcr.io/proxytrace/proxytrace
```

1. Open [http://localhost:5101](http://localhost:5101).
2. Follow the setup wizard to create the administrator, provider, and first project.
3. Copy the project endpoint shown by the wizard.

The `proxytrace` volume contains the database and encryption keys. Keep it when replacing or
upgrading the container. For a production deployment with separate PostgreSQL and Redis containers,
use the [`proxytrace.zip` release artifact](https://github.com/SyntaktikEU/Proxytrace/releases/latest)
and follow the [deployment guide](manual/admin/installation.md#docker-compose).

## Route your first call

Keep your OpenAI SDK and provider key. Change the client's base URL to the project-scoped endpoint
from the setup wizard:

```diff
 client = OpenAI(
-    base_url="https://api.openai.com/v1",
+    base_url="http://localhost:5102/my-project/openai/v1",
     api_key=os.environ["OPENAI_API_KEY"],
 )
```

Send a request normally, then open **Traces** in Proxytrace. The full conversation, tool calls,
model parameters, token usage, latency, cache usage, and cost are captured automatically.

For deterministic agent attribution, add the optional `x-proxytrace-agent` header:

```python
client = OpenAI(
    base_url="http://localhost:5102/my-project/openai/v1",
    api_key=os.environ["OPENAI_API_KEY"],
    default_headers={"x-proxytrace-agent": "support-agent"},
)
```

The setup wizard generates ready-to-use Python, TypeScript, C#, and curl examples with your actual
project endpoint. See [Proxy setup](manual/guide/proxy-setup.md) for API keys, Azure OpenAI, custom
providers, and additional routing options.

<img src="docs/assets/readme/traces-live.gif" width="900" alt="New LLM calls appearing live in the Proxytrace traces table" />

## What you can do

| | Capability |
|---|---|
| **Inspect** | Read complete prompts, responses, tool round-trips, parameters, errors, and timings. |
| **Diagnose** | Find unusual token usage, latency, tool activity, cache behavior, and custom rule matches. |
| **Test** | Promote a real trace into a reusable test case and evaluate it with deterministic or LLM-based assertions. |
| **Compare** | Run the same suite against agent or model candidates and compare quality, speed, and cost. |
| **Improve** | Review optimization proposals backed by measured A/B test results. |
| **Automate** | Query traces, curate suites, and start runs through the REST API or project-scoped MCP server. |

Proxytrace supports OpenAI, Azure OpenAI, and OpenAI-compatible providers on `linux/amd64` and
`linux/arm64`. Feature availability varies by plan; see [proxytrace.dev](https://proxytrace.dev) for
the current feature matrix.

## See the workflow

### Turn a trace into a regression test

Promote a production interaction as-is or correct the expected answer before adding it to a suite.

<img src="docs/assets/readme/add-test.gif" width="900" alt="Converting a captured trace into a test case and adding it to a suite" />

### Compare candidates side by side

Run one suite against multiple agent or model candidates and inspect every score, failure, latency,
and cost difference.

<img src="docs/assets/readme/runs-matrix.png" width="900" alt="A test run comparing baseline and candidate results in a case-by-case matrix" />

### Review improvements backed by evidence

Optimization proposals connect a concrete prompt change to the failures that motivated it and the
A/B test that validated it.

<img src="docs/assets/readme/proposals.png" width="900" alt="An optimization proposal with a prompt diff, rationale, and A/B test evidence" />

## Learn more

- [Get started with the product](manual/guide/getting-started.md)
- [Configure the ingestion proxy](manual/guide/proxy-setup.md)
- [Connect Claude Code, Cursor, or another MCP client](manual/guide/mcp-server.md)
- [Install and operate Proxytrace](manual/admin/installation.md)
- [Report a bug or request a feature](https://github.com/SyntaktikEU/Proxytrace/issues)
- [Read the security policy](SECURITY.md)

## License

Source-available under the [Elastic License 2.0](LICENSE): you may use, copy, modify and
redistribute Proxytrace, but you may not offer it as a managed service to third parties,
circumvent its license-key functionality, or remove its licensing and copyright notices.
A free tier is built in; paid tiers unlock higher limits and additional features.
