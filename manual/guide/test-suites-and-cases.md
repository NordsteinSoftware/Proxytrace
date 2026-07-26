# Test Suites & Cases

A **Test Suite** is a curated, reproducible benchmark. A **Test Case** is one
input/expected-output entry inside a suite. Together they let you measure agent behavior
the same way every time.

## Building a suite from traces

The intended workflow is to **promote production traces** into durable benchmarks:

1. Find a [trace](/guide/capturing-traces) that represents a critical behavior or a
   regression you want to guard against.
2. Open its detail panel and click **Add test** — the captured input becomes the case input.
3. Group related cases into a test suite.

Because cases come from real traffic, suites stay grounded in behaviors that actually
matter.

### Pick the trace where the agent decided

A run asks the agent for **one** reply per case: it sends the case input and scores the next
message. It does not replay a whole conversation.

That matters when the agent used tools. A single agent turn that calls tools is captured as
**several traces** — one per model call — and they all share a conversation. Each trace holds
the conversation as it stood at that moment, so the **last** one already contains every tool
call the agent made *and* every result it got. The only reply the agent can still give there
is a closing summary.

So when you are turning a mistake into a regression test, promote the trace where the agent
**made** the wrong move, not the one that reports it. If you promote the final trace and then
[edit the expected output](#editing-the-expected-output) to say the agent should have refused,
the case can never pass: the input already says the action succeeded, and no amount of prompt
tuning can change a result that is part of the input. The case stays red forever, which looks
exactly like a fix that did not work.

The trace you want is the earlier one whose own response contains the wrong tool call — open
the conversation in the trace detail panel to find it. Promoting a trace **as-is** is never
affected; only a hand-edited expectation can contradict the input.

## The suites page

![The Test Suites page: a selectable suite list on the left and the selected suite's detail panel on the right.](/screenshots/suites/overview.png)

The page is a **master–detail view**: a list of suites on the left and the selected
suite's detail on the right. The left column holds **New suite**, an **Agent** filter
(it only lists agents that actually own a suite, so it stays usable no matter how many
agents the project has), and a name search. Each list entry shows the suite's case count,
latest pass rate, and when it last ran.

## Suite statistics

The detail panel reports the suite's run statistics for a selectable **time window**
("bucket"): **Last run**, the **last 7 days**, the **last 30 days**, or **all time**.
For the chosen window it shows the **pass rate**, the **number of runs**, the **average
run duration**, and the **total cost**.

## Test cases

![A suite's detail panel: its test cases on the left and the selected case's input with its editable expected output on the right.](/screenshots/suites/cases.png)

Each test case captures the input to run and the expectation to check against. What
"expected" means depends on the [evaluators](/guide/evaluators) attached to the suite —
an exact string, a number within tolerance, a JSON shape, a tool that should be called,
and so on.

In the detail panel's **Test Cases** tab you can curate the suite directly: click
**Add from traces** to open a picker (search, time-range filter, and a live conversation
preview) and stage agent calls as new cases, remove a case, or select a case to edit its
expected output. Staged additions appear in the list marked **Pending add**; all edits are
applied together with **Save changes**.

## Editing the expected output

The captured response is only a starting point. When the traced output is *not* what you
want the agent to produce — you intend to change the agent to hit a target — edit the
expected output directly:

- **In the Add test dialog**, the *Expected output* section is editable before you add the
  case to a suite.
- **In the suite detail panel**, select a case and choose **Edit expected output** to revise
  an existing case.

The editor offers two mutually exclusive types:

- **Text response** — the assistant's plain-text answer.
- **Tool request** — one or more tool calls the agent should make. Pick a tool name
  (the agent's declared tools are suggested, but any name is allowed) and supply the call
  **arguments as JSON**. Add or remove tool requests as needed. Saving is blocked until the
  text is non-empty or every tool request has a name and valid JSON arguments.

## Attaching evaluators

![A suite's Evaluators tab: the attach list on the left (a toggle marks each attached evaluator) and the selected evaluator's system prompt and judge model on the right.](/screenshots/suites/evaluators.png)

A test suite has a many-to-many relationship with **evaluators**: one suite can score its
cases with several evaluators, and an evaluator can be reused across suites. Choose the
evaluators that express what "correct" means for the suite in the detail panel's
**Evaluators** tab — flip each evaluator's **toggle** to attach or detach it. See
[Evaluators](/guide/evaluators).

## Scheduling runs

The detail panel's **Schedules** tab configures the suite's **schedules** — recurring runs on
a fixed interval against a chosen set of model endpoints. Create, edit, pause/resume, and
delete a suite's schedules there. Scheduled runs require an Enterprise license; see
[Running tests](/guide/running-tests).

## Creating a suite

**+ New suite** opens a step wizard: pick the agent, select traces to seed cases, name the
suite, and choose evaluators.

## Running a suite

Once a suite has cases and evaluators, run it against an
[agent](/guide/agents) version to produce a [test run](/guide/running-tests). The **Run**
button lives in the detail panel header.

## Run history

![A suite's History tab: a list of the suite's previous runs, newest first, each showing the agent, when it ran, and per-model pass rates.](/screenshots/suites/history.png)

The detail panel's **History** tab lists the suite's previous runs, newest first, each with
its per-model pass rates. Clicking a run opens it on the [Test Runs](/guide/running-tests)
page with that run selected, so you can drill into individual case results.
