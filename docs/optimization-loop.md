# The Optimization Loop

Proxytrace's core feedback loop turns curated traces into validated, evidence-backed agent
improvements:

```
TestSuite (+ TestCases)
      │  ITestRunnerService.RunInBackgroundAsync
      ▼
TestRunGroup ── one TestRun per endpoint ── each TestCase → TestResult (scored by evaluators)
      │  on completion: IOptimizerService.EnqueueAsync(group)
      ▼
Optimizers discover OptimizationTheory hypotheses   (also: external producers via POST /api/theories)
      │  ITheoryValidationService.SubmitAsync  (dedup + per-project quota)
      ▼
A/B validation ── baseline run vs candidate run, back to back, same suite ── two-proportion test
      │  win (improvement beyond noise)   │  loss / no significant win   │  validation errored
      ▼                                   ▼                              ▼
Draft OptimizationProposal                Theory marked Invalidated      Theory marked Failed
(carries A/B evidence)                    (metrics kept, dedup)          (retryable, not counted)
      │  human review on the Proposals review desk
      ▼
 Accepted (promoted → handoff package for the developer)  /  Rejected
      │  ProposalAdoptionService watches ingested traffic for the exact change
      ▼
 Adopted (change observed live in the agent, or confirmed manually)
```

Proxytrace is an observing proxy — it cannot change the client's actual system prompt, tools,
or model. Promoting a proposal therefore does **not** apply anything; it hands the change to
the developer (copy buttons, a markdown handoff doc, and `GET /api/proposals/{id}/artifact`)
and arms adoption tracking (Stage 5).

The user-facing description of this loop lives in `manual/guide/optimization-theories.md`,
`manual/guide/running-tests.md`, and `manual/guide/optimization-proposals.md` — keep those in
sync when the loop changes.

## Stage 1 — Run a suite

A **`TestSuite`** holds curated **`TestCase`** inputs and an N:M set of **`IEvaluator`**s. A `TestCase`
is created three ways, all preserving provenance where one exists: **promoted** from a trace as-is
(expected output = the response the agent recorded), **corrected** from a trace (the agent's input, but
a human-supplied expected output — "the right answer was X", which turns a rejected output into a
regression test), or **synthetic** (raw input + expected output, no source). Promoted and corrected
cases carry `ITestCase.SourceAgentCallId` — a denormalized link back to the source `AgentCall` — so the
chain `trace → case` stays answerable; synthetic cases carry `null`. Both the REST seam
(`POST /api/test-suites/{id}/test-cases`) and the MCP `add_trace_to_suite` tool expose the correction
path (see [`mcp.md`](mcp.md)).

**A case scores one model call — so promote the decision, not the summary.** `RunTestCase` calls
`IModelClient.CompleteAsync(testCase.Input)` **exactly once** and never continues the tool loop
(`TestRunnerService.cs`), so a case only ever grades the next assistant message. A promoted or
corrected case's input is `agentCall.Request` verbatim (`TestCase`'s `CreateNewFromCall` /
`CreateCorrection`), and an agent *turn* that uses tools is **several** `AgentCall`s sharing one
`ConversationId` — each capturing the conversation as it stood. Promote the **last** one and the input
already holds every tool call the agent made *and* every result it got; the only producible output is
the closing summary. A **correction** there is unpassable by construction: when the harmful call
already succeeded in the input, no expected output that contradicts that result can be generated, and
the case fails forever while reading as "the prompt fix did not work". Correct instead the call whose
own *response* contains the wrong tool call — its request stops at the decision point, so the fix has
somewhere to land.

`Conversation.ResolvedToolCallCount` counts the tool calls an input already resolved (paired by
tool-call id) and surfaces as `TestCaseDto.ResolvedToolCallCount`; anything above zero means the case
grades a summary. `add_trace_to_suite` says so in its tool description, and Tracey's
`create_suite` / `add_to_suite` return an `unpassableCases` report when a correction lands on such an
input — see [`../frontend/docs/TRACEY.md`](../frontend/docs/TRACEY.md). Straight promotions are not
flagged: they assert the response the agent actually gave, which agrees with their own input by
construction.

`ITestRunnerService` executes the suite:

