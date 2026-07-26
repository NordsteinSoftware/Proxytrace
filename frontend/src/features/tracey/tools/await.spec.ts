import { describe, it, expect, vi, beforeEach } from 'vitest';
import { TestRunStatus, TheoryStatus } from '../../../api/models';

const { testRunGroupsApi, theoriesApi } = vi.hoisted(() => ({
  testRunGroupsApi: { get: vi.fn() },
  theoriesApi: { get: vi.fn() },
}));
vi.mock('../../../api/test-run-groups', () => ({ testRunGroupsApi }));
vi.mock('../../../api/theories', () => ({ theoriesApi }));

import { createAwaitTools, isRunTerminal, isTheoryTerminal } from './await';
import type { TraceyToolContext } from './shared';

const ctx: TraceyToolContext = {
  projectId: 'p1', artifactScope: 'u:p', navigate: vi.fn(), confirm: vi.fn(), loadedSkillIds: new Set<string>(),
};
const store = vi.fn();

describe('terminal predicates', () => {
  it('treats Completed/Failed/Cancelled runs as terminal, Running/Pending as not', () => {
    expect(isRunTerminal(TestRunStatus.Completed)).toBe(true);
    expect(isRunTerminal(TestRunStatus.Failed)).toBe(true);
    expect(isRunTerminal(TestRunStatus.Cancelled)).toBe(true);
    expect(isRunTerminal(TestRunStatus.Running)).toBe(false);
    expect(isRunTerminal(TestRunStatus.Pending)).toBe(false);
  });

  it('treats Validated/Invalidated/Failed theories as terminal, Proposed/Validating as not', () => {
    expect(isTheoryTerminal(TheoryStatus.Validated)).toBe(true);
    expect(isTheoryTerminal(TheoryStatus.Invalidated)).toBe(true);
    expect(isTheoryTerminal(TheoryStatus.Failed)).toBe(true);
    expect(isTheoryTerminal(TheoryStatus.Proposed)).toBe(false);
    expect(isTheoryTerminal(TheoryStatus.Validating)).toBe(false);
  });
});

