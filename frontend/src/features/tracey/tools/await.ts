import { z } from 'zod';
import { testRunGroupsApi } from '../../../api/test-run-groups';
import { theoriesApi } from '../../../api/theories';
import { TestRunStatus, TheoryStatus, type TestRunGroupDto, type TheoryDto } from '../../../api/models';
import { type ToolFactory, tool } from './shared';
import { abortError, pollUntilTerminal, type PollOptions } from './poll-until-terminal';

/** How often a pending action is polled while waiting. */
const POLL_INTERVAL_MS = 3_000;
/** Hard cap on a single wait. Generous for real suites; a slow action returns `timedOut: true`. */
const AWAIT_ACTIONS_TIMEOUT_MS = 10 * 60 * 1_000;
/**
 * Consecutive failed polls tolerated before a handle lands in `errors`. A full wait makes up to
 * ~200 polls, so a single transient blip (network hiccup, backend restart, gateway 5xx) must not
 * void the whole wait — that was the main way Tracey "lost" a finished run. A persistently bad
 * handle (404 on every poll) still fails within ~10 s instead of spinning to the 10-minute cap.
 */
const MAX_CONSECUTIVE_POLL_FAILURES = 3;

const TERMINAL_RUN: ReadonlySet<TestRunStatus> = new Set([
  TestRunStatus.Completed,
  TestRunStatus.Failed,
  TestRunStatus.Cancelled,
]);
const TERMINAL_THEORY: ReadonlySet<TheoryStatus> = new Set([
  TheoryStatus.Validated,
  TheoryStatus.Invalidated,
  TheoryStatus.Failed,
]);

export const isRunTerminal = (status: TestRunStatus): boolean => TERMINAL_RUN.has(status);
export const isTheoryTerminal = (status: TheoryStatus): boolean => TERMINAL_THEORY.has(status);

/** The kinds of long-running action Tracey can wait on. */
export type AwaitKind = 'test-run' | 'theory';

/** Compact, model-facing result for one awaited handle. */
export interface AwaitResult {
  kind: AwaitKind;
  id: string;
  status: TestRunStatus | TheoryStatus;
  timedOut: boolean;
}

/** Model-facing result for a handle whose polling failed (bad id, network, …). */
export interface AwaitError {
  kind: AwaitKind;
  id: string;
  error: string;
}

export interface RunAwaitResult extends AwaitResult {
  kind: 'test-run';
  status: TestRunStatus;
  suiteName: string;
  agentName: string;
  /**
   * One entry per run in the group. `runId` is the id every run-reading tool takes — the handle
   * itself is a *group* id, so without this the only way to name the run just finished is to list
   * runs and take the newest, which races.
   */
  runs: {
    runId: string;
    agentName: string;
    status: TestRunStatus;
    passed: number;
    failed: number;
    total: number;
    passRate: number;
  }[];
}

export interface TheoryAwaitResult extends AwaitResult {
  kind: 'theory';
  status: TheoryStatus;
  agentName: string;
  resultingProposalId: string | null;
  /**
   * The A/B **candidate** run, recorded whether the theory won or lost — and the only handle to the
   * A/B evidence that exists, since a Tracey-submitted theory carries no evidenceTestRunIds and the
   * baseline run id is never persisted. Pass it to get_case_results to prove a specific case moved.
   */
  abTestRunId: string | null;
  baselinePassRate: number | null;
  projectedPassRate: number | null;
  pValue: number | null;
}

/** What one awaited handle resolves to — discriminated by `kind` (the card narrows on it). */
export type AnyAwaitResult = RunAwaitResult | TheoryAwaitResult;

function summarizeRun(group: TestRunGroupDto, timedOut: boolean): RunAwaitResult {
  return {
    kind: 'test-run',
    id: group.id,
    status: group.status,
    timedOut,
    suiteName: group.suiteName,
    agentName: group.agentName,
    runs: group.runs.map((run) => ({
      runId: run.id,
      agentName: run.agentName,
      status: run.status,
      passed: run.passedCases,
      failed: run.failedCases,
      total: run.totalCases,
      passRate: run.passRate,
    })),
  };
}

function summarizeTheory(theory: TheoryDto, timedOut: boolean): TheoryAwaitResult {
  return {
    kind: 'theory',
    id: theory.id,
    status: theory.status,
    timedOut,
    agentName: theory.agentName,
    resultingProposalId: theory.resultingProposalId,
    abTestRunId: theory.abTestRunId,
    baselinePassRate: theory.baselinePassRate,
    projectedPassRate: theory.projectedPassRate,
    pValue: theory.pValue,
  };
}

