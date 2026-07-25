import { describe, expect, it } from 'vitest';
import { TestRunStatus, TheoryStatus } from '../../../../api/models';
import type { RunAwaitResult, TheoryAwaitResult } from '../../tools/await';
import { awaitOutcome, entityLabel, runCaseSummary } from './await-card-logic';

function run(overrides: Partial<RunAwaitResult> = {}): RunAwaitResult {
  return {
    kind: 'test-run',
    id: 'r1',
    status: TestRunStatus.Completed,
    timedOut: false,
    suiteName: 'Suite',
    agentName: 'Agent',
    runs: [{ runId: 'run1', agentName: 'Agent', status: TestRunStatus.Completed, passed: 3, failed: 1, total: 4, passRate: 75 }],
    ...overrides,
  };
}

function theory(overrides: Partial<TheoryAwaitResult> = {}): TheoryAwaitResult {
  return {
    kind: 'theory',
    id: 't1',
    status: TheoryStatus.Validated,
    timedOut: false,
    agentName: 'Agent',
    resultingProposalId: null,
    abTestRunId: null,
    baselinePassRate: null,
    projectedPassRate: null,
    pValue: null,
    ...overrides,
  };
}

describe('awaitOutcome', () => {
  it('is success when everything completed', () => {
    expect(awaitOutcome([run(), theory()], undefined, false)).toBe('success');
  });

  it('treats a rejected theory as a normal outcome, not a failure', () => {
    expect(awaitOutcome([theory({ status: TheoryStatus.Invalidated })], undefined, false)).toBe('success');
  });

  it('is warn when something is still running past the cap', () => {
    expect(awaitOutcome([run({ timedOut: true, status: TestRunStatus.Running })], undefined, true)).toBe('warn');
  });

  it('is warn when a run was cancelled — its results are incomplete, not "all done"', () => {
    expect(awaitOutcome([run({ status: TestRunStatus.Cancelled })], undefined, false)).toBe('warn');
  });

  it('is danger when a run failed', () => {
    expect(awaitOutcome([run({ status: TestRunStatus.Failed })], undefined, false)).toBe('danger');
  });

  it('is danger when a handle could not be read, even if others timed out', () => {
    expect(awaitOutcome([run({ timedOut: true, status: TestRunStatus.Running })], [{ kind: 'theory', id: 'x', error: 'boom' }], true)).toBe('danger');
  });

  it('does not count a timed-out run whose last-seen status is Failed-free as failed', () => {
    expect(awaitOutcome([run({ timedOut: true, status: TestRunStatus.Running })], undefined, true)).toBe('warn');
  });
});

describe('entityLabel', () => {
  it('joins suite and agent', () => {
    expect(entityLabel({ suiteName: 'Smoke', agentName: 'Support' })).toBe('Smoke → Support');
  });

  it('uses whichever part exists', () => {
    expect(entityLabel({ agentName: 'Support' })).toBe('Support');
    expect(entityLabel({ suiteName: 'Smoke' })).toBe('Smoke');
  });

  it('returns null for a legacy snapshot without names, so the caller can fall back', () => {
    expect(entityLabel({})).toBeNull();
    expect(entityLabel({ suiteName: '', agentName: '' })).toBeNull();
  });
});

describe('runCaseSummary', () => {
  it('aggregates counts across runs', () => {
    const r = run({
      runs: [
        { runId: 'runA', agentName: 'A', status: TestRunStatus.Completed, passed: 3, failed: 1, total: 4, passRate: 75 },
        { runId: 'runB', agentName: 'B', status: TestRunStatus.Completed, passed: 2, failed: 2, total: 4, passRate: 50 },
      ],
    });
    expect(runCaseSummary(r)).toEqual({ passed: 5, failed: 3, total: 8 });
  });

  it('returns null for a legacy snapshot without runs', () => {
    expect(runCaseSummary(run({ runs: undefined as unknown as RunAwaitResult['runs'] }))).toBeNull();
    expect(runCaseSummary(run({ runs: [] }))).toBeNull();
  });
});