- `RunInBackgroundAsync(suite, endpoints, scheduleId, sampleCount)` — creates a **`TestRunGroup`**
  with **`sampleCount` `TestRun`s per endpoint** (model comparison × sampling), queues them, returns
  immediately. A suite may target **at most `ITestRunGroup.MaxModelEndpoints` (3) endpoints** and
  **`ITestRunGroup.MaxSampleCount` (5) samples** per endpoint — both hard caps enforced in
  `CreateGroup` (throws), in the controllers (400), and (endpoints only) in `TestRunSchedule.Validate`;
  the UI caps selection to match. So a group holds up to 3 × 5 = **15 runs**. Each `TestRun` carries a
  zero-based `SampleIndex`; the group carries the requested `SampleCount` (1 for single-sample runs).
  Runs sharing an endpoint form a **cohort** — averaged in the results UI and reduced to one
  representative run for this loop (see below). Scheduled runs always use `sampleCount = 1`;
  A/B validation uses `OptimizationOptions.AbSampleCount`.
- `RunInForegroundAsync(..., sampleCount, ...)` — synchronous run; used internally (A/B validation)
  and in tests. Takes `customAgent` and an `isSystemTestRun` flag that **hides internal A/B runs**
  from the user's run list.
- Each `TestCase` produces a **`TestResult`** scored by the suite's evaluators. Live progress
  streams over SSE via `ITestResultBroadcaster`.
