# MCP Server

Proxytrace hosts a built-in [Model Context Protocol](https://modelcontextprotocol.io) (MCP) server, so
external AI agents — Claude Desktop, Cursor, your own scripts — can use Proxytrace's functionality
directly, much like the built-in [Tracey](./tracey) assistant does, but from outside the app.

The MCP server is reachable at:

```
https://<your-proxytrace-host>/mcp
```

## Authenticating

The MCP server authenticates with a **Proxytrace API key** — the same key type used for the ingestion
proxy, minted on the **Providers → API keys** page (see
[Providers & API Keys](../admin/providers-and-api-keys)). Send it as a bearer token:

```
Authorization: Bearer proxytrace-…
```

### Capabilities (scopes)

Each key grants explicit **capabilities**, chosen when it is created (least privilege):

| Capability | Lets the key… |
|------------|---------------|
| **Ingestion proxy** | authenticate clients at the ingestion proxy to capture LLM traffic |
| **MCP read** | read this project over the MCP server (the `list_*` / `get_*` tools) |
| **MCP write** | additionally curate suites, start/cancel runs and change proposals |

To use the MCP server a key needs **MCP read**; to let an agent make changes, also grant **MCP write**.
A key is only valid for the surfaces it was granted — an ingestion-only key cannot drive MCP, and an
MCP-only key cannot proxy LLM traffic. Keys created before this feature are **ingestion-only**; mint a
new key (or a fresh one) with the MCP capabilities to connect an agent.

### The key's project and owner

Every API key belongs to a **project** and an **owner** (a user). When an agent connects with a key,
the MCP server operates **in that key's project** — all tools see only that project's agents, traces,
suites, runs, proposals and statistics, and anything they create lands in that project — and every
action is **attributed to the key's owner**, so changes an agent makes are recorded as that user's. The
owner is chosen when the key is minted (an explicit user, or the admin who created it). To work across
several projects, use one key per project.

## Connecting a client

Most MCP clients accept a server URL plus headers. For a Claude Desktop / Cursor-style config:

```json
{
  "mcpServers": {
    "proxytrace": {
      "url": "https://your-proxytrace-host/mcp",
      "headers": { "Authorization": "Bearer proxytrace-…" }
    }
  }
}
```

To explore the tools interactively, point the [MCP Inspector](https://github.com/modelcontextprotocol/inspector)
at the same URL and header:

```
npx @modelcontextprotocol/inspector
```

## Available tools

All tools operate within the connecting key's project.

| Area | Tools |
|------|-------|
| Agents | `list_agents`, `get_agent` |
| Traces | `list_traces`, `get_trace` |
| Scopes | `list_scopes` |
| Test suites | `list_suites`, `get_suite`, `create_suite_from_traces`, `add_trace_to_suite` |
| Test runs | `list_test_runs`, `get_test_run`, `start_test_run`, `cancel_test_run`, `get_run_failures`, `compare_runs` |
| Proposals | `list_proposals`, `get_proposal`, `get_proposal_artifact`, `set_proposal_status` |
| Theories | `list_theories`, `get_theory`, `submit_theory` |
| Statistics | `get_dashboard`, `get_agent_overview` |

`list_traces` and `get_dashboard` take an optional `scope` — a [scope](./scopes)'s id or its key
(`support-agents`, or `Support Agents` — it is normalised the same way) — to look at one use case.

`add_trace_to_suite` takes an optional `expectedOutput`. Leave it off to promote the trace as-is (the
expected output is the response the agent actually gave). Provide it to record a **correction** — the
agent saw this input, and the right answer was *X* — which is what turns a rejected output into a
regression test. Either way the new test case keeps a link back to the trace it came from. With this,
an agent holding a single MCP write key can drive the whole optimization loop — capture, correct,
propose a fix, validate — without a separate REST credential.

## Workflows

The server also ships **workflows** — guided playbooks the MCP protocol calls *prompts*. Most clients
surface them as slash commands (e.g. `/optimize_agent`); selecting one walks the agent through the
right tools in order. They mirror the in-app [Tracey](./tracey) assistant's skills:

| Workflow | What it does |
|----------|--------------|
| `optimize_agent` | Gather evidence from runs and traces, then submit one A/B-tested optimization theory. |
| `curate_suite` | Build or grow a benchmark test suite from captured traces. |
| `run_tests` | Run a suite against its agent and review the failures. |
| `review_proposals` | Review open optimization proposals and approve / reject / mark adopted. |
| `project_insights` | Survey the project's usage, cost, pass rates and notable traces. |

The optimization and curation workflows need a key with **MCP write**; the read-only ones work with
**MCP read**.

::: warning Writes have real effects
Write tools act immediately — there is no confirmation step. In particular, `start_test_run` makes
real LLM calls against the agent's endpoint and **incurs cost**. The proposal and theory tools require
a license tier that includes optimization features.
:::
