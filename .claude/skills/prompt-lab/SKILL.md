---
name: prompt-lab
description: Verify an agent's behavior against the live upstream model after changing its system prompt, skills, or tool descriptions. Fires scenarios at the real model with the agent's real prompt and tools, A/Bs the working copy against the committed version, and reports what actually changed. Use this whenever a prompt is edited or reviewed — Tracey's system prompt (frontend/src/features/tracey/tracey-prompt.ts), one of her skills or tool descriptions, or a sample-client demo agent (support / travel / code / data) — and whenever the user asks whether a prompt change worked, wants to test/try/verify/sanity-check an agent's behavior, wonders if an edit made an agent worse, more verbose, chattier, less compliant, or wants to see how an agent responds to something before shipping it. Reach for it even when the user just says "does this still work?" about prompt-shaped code, because the only honest answer comes from asking the model.
---

# Prompt Lab

A prompt is code whose compiler is a language model. You cannot read a diff and know what it did —
you have to run it. This lab runs an agent's **real** prompt and **real** tools against the **live**
upstream model configured in the repo-root `.env`, then hands you transcripts to read.

The default is an A/B: every scenario runs twice, once against the prompt as committed and once
against your working copy, because "is this good?" is unanswerable while "did this change what the
model does, and in the direction I wanted?" is answerable in a minute.

## Before you run

The lab talks to the kiosk LLM endpoint — the same provider the showcase demos on:

- `KIOSK_LLM_BASE_URL`, `KIOSK_LLM_API_KEY`, `KIOSK_LLM_MODEL` must be set in `<repo>/.env`
  (copy `kiosk.env.example` if it is missing). Real calls, real money — keep runs tight while you
  iterate and widen once you are close.
- Tracey needs `frontend/node_modules` installed and her generated manual index present
  (`cd frontend && npm run gen:docs` — it is gitignored, so a fresh clone has none).
- No Docker, no database, no login. Tracey's data tools answer from fixtures.

## The loop

**1. Name the clause under test.** Read the diff and say what behavior it is supposed to change —
"replies should stop narrating between tool calls", "the refund policy should hold under emotional
pressure". A change you cannot phrase as an observable behavior cannot be verified here; say so
rather than running scenarios that cannot see it.

**2. Pick or write the probes.** Check `scenarios/tracey.json` and `scenarios/sample-client.json`
for one that already provokes that behavior. If none does, add one — a scenario is a situation
where your clause decides the answer, plus an `intent` line saying which clause and what you expect.
Probes that no plausible prompt could fail teach nothing; probes that put the clause under real
pressure (the user pushes back, the tempting shortcut is right there) teach a lot.

**3. Run it.** Start narrow — one or two scenarios — so the feedback comes back in a minute:

```bash
node .claude/skills/prompt-lab/scripts/run.mjs --agent tracey --only run-triage,skill-dispatch
```

It prints the path to `report.md` and exits. Full flag list: `run.mjs --help`.

**4. Read the transcripts and judge them.** The report is written for reading, not for scoring:
each scenario shows both variants' tool calls, tool results and final answer, with an at-a-glance
table of steps / tools / word count. There are no pass-fail verdicts in it on purpose — the
judgment is yours, and the transcript is the evidence for it.

Look for:

- **The clause you named.** Did the behavior move, and in the direction you intended?
- **Collateral.** The interesting failures are elsewhere: a brevity rule that also stripped the
  caveat, a policy rule that now refuses legitimate refunds, a skill that stopped loading.
- **Mechanical tells.** Text emitted mid-tool-loop is rendered as `💬 text emitted mid-tool-loop` —
  for Tracey that is a prompt-rule violation, and it is the single most reliable regression signal.
  Word counts and tool sequences sit in the at-a-glance table.
- **⚠️ no fixture** warnings — that branch ran against an empty world, so whatever it "found" there
  is an artifact of the harness, not of the prompt. Add the fixture and rerun before drawing a
  conclusion.

**5. Report honestly.** Say what changed, quote the lines that show it, and state your confidence.
One run at temperature 0 is evidence, not proof: models drift between calls, and a single differing
word is noise. When a call is close, `--repeat 3` and see whether the difference survives.

## Commands

```bash
# Default: A/B the working copy against HEAD, all scenarios for the agent
node .claude/skills/prompt-lab/scripts/run.mjs --agent tracey

# Did an already-committed change do what its message claims?
node .claude/skills/prompt-lab/scripts/run.mjs --agent tracey --baseline HEAD~1

# A sample-client demo agent (support | travel | code | data)
node .claude/skills/prompt-lab/scripts/run.mjs --agent support --only refund-trick

# Single variant, no A/B — exploring current behavior rather than a change
node .claude/skills/prompt-lab/scripts/run.mjs --agent tracey --baseline none

# Stability check on a close call
node .claude/skills/prompt-lab/scripts/run.mjs --agent tracey --only run-triage --repeat 3
```

