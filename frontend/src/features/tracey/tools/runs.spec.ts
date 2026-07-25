import { describe, it, expect, vi, beforeEach } from 'vitest';
import { EvaluationScore, EvaluatorKind, TestRunStatus } from '../../../api/models';

const { testRunsApi } = vi.hoisted(() => ({ testRunsApi: { get: vi.fn(), list: vi.fn() } }));
vi.mock('../../../api/test-runs', () => ({ testRunsApi }));
vi.mock('../../../api/agents', () => ({ agentsApi: { get: vi.fn() } }));
vi.mock('../../../api/test-suites', () => ({ testSuitesApi: { get: vi.fn() } }));
vi.mock('../../../api/test-run-groups', () => ({
  testRunGroupsApi: { get: vi.fn(), create: vi.fn(), cancel: vi.fn() },
}));

import { createRunTools } from './runs';
import type { TraceyTool, TraceyToolContext } from './shared';

// store echoes the digest back so the returned result is assertable. Async to satisfy StoreFn.
const store = vi.fn(async (_kind: string, _full: unknown, summary: unknown) => summary);

const ctx: TraceyToolContext = {
  projectId: 'p1',
  artifactScope: 'u:p',
  navigate: vi.fn(),
  confirm: vi.fn().mockResolvedValue(true),
  loadedSkillIds: new Set<string>(),
};

function run(t: TraceyTool, args: Record<string, unknown>) {
  if (!t.execute) throw new Error('tool has no execute');
  return t.execute(args, ctx);
}

const evaluation = (over: Record<string, unknown> = {}) => ({
  evaluatorId: 'ev1',
  evaluatorKind: EvaluatorKind.Agentic,
  evaluatorName: 'Policy judge',
  score: EvaluationScore.Good,
  reasoning: 'refused correctly',
  errorMessage: null,
  ...over,
});

const completedRun = {
  id: 'run1',
  suiteName: 'Refund policy',
  agentName: 'Returns',
  status: TestRunStatus.Completed,
  passRate: 50,
  totalCases: 2,
  results: [
    {
      id: 'r1',
      testCaseId: 'c1',
      testCaseSummary: 'refund outside the return window',
      actualResponse: 'Refund approved.',
      evaluations: [evaluation({ score: EvaluationScore.Terrible, reasoning: 'it approved the refund' })],
    },
    {
      id: 'r2',
      testCaseId: 'c2',
      testCaseSummary: 'refund inside the return window',
      actualResponse: 'Refund approved.',
      evaluations: [evaluation()],
    },
  ],
};

beforeEach(() => vi.clearAllMocks());

describe('get_case_results', () => {
  it('reports the verdict of the exact cases asked about, including passing ones', async () => {
    testRunsApi.get.mockResolvedValue(completedRun);

    const result = await run(createRunTools(ctx, store).get_case_results, {
      runId: 'run1',
      caseIds: ['c2'],
      expect: 'pass',
    });

    expect(result).toMatchObject({
      runId: 'run1',
      expect: 'pass',
      cases: [{ testCaseId: 'c2', verdict: 'pass' }],
    });
  });

  it('never reports a case the run never executed as passing', async () => {
    testRunsApi.get.mockResolvedValue(completedRun);

    const result = await run(createRunTools(ctx, store).get_case_results, {
      runId: 'run1',
      caseIds: ['ghost'],
    }) as { cases: { verdict: string }[] };

    expect(result.cases[0].verdict).toBe('not-in-run');
  });

  it('distinguishes a crashed judge from a real failure', async () => {
    testRunsApi.get.mockResolvedValue({
      ...completedRun,
      results: [
        {
          ...completedRun.results[0],
          evaluations: [evaluation({ score: null, errorMessage: 'judge timed out' })],
        },
      ],
    });

    const result = await run(createRunTools(ctx, store).get_case_results, {
      runId: 'run1',
      caseIds: ['c1'],
    }) as { cases: { verdict: string; evaluations: { error?: string }[] }[] };

    expect(result.cases[0].verdict).toBe('evaluator-error');
    expect(result.cases[0].evaluations[0].error).toBe('judge timed out');
  });

  it('without caseIds returns the failing cases with their evaluator reasoning', async () => {
    testRunsApi.get.mockResolvedValue(completedRun);

    const result = await run(createRunTools(ctx, store).get_case_results, { runId: 'run1' }) as {
      cases: { testCaseId: string; evaluations: { evaluator: string; reasoning: string }[] }[];
    };

    expect(result.cases).toHaveLength(1);
    expect(result.cases[0]).toMatchObject({
      testCaseId: 'c1',
      evaluations: [{ evaluator: 'Policy judge', reasoning: 'it approved the refund' }],
    });
  });

  it('stores the full payload under the case-results artifact kind', async () => {
    testRunsApi.get.mockResolvedValue(completedRun);

    await run(createRunTools(ctx, store).get_case_results, { runId: 'run1', caseIds: ['c1'] });

    expect(store).toHaveBeenCalledWith(
      'case-results',
      expect.objectContaining({ runId: 'run1', expect: null }),
      expect.anything(),
    );
  });

  it('maps a missing run to notFound instead of throwing', async () => {
    testRunsApi.get.mockRejectedValue(Object.assign(new Error('nope'), { status: 404 }));

    expect(await run(createRunTools(ctx, store).get_case_results, { runId: 'gone' })).toEqual({
      notFound: 'gone',
    });
  });

  it('is gone under its old name', () => {
    expect(createRunTools(ctx, store).get_run_failures).toBeUndefined();
  });
});

describe('compare_runs', () => {
  it('keeps each moved case addressable by id, not just by a clipped summary', async () => {
    const side = (id: string, score: EvaluationScore) => ({
      id: `res-${id}`,
      testCaseId: id,
      testCaseSummary: `case ${id}`,
      actualResponse: 'x',
      evaluations: [evaluation({ score })],
    });
    testRunsApi.get
      .mockResolvedValueOnce({
        ...completedRun,
        id: 'baseline',
        results: [side('c1', EvaluationScore.Terrible)],
      })
      .mockResolvedValueOnce({
        ...completedRun,
        id: 'candidate',
        results: [side('c1', EvaluationScore.Good)],
      });

    const result = await run(createRunTools(ctx, store).compare_runs, {
      baselineRunId: 'baseline',
      candidateRunId: 'candidate',
    }) as { fixedCases: { testCaseId: string; summary: string }[] };

    expect(result.fixedCases).toEqual([{ testCaseId: 'c1', summary: 'case c1' }]);
  });
});