describe('await_actions', () => {
  beforeEach(() => vi.clearAllMocks());

  it('names each run, so the next step does not have to guess which one it was', async () => {
    testRunGroupsApi.get.mockResolvedValue({
      id: 'g1', suiteName: 'Refund policy', agentName: 'Returns', status: TestRunStatus.Completed,
      runs: [{
        id: 'run1', agentName: 'Returns', status: TestRunStatus.Completed,
        passedCases: 1, failedCases: 1, totalCases: 2, passRate: 50,
      }],
    });

    const tool = createAwaitTools(ctx, store).await_actions;
    if (!tool.execute) throw new Error('tool has no execute');
    const result = await tool.execute({ handles: [{ kind: 'test-run', id: 'g1' }] }, ctx) as {
      results: { runs: { runId: string }[] }[];
    };

    expect(result.results[0].runs[0].runId).toBe('run1');
  });

  it('carries the A/B evidence a theory produced, even when it lost', async () => {
    // The candidate run id is recorded on the rejected path too, and it is the only handle to the
    // A/B evidence that exists — so an Invalidated theory still has to surface it.
    theoriesApi.get.mockResolvedValue({
      id: 't1', agentName: 'Returns', status: TheoryStatus.Invalidated, resultingProposalId: null,
      abTestRunId: 'cand1', baselinePassRate: 0.5, projectedPassRate: 0.75, pValue: 0.3,
    });

    const tool = createAwaitTools(ctx, store).await_actions;
    if (!tool.execute) throw new Error('tool has no execute');
    const result = await tool.execute({ handles: [{ kind: 'theory', id: 't1' }] }, ctx) as {
      results: Record<string, unknown>[];
    };

    expect(result.results[0]).toMatchObject({
      abTestRunId: 'cand1', baselinePassRate: 0.5, projectedPassRate: 0.75, pValue: 0.3,
    });
  });

  it('aggregates a mixed batch of already-terminal handles', async () => {
    testRunGroupsApi.get.mockResolvedValue({
      id: 'g1', suiteName: 'Suite', agentName: 'A', status: TestRunStatus.Completed,
      runs: [{ agentName: 'A', status: TestRunStatus.Completed, passedCases: 7, failedCases: 3, totalCases: 10, passRate: 70 }],
    });
    theoriesApi.get.mockResolvedValue({
      id: 't1', agentName: 'A', status: TheoryStatus.Validated, resultingProposalId: 'pr1',
    });

    const tool = createAwaitTools(ctx, store).await_actions;
    if (!tool.execute) throw new Error('tool has no execute');
    const result = await tool.execute(
      { handles: [{ kind: 'test-run', id: 'g1' }, { kind: 'theory', id: 't1' }] },
      ctx,
    ) as { anyTimedOut: boolean; results: { kind: string; id: string; status: string; timedOut: boolean }[] };

    expect(result.anyTimedOut).toBe(false);
    expect(result.results).toHaveLength(2);
    expect(result.results[0]).toMatchObject({ kind: 'test-run', id: 'g1', status: TestRunStatus.Completed, timedOut: false });
    expect(result.results[1]).toMatchObject({ kind: 'theory', id: 't1', status: TheoryStatus.Validated, timedOut: false });
    expect(testRunGroupsApi.get).toHaveBeenCalledWith('g1', { silentStatuses: [404] });
    expect(theoriesApi.get).toHaveBeenCalledWith('t1', { silentStatuses: [404] });
  });

  it('captures a persistently failing handle without losing the other results', async () => {
    vi.useFakeTimers();
    try {
      // Rejects on every poll — the retry tolerance must run out and land it in `errors`.
      testRunGroupsApi.get.mockRejectedValue(new Error('not found'));
      theoriesApi.get.mockResolvedValue({
        id: 't1', agentName: 'A', status: TheoryStatus.Validated, resultingProposalId: 'pr1',
      });

      const tool = createAwaitTools(ctx, store).await_actions;
      if (!tool.execute) throw new Error('tool has no execute');
      const pending = tool.execute(
        { handles: [{ kind: 'test-run', id: 'bad' }, { kind: 'theory', id: 't1' }] },
        ctx,
      ) as Promise<{
        anyTimedOut: boolean;
        results: { kind: string; id: string }[];
        errors?: { kind: string; id: string; error: string }[];
      }>;
      await vi.advanceTimersByTimeAsync(60_000);
      const result = await pending;

      expect(result.results).toHaveLength(1);
      expect(result.results[0]).toMatchObject({ kind: 'theory', id: 't1' });
      expect(result.errors).toEqual([{ kind: 'test-run', id: 'bad', error: 'not found' }]);
      expect(result.anyTimedOut).toBe(false);
    } finally {
      vi.useRealTimers();
    }
  });

  it('survives a transient poll failure and still delivers the result', async () => {
    vi.useFakeTimers();
    try {
      // One network blip mid-wait, then the run completes — the wait must NOT give up on the
      // handle (this was the main way Tracey "lost" a finished run and never followed up).
      testRunGroupsApi.get
        .mockRejectedValueOnce(new Error('network blip'))
        .mockResolvedValue({
          id: 'g1', suiteName: 'Suite', agentName: 'A', status: TestRunStatus.Completed,
          runs: [{ agentName: 'A', status: TestRunStatus.Completed, passedCases: 1, failedCases: 0, totalCases: 1, passRate: 100 }],
        });

      const tool = createAwaitTools(ctx, store).await_actions;
      if (!tool.execute) throw new Error('tool has no execute');
      const pending = tool.execute(
        { handles: [{ kind: 'test-run', id: 'g1' }] },
        ctx,
      ) as Promise<{ results: { status: string }[]; errors?: unknown[] }>;
      await vi.advanceTimersByTimeAsync(10_000);
      const result = await pending;

      expect(result.errors).toBeUndefined();
      expect(result.results).toHaveLength(1);
      expect(result.results[0]).toMatchObject({ kind: 'test-run', id: 'g1', status: TestRunStatus.Completed });
    } finally {
      vi.useRealTimers();
    }
  });

  it('forwards the turn abort signal into the polled API call', async () => {
    const signal = new AbortController().signal;
    testRunGroupsApi.get.mockResolvedValue({
      id: 'g1', suiteName: 'S', agentName: 'A', status: TestRunStatus.Completed, runs: [],
    });

    const tool = createAwaitTools(ctx, store).await_actions;
    if (!tool.execute) throw new Error('tool has no execute');
    await tool.execute({ handles: [{ kind: 'test-run', id: 'g1' }] }, ctx, signal);

    expect(testRunGroupsApi.get).toHaveBeenCalledWith('g1', { silentStatuses: [404], signal });
  });

  it('rejects with AbortError when the turn is stopped, instead of a per-handle error', async () => {
    const controller = new AbortController();
    controller.abort();
    testRunGroupsApi.get.mockResolvedValue({ id: 'g1', status: TestRunStatus.Running, runs: [] });

    const tool = createAwaitTools(ctx, store).await_actions;
    if (!tool.execute) throw new Error('tool has no execute');

    await expect(
      tool.execute({ handles: [{ kind: 'test-run', id: 'g1' }] }, ctx, controller.signal),
    ).rejects.toMatchObject({ name: 'AbortError' });
  });
});
