# Getting Started

Proxytrace is an AI agent observability platform. It sits between your agents and the LLM
provider as an **OpenAI-compatible proxy**, capturing every interaction, then lets you
curate those traces into benchmark test suites, evaluate agent versions, and act on
data-driven improvement proposals.

This guide is for people **using** Proxytrace through its web UI. If you need to install or
operate the platform, see the [Operations](/admin/installation) section.

## The workflow at a glance

1. **Route traffic through the proxy.** Change one base URL — no SDK or code changes. See
   [Proxy Setup](/guide/proxy-setup).
2. **Traces are captured automatically.** Every call records the full message history,
   tool definitions, model parameters, provider, latency, and response. See
   [Capturing Traces](/guide/capturing-traces).
3. **Agents are detected automatically.** Proxytrace extracts agent definitions (system
   prompt, tools, model, provider) from traces and versions them as they change. See
   [Agents](/guide/agents).
4. **Curate traces into test suites.** Turn representative traces into reproducible
   benchmarks — add one straight from its detail panel, or let Proxytrace propose the
   cases. See [Test Suites & Cases](/guide/test-suites-and-cases).
5. **Run structured evaluations.** Score each case with configurable
   [Evaluators](/guide/evaluators) and track results over time via
   [Running Tests](/guide/running-tests).
6. **Receive optimization proposals.** Review concrete prompt and tooling suggestions
   grounded in evidence. See [Optimization Proposals](/guide/optimization-proposals).

## Key concepts

| Concept | What it is |
|---|---|
| **Trace (Agent Call)** | One fully captured agent invocation. |
| **Agent** | A definition (system prompt, tools, model endpoint) extracted from traces. |
| **Test Suite / Case** | A curated, reproducible benchmark and its individual input/expected cases. |
| **Test Run** | Execution of a suite against an agent, producing per-case evaluations and metrics. |
| **Evaluator** | A scoring function — exact match, numeric, JSON schema, tool usage, or LLM-based. |
| **Optimization Proposal** | A data-driven recommendation grounded in test-run evidence. |
| **Model Endpoint** | A model paired with a provider, with per-token cost tracking. |
| **Project** | The tenancy unit grouping agents, suites, runs, and keys. |

## Supported screens

Proxytrace is a desktop-first tool, but the monitoring views — dashboard, traces, agents,
and test runs — also work on phones and small screens: navigation moves into a slide-out
menu (the icon top-left), trace lists show the essential columns, and lists open their
detail as a separate screen with a back button. Authoring flows (suite creation wizards,
the playgrounds, evaluator editing) are best used on a desktop browser.

## Resetting your password

Forgot your password? Click **Forgot password?** on the sign-in screen and enter your email.
Proxytrace sends a one-time reset link (valid for one hour) that lets you choose a new password and
signs you straight in. If outgoing email isn't configured on your instance, the link can't be
emailed — ask an administrator to generate a reset link for you instead (see
[User Management](/admin/user-management)).

This applies to local password accounts only. If your organization signs in through single sign-on
(SSO), password resets are handled by your identity provider, not Proxytrace.

## Next step

Start by [setting up the proxy](/guide/proxy-setup) so your agent traffic begins flowing
into Proxytrace.
