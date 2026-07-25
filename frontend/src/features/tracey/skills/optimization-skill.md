---
name: optimize-agent
description: Theorize a concrete improvement to an agent and A/B-test it. Use when the user asks to optimize, improve, or tune an agent.
tools: submit_optimization_theory, get_agent_stats, list_suites, list_runs, get_run, get_run_failures, compare_runs, find_traces, get_trace, list_theories, await_actions
---

# Skill: Optimize an agent

You theorize ONE concrete, evidence-backed change to an agent, then submit it as an
**optimization theory**. The backend runs a baseline-vs-candidate A/B test in the background and
either promotes the theory to a reviewable proposal (it improved the pass rate) or rejects it.
You do not run the A/B test yourself — `submit_optimization_theory` kicks it off and the card
that tool renders shows the live result.

## Preconditions (check before theorizing)

1. **The agent exists.** The user names the agent ("the Returns agent"), so resolve that name to an
   id with `list_agents` FIRST, then `get_agent` with the matched id — never pass the typed name as
   `agentId` (it is a name, not an id, and 404s). If nothing matches, say so and stop; if several
   match, disambiguate with `ask_questions`. `list_agents` hides internal system agents by default,
   so if the user explicitly names one (the Tracey assistant, an evaluator), pass
   `includeSystem: true` so it shows up.
2. **The agent has a test suite.** Call `list_suites({ agentId })` with the target agent's id — it
   returns only that agent's suites (each row also carries `agentId`/`agentName`). A theory is
   validated against a suite, so this is required.
   - If there is **no** suite for the agent, explain that optimization needs a test suite to
     measure against, point them to create one, and stop. Do not submit a theory without a suite.
   - If there are several, ask the user which suite to validate against with `ask_questions`.

## Check what was already tried

Call `list_theories` for the agent FIRST. An idea that was already **Invalidated** must not be
re-submitted; a **Validated** one tells you what kind of change works for this agent. Mention
relevant prior attempts in your rationale.

## Ground the theory in evidence

Don't guess. Look at how the agent is actually doing before proposing a change. These are
investigative reads for YOUR reasoning — keep them silent (no `present`); the user doesn't need a
card for every lookup, just your grounded conclusion and the theory card at the end:

- `list_runs({ agentId })` → pick the latest completed run, then `get_run_failures` — the failing cases with
  each evaluator's verdict and reasoning. This is your primary evidence: read the actual
  responses and the reasoning, and name the failure pattern (wrong format? ignored constraint?
  missing knowledge? tone?).
- `compare_runs` — when there are two runs of the suite (e.g. before/after an earlier change),
  see exactly which cases moved. A regression cluster is evidence too.
- `find_traces` — search the agent's real captured calls (by text or HTTP status) when the suite
  alone doesn't explain the failure; `get_trace` one with `verbose: true` to read its whole
  conversation (all messages, tool calls, response) rather than just its metadata.
- `get_agent_stats` — token usage, cost, latency trends (last 30 days), for cost/latency-motivated
  changes (model switch).

Use what you find to choose ONE kind of change and to write a specific, honest rationale that
cites the evidence (e.g. "3 of 4 failing cases return prose where the evaluator expects JSON").

## Pick exactly one change kind

| Kind | When | What you author |
|------|------|-----------------|
| **System prompt** | Failures look like missing instructions, wrong tone/format, ignored constraints. | A full rewritten system message that keeps what works and fixes the observed gap. |
| **Model switch** | Quality ceiling, or cost/latency is the problem and a different model would help. | The id of another `ModelEndpoint` in the project (find it via `get_agent` / `get_provider`). |
| **Tool update** | The agent's tool definitions are wrong/missing/poorly described. | The full proposed tool list (name, description, JSON-schema parameters). |

Prefer the smallest change that addresses the evidence. One theory = one change.

## Submit the theory

Call `submit_optimization_theory` with:
- `agentId`, `suiteId` (from the steps above),
- `priority` (Low/Medium/High/Critical — judge by how strong the evidence is),
- a one-sentence `rationale` grounded in what you saw,
- `details` for the chosen kind:
  - System prompt → `{ kind: 'SystemPrompt', currentSystemMessage, proposedSystemMessage }`
    (read `currentSystemMessage` from the agent's `systemMessage` field returned by `get_agent` —
    never retype or guess it).
  - Model switch → `{ kind: 'ModelSwitchSeed', proposedEndpointId }`
  - Tool update → `{ kind: 'ToolUpdateSeed', proposedTools: [{ name, description, parametersJson }] }`

The tool is confirm-gated (the app asks the user to approve the A/B run). After it returns, the
theory card it renders streams the status: **Validating** while the A/B test runs, then either a
link to the new **proposal** (it won) or **rejected** (no improvement). Add at most one sentence
of context — the card shows the rest. If the submission comes back as a duplicate or quota error,
explain it plainly instead of retrying.

`submit_optimization_theory` returns an `awaitable` handle (`{ kind: "theory", id }`), and the
app forces your next step to be `await_actions([handle])` — you cannot end the turn before the
A/B validation finishes (Validated or Invalidated). Then tell the user the result — on a win,
mention the proposal it created (`resultingProposalId`). If it reports `timedOut`, say validation
is still running and to check back.

## Guardrails

- Submit ONE theory per request unless the user explicitly asks for several.
- Never invent ids, model names, or numbers — read them with the tools first.
- If you can't find evidence or a suite, say so; don't submit a baseless theory.
