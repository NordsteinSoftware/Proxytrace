---
name: file-issue
description: >-
  Capture an out-of-scope problem as a well-formed GitHub issue in the
  NordsteinSoftware/Proxytrace repo, then return to the task at hand. Use this the
  moment you notice something broken or smelly that is NOT part of what you were
  asked to do and that you would otherwise silently work around or let slide —
  a latent bug, an unrelated failing/flaky test, dead or duplicated code, a
  missing guard or validation, a TODO/FIXME that actually bites, a perf cliff,
  an N+1 query, a doc or manual that contradicts the code, a confusing API, a
  swallowed exception. Also use it when the user explicitly asks to "file an
  issue", "open a ticket", "log that", "make a GitHub issue", or "track this for
  later". It is for CREATING a new issue to capture a stumble — NOT for fixing an
  in-scope bug, debugging a failure, querying or triaging existing issues, or
  building a feature; those route to other skills or to plain tools. The whole
  point is that good problems get lost when nobody writes them down, so reach for
  this skill eagerly rather than carrying a mental note — even for small things —
  instead of derailing your current change to fix them.
---

# Filing Issues for Stumbles

While doing one thing you constantly notice *other* things — a bug two functions
over, a test that's been silently skipped, a comment that lies about the code.
The instinct is to either fix it now (which derails your task and bloats the
diff) or shrug and move on (so the knowledge dies with this session). Neither is
good. The third option is to spend ~30 seconds writing it down as a GitHub issue
so someone — maybe you, later — can pick it up with full context. That's what
this skill is for.

The repo is **`NordsteinSoftware/Proxytrace`** (public). `gh` is authenticated; run
`gh` commands from inside the repo so the right repo is inferred, or pass
`-R NordsteinSoftware/Proxytrace` explicitly.

## When to file (and when not to)

File when the problem is **real, evidenced, and out of scope** for your current
task:

- A bug you can point at — wrong logic, a swallowed exception, a missing null
  guard, an off-by-one.
- Tech debt that costs someone later — duplicated logic, a god class, an N+1
  query, a leaky abstraction, a TODO that's now load-bearing.
- A test that's skipped, flaky, or asserts the wrong thing.
- Docs/manual that contradict the code (the codebase treats stale docs as a
  defect — see the "Keep these docs current" hard rule).
- Anything you find yourself *working around* instead of fixing.

**Don't** file when:

- It's in scope — just fix it as part of your task.
- It's trivial **and** you're already fixing it in this change anyway.
- It's pure speculation with no evidence ("this might be slow under load" with
  nothing to back it). Investigate enough to make it concrete, or drop it.
- A matching open issue already exists (search first — see below).

If it's genuinely borderline or subjective ("I'd have designed this
differently"), don't auto-file a stream of opinions — mention it to the user in
one line and let them decide. The bar is "would a maintainer be glad this was
written down," not "everything I'd change."

## Workflow

1. **Search for duplicates first.** A duplicate issue is noise.
   ```bash
   gh issue list --state open --search "<2-3 distinctive keywords>"
   ```
   If a clear match exists, add a comment with your new finding instead of
   opening a duplicate (`gh issue comment <n> --body "..."`), tell the user, and
   carry on. Only open a new issue when nothing matches.

2. **Write a clear, searchable title.** Someone scanning the issue list should
   know what it is without opening it. Lead with the symptom or the component.

3. **File it** with a body that follows the template below:
   ```bash
   gh issue create --title "<title>" --label "<label>" --body "$(cat <<'EOF'
   <body>
   EOF
   )"
   ```
   Apply labels (see the list below). `gh issue create` prints the URL.

4. **Tell the user in one line and return to your task.** e.g.
   *"Filed #142 for the swallowed exception in `AgentCallRepository`; back to the
   migration."* Don't let the detour grow — capturing is the goal, not solving.

## Title — make it findable

Lead with what's wrong and where; keep it specific.

**Good:** `AgentCallCleanupService swallows SqliteException on empty DB`
**Bad:** `Fix cleanup bug`

**Good:** `TestRunRepository.GetAllAsync runs N+1 query per evaluation`
**Bad:** `Performance issue in repository`

## Body — what / where / why, plus evidence

Enough context that someone who wasn't in this session can pick it up cold. Use
this shape (drop sections that don't apply):

```markdown
## What
One or two sentences: what's wrong.

## Where
`path/to/File.cs:123` (file:line so it's clickable), plus the method/component.

## Why it matters
The concrete cost — what breaks, who hits it, what it blocks or slows.

## Repro / evidence
Steps, a failing assertion, or a log/stack trace in a code fence.

## Suggested fix (optional)
A pointer if you have one — but don't pretend certainty you don't have.
```

Paste stack traces, logs, and code snippets inside fenced code blocks so they
stay readable. A `file:line` reference is worth a paragraph of prose — always
include one when you have it.

## Labels

Apply the labels that fit with `--label`; combine a *type* with a *priority*
when the priority is obvious:

| Use | Label |
|-----|-------|
| Defect / something broken | `bug` |
| New capability or feature request | `enhancement` |
| Cleanup, restructure, debt | `type:refactor` |
| Test gap / flaky / wrong assertion | `type:test` |
| Docs or manual wrong/missing | `type:docs` (a `documentation` synonym also exists — prefer `type:docs` for consistency with the `type:*` family) |
| CI / build / release pipeline | `type:ci` |
| Urgency, when clear | `priority:high` · `priority:medium` · `priority:low` |
| Small, self-contained, good for a newcomer | `good first issue` |

When unsure of priority, leave it off rather than guessing — a maintainer can
triage. If a label you want doesn't exist, file without it (don't invent
labels).

## Several stumbles at once

If one session turns up multiple unrelated problems, file each as its own issue
so they can be triaged and closed independently. If they're facets of one root
cause, file a single issue and list them — don't fragment one problem into five
tickets. Either way, file as you go rather than hoarding them to the end, where
they tend to be forgotten or lose their context.
