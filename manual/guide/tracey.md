# Tracey — the In-App AI Assistant

::: tip Enterprise feature
Tracey is part of the **Enterprise** tier. On the Free tier the **Tracey AI** sidebar entry
is locked and the page shows an upgrade prompt. See [Licensing](/admin/licensing).
:::

Tracey is a conversational assistant with her own full-page **Tracey AI** view. She
understands plain-language requests, reads your project's live state (agents, test suites,
runs, proposals, dashboard stats), navigates you around the UI, and can run a small set of
actions on your behalf.

Tracey is itself an AI agent running on your project's **system endpoint**. Her own
reasoning calls are routed back through the Proxytrace proxy, so every step she takes is
captured as a trace — she is the platform's first dogfood agent.

## Opening Tracey AI

Open **Tracey AI** from the sidebar (the top entry). The page is the chat itself; plots,
tables, entity cards, and question widgets render **inline in the conversation** as Tracey
produces them. A **conversation-history panel** can be opened on the right via the sidebar icon
in the chat header. Your conversations are remembered per project on this device — see
[Conversation history](#conversation-history) below.

## Asking questions

Type a request and press **Enter** (use **Shift+Enter** for a newline). Examples:

- "List my agents."
- "Plot the token usage per agent."
- "Which proposals are waiting for review?"
- "Take me to the test runs page."

Tracey fetches live data before answering. If a request is ambiguous (for example, several
agents match a name) or she needs a few decisions from you before acting, she asks with an
inline **questions widget** rather than guessing — see *Inline components* below.

You can also point her at one specific item by pasting its **id** — copy it from the page URL or
from a table and ask "look at trace `6339237b-0757-48ec-88bc-83233a3d29a8`" or "debug this run
`…`". She opens exactly that item instead of searching for it. Naming something instead
("the Returns agent") works too; she looks the name up first.

### Reading a whole trace

When you ask about a captured call, Tracey can read the **entire trace** — not just its headline
numbers. Ask her to explain, review, summarize, or debug a trace ("why did this call fail?", "what
was this agent told?", "summarize trace `…`") and she pulls the **full conversation**: every
message in the request (the system prompt, the user turns, the assistant's replies, tool calls and
their results), the model's final response, the tool schema the agent was offered, and the model
parameters (temperature, max tokens, …) the call ran with. That is what lets her quote the prompt,
point at the exact turn that went wrong, and turn a trace into a test case.

For a quick "how big, how slow, how much" question she stays on the trace's summary — model,
status, tokens, latency and cost — so the answer is fast and cheap. Either way the trace itself
renders as a card you can click through to the full detail view.

By default Tracey answers about **your own agents**. Proxytrace runs a few internal *system*
agents — Tracey herself and the evaluators that score your test runs — which make their own model
calls. She leaves these out of "list my agents", token-usage charts, recent test runs, and trace
searches so the numbers are about your work, not the platform's. Just ask if you want them
included — for example "show token usage including the Tracey agent" or "list system agents" — and
she'll add them back in.

## Answers from the manual

Tracey also uses **this manual** as a knowledge base. For how-to, what-is, and setup
questions about Proxytrace itself — "How do I set up the proxy?", "What does a numeric-match
evaluator do?", "How does agent versioning work?" — she searches the user guide and answers
from it, rather than guessing.

When her answer draws on the manual, she **cites the source as a clickable link** back to the
exact guide section, so you can open the full page and read more. The split is simple:
questions about *your data* (agents, runs, stats) are answered from live project state;
questions about *how the product works* are answered from the manual, with citations.

## The opening view

![Tracey's opening view: a centered "How can I help?" prompt with starter action chips above the message box.](/screenshots/tracey/opening-view.png)

When a conversation is empty, Tracey opens in a centered "initial view": the message box sits
toward the middle of the panel with **starter chips** above it. Send your first message and the
box glides down to the bottom and the chips disappear; start a **new conversation** and it
glides back up. This is purely presentational — your history still persists per project.

## Quick actions, chips, and the "/" menu

![The Tracey "/" menu, scrolled to the section break: curated quick actions (Run a suite, Review proposals, Optimize an agent, Diagnose an agent) sit above the Tools list (navigate, search_docs, load_skill).](/screenshots/tracey/menu.png)

In the opening view, **chips** surface common quick actions (e.g. *List agents*, *Plot token
usage*, *Improve failing runs*). Click one and its request is **sent straight away** — no extra
confirmation or send click needed. The chips show only while the conversation is empty; to tweak
a prompt before sending, pick the quick action from the **`/`** menu instead.

Type **`/`** at the start of a message to open a picker: curated quick actions on top and the
full list of tools below. Use **↑/↓** to move, **Enter** to choose, **Esc** to dismiss.
Choosing a quick action prefills its full prompt; choosing a tool inserts a `/tool_name`
slash command — send it as-is to invoke that tool directly.

Tracey **streams** her replies as they are generated and renders them as Markdown (headings,
lists, tables, and code blocks).

## Ask Tracey from anywhere

You don't have to start on the Tracey page. Wherever the app shows something worth
investigating, a cyan **⚡ Ask Tracey** button appears next to it. It opens Tracey AI in a fresh
conversation with the item context already filled in. On a trace, clicking the button first opens
a multiline question box so you can ask exactly what you need; submitting sends a message such as
`Trace ddee0986-af5e-48c3-8e5d-d846f3c5350a: why was the refund approved?`. Other Ask Tracey
buttons send their contextual request immediately:

- **A trace's detail drawer** — enter a focused question about the call; Tracey reads the full
  trace before answering.
- **An agent's header** — if the agent has suites with a low pass rate, Tracey digs into the
  failing runs and proposes an improvement to A/B-test; otherwise she reviews the agent's recent
  anomalies and results.
- **A test run's header** — Tracey fetches the run's failures, groups them by cause, and
  suggests fixes.
- **A theory's dossier** (Proposals review desk) — Tracey walks through the proposed change,
  its evidence and A/B result, and recommends accepting or rejecting.
- **The Anomalies page** — Tracey investigates the recent flagged calls across agents and
  recommends prevention.
- **The Dashboard** — Tracey gives a project health review.

Your current conversation isn't lost — it is archived to the conversation history (right-hand
rail) and the button starts a new one. The button only appears when Tracey is available
(Enterprise license, interactive mode, a project selected).

## Follow-up suggestions

After Tracey answers, two **follow-up suggestions** appear as clickable chips beneath her reply —
likely next messages drawn from what you just discussed (e.g. *Compare with the last run*, *Show
the failing cases*). Click one to send it straight away, or just keep typing your own message. The
suggestions clear as soon as you send anything, and they are not saved — reopening a past
conversation shows the messages but no suggestions.

While Tracey is thinking or replying, the **send** button turns into a **Stop** button — press it
to cancel the response. Stopping halts her generation immediately (the in-flight model call is
cancelled, not left running in the background) and ends the turn. If she was waiting on a
long-running action you started — a test run or an optimization theory — stopping only ends her
*waiting*; that action keeps running on the server, and you'll still find its result on the Runs or
Proposals page.

## How Tracey answers

Tracey is built to be **scanned, not read**. She answers in one short block — a bold headline, then
a few bullets or a small table, with status markers (✅ ❌ ⚠️ 🔴 🟢 ⏳) as verdicts — and she doesn't
narrate her own work. You won't see "let me check…" before a step or "that worked" after it,
because every tool call already shows its own row in the thread, and she won't repeat numbers that
are already on a card in front of you.

That holds however much work a request takes: a job spanning ten tool calls ends in the same short
answer as a one-line question. If you *want* the long version, ask her to explain or walk you
through it and she will.

## Inline components

Tracey renders rich UI **directly in the chat thread** rather than dumping raw numbers:

- **Charts** — bar, line, or area plots of your data, with gridlines, a max/min/avg/last
  summary, and hover for the exact value of any point.
- **Tables** — tabular comparisons; numeric columns are right-aligned for easy scanning, and a
  footer reports the row and column count.
- **Text cards** — longer markdown (fully rendered), JSON, or code. Code and JSON include a
  copy button and expand when long.
- **Entity cards** — a single agent, test suite, test run, proposal, provider, or trace shown
  as a card. Click a card to jump straight to that entity's page in the app.
- **List cards** — *List agents/suites/runs/proposals* render as a titled, counted list. Each
  row links to its entity, and **View all** (or **+N more**) opens the full page.
- **Stats cards** — *dashboard stats* and per-agent stats render as a grid of key figures
  (calls, tokens, latency, cost, pass rate) with an **Open** link to the matching page.
- **Questions widget** — when Tracey needs to ask you something, she shows a stepped widget
  that walks you through one question at a time (with a *Step 1 of N* indicator when there's
  more than one). Each question is a **vertical list of options** plus a static
  **"Something else…"** free-text field for a custom answer. Some questions allow **multiple**
  selections; others only one, and the free-text field is exclusive with the option picks.
  Click **Next** to move on (**Submit** on the last one); the widget then collapses into a
  read-only summary of your questions and answers, which are sent back as your next message.

## Tool calls

Tracey doesn't show a card for everything she looks up. When she's gathering information on the way
to an answer — listing your runs, checking a failure, comparing two runs — those reads appear as a
quiet **collapsed row** showing the **tool name**, its **execution duration**, and a **status** —
*Running* (pulsing), *Done*, or *Failed*. A full card appears only when that card *is* the answer
you asked to see, so the thread stays clean instead of stacking a card per step. Click any collapsed
row to expand it and inspect the tool's **Input** and its **Output** (or **Error** if it failed),
each with a copy button. (Navigation and other plumbing tools always show as this collapsed row.)

## Running actions

Beyond reading your data, Tracey can change state on your behalf:

| Action | What it does |
|--------|--------------|
| **Start a test run** | Runs a suite against an agent's endpoint, then shows a **live progress card** (see below). |
| **Cancel a test run** | Stops an in-progress run you started by mistake or no longer want. |
| **Build a suite from traces** | Creates a new test suite for an agent, seeded from captured traces you point her at (see *Curating suites from traces*). |
| **Add traces to a suite** | Adds more captured traces to an existing suite as new test cases. |
| **Edit a suite's cases** | Sets a case's expected answer, or removes a case. |
| **Create an evaluator** | Adds a scorer (an LLM judge or an exact/numeric/JSON-schema match) and attaches it to a suite she creates (see *Diagnosing an agent*). |
| **Approve / reject a proposal** | Sets an optimization proposal's status. |
| **Submit an optimization theory** | Theorizes a change to an agent and kicks off an A/B test (see *Optimizing an agent*). |

Actions run **immediately** — Tracey states what she's doing in the conversation rather than
pausing for a confirmation click, so watch the chat if you want to follow along.

Once a **test run** starts, the chat shows a **live run-progress card**: a progress bar that
fills as cases complete, a running case count, and a pass-rate badge — all streaming in real time
(*queued → running → completed*). When the run finishes the card settles on the final pass/fail
tally; a **View run** link opens the full run page at any point.

After starting a run (or submitting an optimization theory), Tracey **always waits for the result
and reacts in the same reply** — she comes back with an analysis once the run completes, rather
than leaving you to ask. While she waits, the **waiting card** shows each action's live status —
the suite and agent with a case-progress bar for a test run, the A/B phase for a theory — plus a
stopwatch of how long she's been waiting. When everything settles, the card turns into a result
summary: one row per action with its pass/fail tally and a verdict badge. If she's waiting on
several runs at once, she waits for all of them and summarizes together. Very long runs are
capped: if one hasn't finished in time the card marks it **Still running** so you can check back.

### Curating suites from traces

Proxytrace captures every LLM interaction as a trace — and Tracey can turn those real
interactions into a **benchmark test suite**, the same loop the product is built around. Point her
at the traces you care about and she'll do the curation:

- **"Find the failed calls for my support agent and make a suite from them."** Tracey searches the
  captured traces, then **creates a new suite** seeded from them — each trace becomes a test case,
  and the suite is runnable straight away.
- **"Add these traces to the regression suite."** She **adds** them to an existing suite as new
  cases.
- **"Set the expected answer for that case to …"** A captured response isn't always the *ideal*
  answer, so she can **set a case's expected output** — what the case is scored against — or
  **remove** a case that isn't useful.
- **"Which test cases are worth making from trace `8f3a…`?"** For a multi-turn conversation she
  can **propose the cases worth building** — the turns where the agent actually decided something,
  each marked as locking in the current behavior or asserting a corrected one. She proposes only;
  nothing is created until you tell her to add them. This is the same agent behind
  [**Generate tests**](/guide/test-suites-and-cases#let-proxytrace-propose-the-cases) in the trace
  detail panel, and it needs an Enterprise license.

The resulting suite renders as a card you can open. A natural flow: find notable traces → build
or extend a suite → refine the key cases → start a run.

## Skills

For richer, multi-step jobs, Tracey has **skills** — built-in playbooks she loads **on demand**.
Her everyday instructions stay lean; when your request matches a skill, she pulls in that skill's
detailed steps just for that task and follows them. You don't invoke skills directly — just ask
for what you want in plain language (e.g. "optimize my support agent") and Tracey loads the right
skill herself.

A skill also **unlocks the specialist tools** that task needs. To keep her everyday toolset
focused — and her tool selection sharp — Tracey carries only a lean core by default (navigation,
manual search, the inline renderers, the question widget, and reading your agents). The tools for a
particular area come in *with* its skill, so the first time a request touches that area she loads
the matching skill, which brings both the playbook and its tools. A loaded skill stays loaded for
the rest of the conversation, so she only loads each one once. This is automatic and changes
nothing about how you talk to her; you may just notice a brief "loading skill" step.

Her skills cover:

| Skill | Loads when you ask about… |
|-------|---------------------------|
| **Test suites & runs** | your suites, test runs, results/pass rates, why a run failed, comparing two runs, or starting/cancelling a run |
| **Suite curation** | building a suite from captured traces, or adding/removing/editing a suite's test cases |
| **Review proposals** | listing or reviewing proposals, or approving/rejecting one |
| **Project insights** | overall stats/usage/cost, a provider, or finding/inspecting captured traces |
| **Optimize an agent** | optimizing, improving, or tuning an agent (below) |
| **Diagnose an agent** | what's wrong with an agent, its anomalies/outliers, or degraded behavior (below) |
| **Test-driven improvement** | a specific thing an agent got wrong, usually with a trace id (below) |

## Optimizing an agent

Ask Tracey to **optimize, improve, or tune an agent** and she runs a complete optimization loop
for you. There's an **Optimize an agent** starter chip for it, too.

She needs two things: the agent must **exist** and it must have a **test suite** (the benchmark
the change is measured against). If the agent has no suite, Tracey tells you and stops — create a
suite first.

The flow:

1. **Theory.** Tracey grounds a hypothesis in real evidence before proposing anything: she checks
   which theories were **already tried** (so an invalidated idea isn't re-submitted), digs into
   the latest run's **failing cases** — each test's actual response and every evaluator's verdict
   and reasoning — **compares runs** to see which cases a previous change fixed or regressed, and
   can **search the agent's captured traces** for errors or suspicious calls. From that she
   theorizes **one** concrete change — a rewritten **system prompt**, a **model switch**, or a
   **tool update** — with a rationale that cites what she found. After you confirm, she submits
   it as an **optimization theory**.
2. **A/B test.** The theory is validated in the background: Proxytrace runs the suite against the
   current agent (baseline) and against the proposed change (candidate), back to back. Tracey
   shows a **theory card** in the chat that streams the status — *Running A/B test…* while it
   works.
3. **Result.** The card resolves to one of two outcomes:
   - **Improved** — the change raised the pass rate, so it becomes a reviewable **optimization
     proposal**. The card shows a **View proposal** link; from there you can approve or reject it
     (and Tracey can do that for you too).
   - **Rejected** — the change didn't improve the agent, so the theory is discarded (kept for
     provenance, visible in the optimization pipeline).

Theories Tracey submits appear alongside optimizer- and user-submitted ones in the project's
optimization pipeline, tagged **via Tracey AI**. Identical theories are de-duplicated and a
project has a cap on how many can be validating at once, so if a submission is a duplicate or the
queue is full, Tracey tells you instead of running it again.

## Diagnosing an agent

Ask Tracey **what's wrong with an agent** — or about its **anomalies** — and she works the
problem end to end. There's a **Diagnose an agent** starter chip for it, too. Proxytrace
[flags anomalous calls automatically](/guide/outliers): a call far outside the agent's own recent
baseline is marked with the reason — high token count, high latency, low cache hit, or many tool
calls. Tracey starts from those flags.

The flow:

1. **Anomalies.** She fetches the agent's recently flagged calls and shows them as a card — each
   row links to the trace and carries its reason badges. No anomalies? She tells you the agent
   looks healthy and stops.
2. **Analysis.** She reads a few representative flagged traces and names the failure pattern —
   e.g. a bloated prompt driving token spikes, prompt churn defeating the cache, or a tool-call
   loop.
3. **Test cases.** She turns the problem into something measurable. If a suite already targets
   this kind of failure, she **adds the flagged traces** to it as cases; otherwise she **creates a
   new suite** for the problem (your happy-path suite stays clean) with a **matching evaluator** —
   usually an LLM judge whose instructions name the observed failure. Where a flagged call's
   recorded response is itself the problem, she sets the case's **expected output** to what the
   agent should have answered.
4. **Run, theory, A/B test.** She runs the suite, reads the failures, and — when the evidence
   supports a concrete fix — submits an **optimization theory**, exactly as in
   [Optimizing an agent](#optimizing-an-agent): the change is A/B-tested in the background and
   becomes a reviewable proposal if it improves the pass rate.

Anomalies are statistical, so Tracey won't force a fix out of a one-off spike — if the flagged
calls don't add up to a repeating, fixable pattern, she says so and stops.

## Reporting a defect: the red/green loop

Anomaly detection catches calls that look *unusual*. It cannot catch a call that looks perfectly
normal and is simply **wrong** — an agent that approves a refund outside the return window uses
ordinary tokens and ordinary latency. That one you have to report, and Tracey turns it into a test.

Paste the trace id and say what went wrong:

> "The agent in trace `5b715614-…` approved a refund even though it shouldn't have, because the
> refund window had already expired. Please look into it."

She then works a test-first loop:

1. **Reproduce.** She opens the trace, reads the whole conversation, and tells you what the agent
   actually did, quoting it. If the trace doesn't show the behavior you described, she says so and
   stops rather than inventing a problem.
2. **State the rule.** She writes the rule that was broken as one sentence — *"a refund must be
   refused when the request falls outside the return window"*. That sentence becomes both the
   expected answer and the criterion the evaluator scores against.
3. **Pick the turn that decided.** In a conversation where the agent used tools, one turn is several
   calls, and only one of them *made* the wrong move — the rest report it. Correcting the wrong one
   produces a test that can never pass, which looks exactly like a fix that didn't work. So she asks
   the [test-case agent](/guide/test-suites-and-cases#let-proxytrace-propose-the-cases) which call to
   correct, passing along your description of the problem.
4. **Write a failing test.** She adds that call to a fitting suite (or creates one) as a **test case
   whose expected answer is what the agent should have said** — not what it did say. She also makes
   sure the suite has an evaluator that can actually judge the rule, creating an LLM judge for it if
   none fits.
5. **Prove it fails.** She runs the suite and checks *that specific case*. This step is the point of
   the whole loop. If the case unexpectedly **passes**, the test doesn't capture your bug, and she
   fixes the test rather than proposing a change. If the evaluator itself errored, she says so — a
   broken judge is not evidence the agent is wrong.
6. **Propose a fix and prove it works.** She submits an optimization theory, and when the background
   A/B test finishes she checks your case again against the candidate — showing it move from failing
   to passing.

Two things worth knowing about the result. First, Proxytrace only *observes* your agent: it can
prove a change works, but **a human still has to apply it**. Second, a theory only becomes a formal
**proposal** when the whole suite improves by more than statistical noise, and a single case moving
is never enough on its own — so Tracey will often report that your case is fixed while the theory
itself came back rejected. That is the expected outcome, not a contradiction: the case-level result
is the proof that the fix addresses what you reported.

## Conversation history

Tracey keeps a history of your recent conversations, per project, in a **side panel** on the right
of the Tracey AI page. Everything is stored **locally in your browser** — conversations are private
to this device and are never uploaded.

The panel is **hidden by default**: the sidebar icon at the top right of the chat opens it, and
closes it again to give the conversation the full width. Your choice is remembered on this device.

- **Start a new conversation** with **New conversation** at the top of the rail (or the icon next to
  the **send** button). Your current conversation isn't lost — it stays in the history list; the new
  one begins on a clean slate. A conversation is titled automatically from your first message.
- **Open a past conversation** by clicking it in the rail. It loads back into the chat and you can
  **keep going** right where you left off — viewing and continuing are the same action. The active
  conversation is highlighted.
- **Delete a conversation** with the trash icon on its row (it appears on hover), then confirm.
  Deleting is permanent and removes that conversation's cached result data from your browser too.
- Tracey keeps your **20 most recent** conversations per project; once you pass that, the oldest one
  drops off automatically.

Because it's all stored locally, your history is **per browser and per device** — it won't follow
you to another computer, and clearing your browser's site data removes it.

## Where Tracey's traces appear

Because Tracey runs through the proxy, her LLM calls show up in **Traces** like any other
agent, attributed to the built-in **Tracey** system agent for your project. You can filter
traces to system agents to review exactly what she did and how many tokens it cost.

Each finished Tracey response carries a subtle **status row** beneath it. Because a single
answer can involve several model calls (Tracey calling tools, then answering), the row breaks
down usage for the whole turn — the **input tokens**, the **share of those input tokens served
from cache** (shown when any were), the **output tokens**, and the **response time** — where the
token figures match what the Traces page shows for that turn's conversation group, plus two
actions:

- a **copy** button that puts the response text on your clipboard, and
- a **trace icon** that jumps straight to that turn's captured traces in the **Traces** view,
  without hunting for them in the list.

The row appears once the reply has finished streaming, with the figures shown immediately. If
you click the trace icon before the capture has finished saving (ingestion is asynchronous),
you'll see a brief "still being captured" note — try again in a second.

Very long conversations stay fully visible in the chat, but Tracey only *considers* the most
recent stretch of the thread when answering (this keeps long sessions fast and affordable). If
she seems to have forgotten something from much earlier, restate it or start a fresh
conversation.

## Privacy & security

Tracey's chat runs **same-origin**: the browser calls the Proxytrace API with your normal
session (JWT), and the API forwards each request to your model provider server-side. Your
upstream provider credentials never reach the browser.

> Tracey makes real model calls, so she needs a configured provider with a valid key.
> Interactive features including Tracey are unavailable in read-only kiosk mode (no
> `Kiosk:Endpoint` configured); they are fully enabled in interactive kiosk mode.