Useful flags: `--model <id>` (does the change hold on another model?), `--temperature default`
(leave sampling to the provider), `--max-steps`, `--scenarios <path>`, `--fixtures <path>`,
`--out <dir>`. Reports land in `<repo>/.prompt-lab/<timestamp>/` (gitignored): `report.md` to read,
`raw.json` for the full untruncated data.

The baseline is skipped automatically — with a note in the report — when the working copy is
identical to the ref under the agent's watched paths. If you expected an A/B and got one variant,
your edit is already committed: use `--baseline HEAD~1`.

## Fixtures (Tracey only)

Tracey's data tools are stubbed from `fixtures/tracey.json`: one entry per tool name, shaped like
the digest that tool really returns (the `store(kind, full, digest)` second argument in
`frontend/src/features/tracey/tools/*.ts`). The entries describe one small coherent project, so a
multi-tool turn stays self-consistent — ids in the agent list reappear in the runs, the runs in the
case results. Keep that property when you extend it; a fixture world that contradicts itself
produces confused answers you will misread as prompt regressions.

**An entry can echo the call it is answering**, which is what keeps that property under writes. A
constant is fine for a read of a world that already exists; it is wrong the moment the model writes
something. A model that posts a *correction* and reads back `isCorrection: false`, or that probes two
suites and is handed the same one twice, concludes its own correct call did not land — it retries,
burns the step budget, and the report shows a failure the prompt never caused. The vocabulary
(resolved by `scripts/fixture-world.mjs`, JSON only — no functions in the fixture file):

| Shape | Does |
|---|---|
| `{"_byArg": "suiteId", "_cases": {"<id>": …}, "_default": …}` | branch on an argument; with no branch and no `_default` the answer is `{ notFound: <value> }`, exactly what the real by-id tools return |
| `{"_forEach": "cases", "_item": …, "_empty": …}` | one output entry per element of an array argument |
| `{"_like": "get_suite", …}` | resolve another entry against the same arguments, then override keys on top — one suite body, not five copies |
| `"$args.x"`, `"$item"`, `"$item.x"` | a value from the arguments or the current `_forEach` element |
| `"$has.args.x"`, `"$count.args.x"`, `"$index"` | was it sent · array length · position |
| `"$uuid"` | a minted id, stable for the same call so an A/B diff is never id noise |
| `"$$…"`, `_comment` | a literal `$`-leading string; a key that is dropped |

The `_byArg` default matters as much as the echo: a by-id read of something the world does not have
answers `notFound` rather than another entity's data, so "I probed a second suite" cannot read as
"there are two identical suites". After editing the file, run the resolver's self-check — it asserts
the shapes the scenarios lean on, including that a case created mid-scenario reaches a `fail` verdict
in the run it was added to and a `pass` in the A/B candidate run:

```bash
node --test .claude/skills/prompt-lab/scripts/fixture-world.test.mjs
```

`navigate`, `load_skill` and `search_docs` are never stubbed — they run their real implementations,
so skill loading and progressive tool disclosure behave exactly as in the app. Sample-client agents
need no fixtures at all: `chat.js` ships the tool simulator the demo itself runs on.

## What this is not

Faithful where it matters, and honest about the rest. The lab reuses Tracey's actual prompt, tool
schemas and skill markdown through Vite, and mirrors `TraceyTransport` (same Chat Completions model,
same per-step `activeTools` disclosure, same loop-until-answered stop condition). It differs in
ways worth knowing before you over-read a transcript:

- `generateText`, not `streamText` — streaming artifacts and the UI's rendering are out of scope.
- No forced `await_actions` after a pending awaitable, and no message windowing: long-conversation
  and long-poll behavior is not exercised here.
- Fixtures are not your data. Tool *selection* is real; what the tools return is invented.
- A fixture write echoes, it does not mutate: `add_to_suite` reports the cases it was handed, but
  the suite it returns is the one that was there before, and `unpassableCases` is never reported —
  that flag comes from the backend. `fixtures/tracey.json`'s `_readme` lists the rest.
- Sample-client multi-turn scenarios replay prior turns as plain assistant text (tool messages are
  dropped from the replayed history), so deep multi-turn tool chains drift from production.

For end-to-end behavior with real data and real UI, that is the Playwright suite (`run-e2e-tests`).
This lab answers a narrower question much faster: what does the model do with this prompt?

## When the change is bigger than a prompt

Two prompts in this repo have a second copy that must move with them:

- The sample-client agents' system prompts are duplicated **verbatim** in the API seeder
  (`Proxytrace.Api/.../CoreSeedScenario.cs`). Change one, change the other, or the seeded agent and
  the live demo drift and the showcase's adoption detection breaks.
- Tracey's prompt is captured from the wire into her stored agent, so a change to it shows up as a
  new agent version in the app — expected, not a bug.