- The runner's work queue and in-flight state are **in-memory only**, so a process restart mid-run
  strands those groups in `Running`/`Pending` (they can't be resumed). `OrphanedTestRunReaperHostedService`
  runs on startup and marks every non-terminal group and its non-terminal runs **`Cancelled`**
  (via `ITestRunGroupRepository.GetByStatusesAsync`), mirroring how theory validation re-reconciles its
  in-memory queue on boot. Idempotent — a clean shutdown leaves nothing to reap.

Runs can be kicked off **manually** (the API/UI) or on a **schedule**. A `TestRunSchedule` (a
domain entity binding a suite to a fixed set of endpoints — capped at 3, like manual runs — + a
cadence) is polled by
`TestRunSchedulerService` — a `BackgroundService` on a ~60s `PeriodicTimer`, disabled in kiosk —
which fires `RunInBackgroundAsync(suite, endpoints, scheduleId)` for each due/enabled/licensed
schedule (skipping any whose prior run is still in flight, then advancing `NextRunAt` so missed
ticks collapse). The resulting `TestRunGroup` therefore carries a `ScheduleId`; scheduled runs
feed every downstream stage of this loop exactly like manual ones.

| Concern | File |
|---|---|
| Run orchestration | `Proxytrace.Application/TestRun/Internal/TestRunnerService.cs` (`ITestRunnerService`) |
| Scheduled triggers | `Proxytrace.Application/TestRun/Internal/TestRunSchedulerService.cs` (`BackgroundService`) |
| Run config (concurrency etc.) | `Proxytrace.Application/TestRun/TestRunnerConfiguration.cs` |
| Live results SSE | `Proxytrace.Application/Streaming/Internal/TestResultBroadcaster.cs` |
| Aggregate stats | `Proxytrace.Application/Statistics/TestRun/Internal/TestRunStatsProjector.cs` |
| Run → DTO (incl. latency) | `Proxytrace.Api/Dto/TestRuns/TestRunDtoMapper.cs` |
| API entry | `Proxytrace.Api/Controllers/TestRunGroupsController.cs` |
| Domain entities | `Proxytrace.Domain/{TestSuite,TestCase,TestRunGroup,TestRun,TestResult}/` |

**A run's "latency" is the model's inference latency, aggregated across cases — never a wall-clock
run timer.** Each `TestResult.Latency` is a `Stopwatch` around the single model call
(`ModelClient.CompleteAsync`), excluding evaluators. Everything that reports run latency derives
from those per-case values: `TestRunStats.TotalDuration` sums them, the anomaly detector and
`TheoryValidatorBase.Metrics` use the sum/mean, and the API DTO mappers expose `DurationMs` as their
**average** via the shared `RunLatency.AverageInferenceMs` helper (the run cards and the proposal
A/B-test card both go through it, so the surfaces can't drift). A wall-clock `CompletedAt − CreatedAt`
measure would fold in the run's queue wait, the evaluator passes, and the parallel-execution overlap
between cases, so it is deliberately **not** used for latency.

When a group completes, `TestRunnerService` calls `IOptimizerService.EnqueueAsync(group)`
(`TestRunnerService.cs:178`). `TestRunGroupsController` can also enqueue on demand.

The same completion point also feeds **anomaly detection** (a parallel, independent pipeline — see
below): `TestRunnerService` calls `IAnomalyDetectionService.EnqueueAsync(group)` on both the success
and the failure path (a failed group is itself the most important anomaly). Anomaly detection raises
user notifications rather than theories; it does not participate in the theory→proposal loop.
`Proxytrace.Application/Anomaly/` holds `IAnomalyDetectionService` (a hosted background queue, a
structural copy of `OptimizerService`), the pure `IAnomalyDetector` rule engine (run failed /
endpoint unavailable, pass-rate drop or latency increase vs a rolling baseline computed from
`TestRunStats`), `IAnomalyInputFactory` (assembles a group's `AnomalyInput` — cohorts + rolling
baseline — shared with the kiosk demo seeder, which runs the same rule engine over its seeded
incident groups), and `AnomalyDetectionConfiguration` (thresholds + baseline window). Detected
anomalies are delivered through `INotificationService` → the dashboard notification channel.

**Sampling and the loop — cohort aggregation.** The per-run `TestRunStats` projection is left
**unchanged** (one row per `TestRun`); the loop aggregates at read time so N samples never produce N
near-identical anomalies or bias a proposal toward "sample 0":

- `Proxytrace.Application/TestRun/RunCohort.cs` groups a group's runs by endpoint and resolves, per
  cohort, a **representative run** — the **median sample by pass count** (tie → lowest `SampleIndex`;
  fallback to a completed sample before stats project) — plus mean stats across the samples.
- Both **optimizers** (`CompositeOptimizer` builds the cohorts once and hands `IReadOnlyList<RunCohort>`
  to each `IOptimizerImplementation`) and **anomaly detection** consume cohorts: one input/proposal per
  endpoint, off the representative + cohort mean.
- The anomaly **baseline** (`AnomalyDetectionService.BuildBaselineAsync`) groups prior `TestRunStats` by
  `GroupId`, averages each prior group's samples to one point, then takes the last `BaselineWindow`
  **groups** — so one prior sampled run counts once in the rolling window.
- Read-time consumers that report "one result per endpoint per group" (suite run aggregates, the
  dashboard pass-rate sparkline, an agent's latest suite pass rates) call
  `TestRunStatsCohortExtensions.AggregateSamples`, which collapses only the sample dimension.

## Stage 2 — Discover theories

`IOptimizerService` (a hosted background service) hands the completed group to `IOptimizer`.
`CompositeOptimizer` fans out to every `IOptimizerImplementation`, each producing unproven
**`IOptimizationTheory`** hypotheses (Kind = system-prompt / tool-update / model-switch):

| Optimizer | File |
|---|---|
| Composite fan-out | `Optimization/Internal/CompositeOptimizer.cs` |
| Prompt rewrite | `Optimization/Internal/UpdateSystemPromptOptimizer.cs` |
| Tool definition change | `Optimization/Internal/UpdateToolDefinitionOptimizer.cs` |
| Model switch | `Optimization/Internal/SwitchModelOptimizer.cs` |
| Queue + dispatch | `Optimization/Internal/OptimizerService.cs` (`IOptimizerService`) |

A theory is **just a hypothesis** at this point — not persisted, not validated. The optimizer
submits each via `ITheoryValidationService.SubmitAsync`. External producers (users, Tracey AI,
API callers) submit the same way through `Proxytrace.Api/Controllers/TheoriesController.cs`
(`POST /api/theories`), so every theory flows through one pipeline.

## Stage 3 — Validate via A/B run

`TheoryValidationService` (hosted background service) deduplicates, enforces the per-project
concurrent-validation quota (`TheorySubmissionOutcome`: `Accepted` / `Duplicate` /
`QuotaExceeded`), then routes the theory to the matching `ITheoryValidator`. The queue itself
is in-memory; on startup the service re-queues every theory still `Proposed`/`Validating`
(`IOptimizationTheoryRepository.GetActiveAsync`) so a restart cannot strand the backlog.
Recovery is skipped in kiosk mode: kiosk storage is in-memory and freshly demo-seeded on
every boot, so the only backlog it could find is the seeded `Proposed`/`Validating` demo
theories — re-queuing those would trigger real A/B runs (LLM spend) on every kiosk start
when a live `Kiosk:Endpoint` is configured. Theories submitted during a kiosk session still
validate normally via `SubmitAsync`:

| Validator | File |
|---|---|
| Pipeline + queue + quota | `Optimization/Internal/TheoryValidationService.cs` (`ITheoryValidationService`) |
| Shared A/B flow (ephemeral candidate agent) | `Optimization/Internal/Validation/AbTestTheoryValidator.cs` |
| Validator base | `Optimization/Internal/Validation/TheoryValidatorBase.cs` |
| System-prompt theory | `.../Validation/SystemPromptTheoryValidator.cs` |
| Tool-update theory | `.../Validation/ToolUpdateTheoryValidator.cs` |
| Model-switch theory | `.../Validation/ModelSwitchTheoryValidator.cs` |
| Two-proportion stats / p-value | `.../Validation/ProportionStats.cs` |
| Evidence assembly | `Optimization/Internal/Evidence/OptimizerEvidenceBuilder.cs` |

Validation runs the **baseline and candidate fresh, back to back**, against the same suite and
the agent's current state — so the only difference is the proposed change (reusing an older run
would conflate the change with agent drift while the theory waited in the queue). For prompt/tool
kinds the candidate is an **ephemeral agent** built by the validator; for model switches it is the
agent on the alternate endpoint. The candidate run is linked to the theory while still in flight
via the `CandidateRunObserver` callback, and is flagged `isSystemTestRun` so it stays out of the
user's run list.

Each arm runs `OptimizationOptions.AbSampleCount` times (default **3**) and the samples are
**pooled** — passes and totals summed across them — before the significance test. One sample per arm
caps the evidence at the suite's case count, which on a small suite cannot resolve anything but an
enormous effect: an 11-case suite improving 5/11 → 8/11 lands at p≈0.19 and is discarded, while the
same effect pooled over three samples (15/33 → 24/33) is significant. The cost is proportional —
each extra sample is another full suite run per arm.

The outcome (`TheoryValidationOutcome`) records baseline pass rate, projected pass rate, p-value,
and candidate run id **regardless of result**:
- **Won** — improvement is real (beyond sampling noise: the two-proportion p-value must be
  ≤ `OptimizationOptions.SignificanceLevel` = 0.05) → spawns a **Draft `OptimizationProposal`**
  carrying the A/B comparison as evidence. Model-switch theories instead require *no* pass-rate
  regression plus a genuine cost or latency win — equal-quality-but-pricier is not a win.
- **Rejected** — the A/B ran cleanly but showed no significant improvement → theory marked
  Invalidated; metrics still kept so the same idea isn't retried (dedup).
- **Could not test** — the comparison never happened: a run was incomplete (unreachable/unauthorized
  provider, upstream outage, per-case inference errors) or the validation threw → theory marked
  **Failed** (`TheoryStatus.Failed`, audit `TheoryValidationFailed`), with no metrics but any linked
  A/B run preserved for diagnosis. A Failed theory is *unproven, not disproven*: it is **excluded
  from the review desk's win rate**, surfaced in its own "Needs attention" queue group instead of
  History, does **not** suppress an identical resubmission (dedup treats it like Invalidated), and
  can be retried (reset) or dismissed (reject → Invalidated without metrics).

### Tuning validation (`OptimizationOptions`)

Bound from the `Optimization` configuration section (`Proxytrace.Api/Module.cs`), with a
`PreserveExistingDefaults()` fallback in `Optimization/Module.cs` so hosts that bind no
configuration — tests, tooling — still resolve it:

| Setting | Default | Effect |
|---------|---------|--------|
| `AbSampleCount` | `3` | Runs per arm, pooled before the significance test (max `ITestRunGroup.MaxSampleCount`). |
| `SignificanceLevel` | `0.05` | Maximum two-sided p-value that counts as a real difference. |
| `RequireStatisticalSignificance` | `true` | When false, beating the baseline is enough — the p-value is still computed and stored. |

`OptimizationOptions.KioskShowcase` is the one place the gate is dropped: the demo runs one sample
per arm so Step 7 finishes in front of an audience, and its real-but-small effect straddles any
fixed threshold. The frontend labels such a proposal **"improvement only"** rather than
"significant" (`theoryQueue.significanceLabel`), so a relaxed gate is never presented as proof it
isn't. Never relax either setting for a real installation: a false positive means shipping a prompt
change that did nothing.

## Stage 4 — Proposal review

An **`OptimizationProposal`** has `Kind`, `Status` (`Draft`/`Accepted`/`Adopted`/`Rejected`),
`Priority`, `Rationale`, typed payloads (`SystemPromptProposal`, `ToolUpdateProposal`,
`ModelSwitchProposal`), and `EvidenceTestRunIds`. Status changes are **domain transitions**
(`Accept()`, `Reject()` from Draft; `MarkAdopted()` from Accepted) — the API's
`PATCH /api/proposals/{id}/status` returns 409 for anything else. Theories and proposals stream
to the **Proposals** review desk over SSE via `IProposalBroadcaster` (`proposal-created`,
`proposal-status-changed`) and `ITheoryBroadcaster`.

**Promote = handoff, not auto-apply.** On promote the UI offers the handoff package: copy
buttons for the proposed prompt / tools JSON / model name, a client-generated markdown
"apply this change" doc, and the machine-readable artifact endpoint
`GET /api/proposals/{id}/artifact` (license-gated like the rest of the controller).

`ITheoryValidationService` also supports **resetting** a terminal theory (Validated, Invalidated,
or Failed) for re-validation (`TheoryResetOutcome`) — refused if the spawned proposal was already
Accepted or Adopted (`BlockedByAcceptedProposal`). For a Failed theory the review desk offers this
as **Retry validation**.

It also supports **rejecting** a theory on user request (`RejectAsync` →
`TheoryRejectOutcome`, `POST /api/theories/{id}/reject`): a `Proposed` theory is dismissed without
ever running A/B validation; a `Validating` theory has its in-flight A/B run cancelled; a `Failed`
theory is filed away without a retry. All land in
`Invalidated` with no A/B metrics — the absence of metrics (`Reject()` on the entity) is what
distinguishes a manual dismissal from an A/B-disproven invalidation (the review-desk copies adapt
accordingly). Cancellation works because each validation registers a per-theory
`CancellationTokenSource` (linked to the service stopping token) around the validator call;
cancelling it aborts the candidate/baseline run through the test runner's linked-token path. A
`Proposed` theory still queued is simply transitioned and skipped when the serial worker reaches it.

**Validation is serial.** `TheoryValidationService` is a singleton hosted worker draining a
single-reader channel with a sequential `await` loop, and each validation runs baseline then
candidate back-to-back — so **at most one A/B run executes at a time** process-wide. The
`MaxInFlightPerProject` quota bounds only the queued backlog, not parallelism. (Horizontal scaling to
multiple replicas would need a distributed lease in `RecoverInFlightTheoriesAsync` to preserve this;
the current deployment is single-process.)

| Concern | File |
|---|---|
| Proposal SSE | `Streaming/Internal/ProposalBroadcaster.cs` |
| Theory SSE | `Streaming/Internal/TheoryBroadcaster.cs` |
| Proposal / theory entities | `Proxytrace.Domain/{OptimizationProposal,OptimizationTheory}/` |
| Artifact endpoint + status transitions | `Proxytrace.Api/Controllers/ProposalsController.cs` |
| DI wiring (optimizers, validators, hosted services) | `Proxytrace.Application/Optimization/Module.cs` |

## Stage 5 — Adoption tracking

After promotion, **`ProposalAdoptionService`** (hosted service,
`Optimization/Internal/Adoption/`) watches `IEntityEventService` and flips an Accepted proposal
to **Adopted** when the change shows up — exactly — in the agent's live state:

- **new `IAgentVersion`** (ingestion detected a prompt/tool change) → exact prompt string /
  tool-set match against the proposed change; the proposal records the version
  (`AdoptedAgentVersionId`/`Number`, "Adopted in v{N}").
- **`IAgent` updated** (ingestion flipped the endpoint) → endpoint match for ModelSwitch
  proposals.
- **proposal promoted** → immediate check against the agent's current state (covers "already
  applied before promoting").
- **startup sweep** over all Accepted proposals heals events missed while down.

Matching is deliberately exact (`ProposalAdoptionMatcher`); a tweaked adoption is confirmed by
the human **Mark adopted** action (`PATCH … status: Adopted`, sets `AdoptedManually`). Known
auto-detection gaps — reverting to an already-stored old version (no new-version event) and
traffic attributed to a different agent — are covered by Mark adopted; the handoff doc
recommends pinning attribution with the `X-Proxytrace-Agent` header.

## Licensing

`OptimizationProposals` and `AgenticEvaluators` are **license-gated features** — gate access
through `ILicenseService` (see [`licensing.md`](licensing.md)), not by checking tiers inline.
