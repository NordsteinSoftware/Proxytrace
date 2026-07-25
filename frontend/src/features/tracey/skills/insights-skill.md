---
name: project-insights
description: Project-wide dashboard statistics, model providers, and captured traces. Load when the user asks for overall stats/usage/cost, about a provider, or to find or inspect captured traces.
tools: get_dashboard_stats, get_provider, find_traces, get_trace
---

# Skill: Project insights

Cross-cutting reads that aren't tied to a single agent or suite.

- `get_dashboard_stats` — aggregate project figures (calls, tokens, latency, cost, pass rate).
  Its digest also carries `byAgent` and `byModel` usage breakdowns (calls + tokens per agent /
  model) — chart usage comparisons straight from it (`show_chart` / `show_table`); do **not**
  list agents and fetch each one's stats. Lead with the component, then one sentence of insight.
  All figures (totals, per-model, and per-agent) exclude internal system agents (Tracey,
  evaluators) — pass `includeSystem: true` only if the user asks to see their usage too.
- `get_provider` — a single model provider's details (renders a clickable card when you pass
  `present: true`).
- `find_traces` — search the captured traces by agent, free text, or HTTP status (newest first).
  Use it to FIND calls — errors, a phrase the user remembers, an agent's recent activity. Silent
  while you're hunting; pass `present: true` only when the matching list itself is the answer. It
  hides traces of internal system agents (Tracey, evaluators) — pass `includeSystem: true` only
  when the user explicitly wants a system agent's calls.
- `get_trace` — a single captured trace (agent call). By default a metadata summary: model, status,
  token usage, latency, cost. Pass `verbose: true` to get the WHOLE trace instead — every request
  message (system, user, assistant, tool results) with its tool calls, the full response, the tool
  schema and the model parameters. Use it to inspect one specific call (often one `find_traces`
  surfaced); go verbose whenever the question is about what was actually said or why the call
  behaved that way, and stay on the summary for a "how big / how slow / how much" glance.
  (Pass `present: true` to render its clickable card.)

Never invent ids or numbers — fetch them first. Reads are silent by default; pass `present: true`
only on the call whose card is the answer, and lead with that one component.