/** Waits on a single handle, dispatching to the right API + terminal predicate by kind. */
async function awaitOne(handle: { kind: AwaitKind; id: string }, opts: PollOptions): Promise<AwaitResult> {
  // Pass the abort signal into the API call too, so hitting Stop tears down the in-flight GET
  // rather than only halting the loop after it returns.
  const reqOpts = { silentStatuses: [404], signal: opts.signal };
  if (handle.kind === 'test-run') {
    const { snapshot, timedOut } = await pollUntilTerminal(
      // A 404 (bad handle) still rejects into the per-handle `errors` — silent so the
      // model-recoverable failure doesn't raise a red error toast.
      () => testRunGroupsApi.get(handle.id, reqOpts),
      (g) => isRunTerminal(g.status),
      opts,
    );
    return summarizeRun(snapshot, timedOut);
  }
  const { snapshot, timedOut } = await pollUntilTerminal(
    () => theoriesApi.get(handle.id, reqOpts),
    (t) => isTheoryTerminal(t.status),
    opts,
  );
  return summarizeTheory(snapshot, timedOut);
}

const handleSchema = z.object({
  kind: z.enum(['test-run', 'theory']).describe('The kind of action: "test-run" or "theory".'),
  id: z.string().describe('The action id from the `awaitable` handle of the producing tool.'),
});

/**
 * Tracey's wait tool. Resolves once every handed-in action reaches a terminal state (or the
 * per-handle cap is hit), then returns one aggregate so Tracey can react in the same turn.
 */
export const createAwaitTools: ToolFactory = () => ({
  await_actions: tool({
    description:
      'Wait for long-running actions to finish, then return their results so you can react in the ' +
      'same turn. Pass the exact `awaitable` handle(s) ({ kind, id }) from start_test_run or ' +
      'submit_optimization_theory — never a suite/agent id. Call once with EVERY pending handle ' +
      'batched; never per action, and never poll get_run / get_proposal yourself. A result with ' +
      '`timedOut: true` means the action is still running — say so. An entry in `errors` means its ' +
      'state could not be read (the action itself may still be running or have succeeded) — report ' +
      'that honestly instead of claiming the action failed. A finished run names each `runId` (pass ' +
      'those to get_case_results — the handle itself is a GROUP id); a finished theory reports ' +
      '`abTestRunId`, the A/B candidate run, plus the baseline/projected pass rates and the p-value.',
    parameters: z.object({
      handles: z.array(handleSchema).min(1).describe('The actions to wait for.'),
    }),
    confirm: false,
    execute: async ({ handles }, _ctx, signal) => {
      const opts: PollOptions = {
        intervalMs: POLL_INTERVAL_MS,
        timeoutMs: AWAIT_ACTIONS_TIMEOUT_MS,
        maxConsecutiveFailures: MAX_CONSECUTIVE_POLL_FAILURES,
        // Abort-aware sleep: when the user stops the turn, the wait ends now, not at the next
        // poll tick — otherwise a stopped turn would keep polling for up to 10 minutes.
        sleep: (ms) =>
          new Promise((resolve, reject) => {
            if (signal?.aborted) {
              reject(abortError());
              return;
            }
            const timer = setTimeout(() => {
              signal?.removeEventListener('abort', onAbort);
              resolve();
            }, ms);
            const onAbort = () => {
              clearTimeout(timer);
              reject(abortError());
            };
            signal?.addEventListener('abort', onAbort, { once: true });
          }),
        now: () => Date.now(),
        signal,
      };
      // One bad handle (deleted entity, mistyped id, network failure) must not lose the other
      // results, so failures are captured per handle instead of rejecting the whole wait. An
      // abort is not a per-handle failure — it cancels the whole call, so it propagates.
      const settled = await Promise.all(
        handles.map(async (handle): Promise<AwaitResult | AwaitError> => {
          try {
            return await awaitOne(handle, opts);
          } catch (e) {
            if (e instanceof Error && e.name === 'AbortError') throw e;
            return { kind: handle.kind, id: handle.id, error: e instanceof Error ? e.message : String(e) };
          }
        }),
      );
      const results = settled.filter((r): r is AwaitResult => !('error' in r));
      const errors = settled.filter((r): r is AwaitError => 'error' in r);
      // Returned inline (not via the artifact `store`): the aggregate is already compact and there
      // is no `await_actions` card to resolve a reference — it falls back to ToolCallCard. Storing
      // it would only add a blob nothing reads. The per-item live cards already visualize progress.
      return {
        results,
        ...(errors.length > 0 ? { errors } : {}),
        anyTimedOut: results.some((r) => r.timedOut),
      };
    },
  }),
});
