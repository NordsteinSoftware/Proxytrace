import { describe, it, expect } from 'vitest';
import { EvaluationScore, EvaluatorKind, TestRunStatus } from '../../../api/models';
import type { EvaluationResultDto, TestResultDto, TestRunDto } from '../../../api/models';
import { caseResults } from './case-verdict';

const evaluation = (over: Partial<EvaluationResultDto> = {}): EvaluationResultDto => ({
  evaluatorId: 'ev1',
  evaluatorKind: EvaluatorKind.Agentic,
  evaluatorName: 'Policy judge',
  score: EvaluationScore.Good,
  reasoning: 'refused correctly',
  errorMessage: null,
  ...over,
});

const result = (over: Partial<TestResultDto> = {}): TestResultDto => ({
  id: 'r1',
  testCaseId: 'c1',
  testCaseSummary: 'refund outside the return window',
  actualResponse: 'Refund approved.',
  evaluations: [evaluation()],
  durationMs: 10,
  ...over,
});

const run = (over: Partial<TestRunDto> = {}): TestRunDto => ({
  id: 'run1',
  groupId: 'g1',
  suiteId: 's1',
  suiteName: 'Refund policy',
  agentId: 'a1',
  agentName: 'Returns',
  endpointId: 'ep1',
  endpointName: 'gpt',
  sampleIndex: 0,
  status: TestRunStatus.Completed,
  totalCases: 1,
  passedCases: 0,
  failedCases: 1,
  passRate: 0,
  costEur: null,
  tokensIn: null,
  tokensOut: null,
  cachedTokensIn: null,
  evaluators: [],
  startedAt: '2026-07-25T10:00:00Z',
  completedAt: '2026-07-25T10:01:00Z',
  durationMs: 10,
  testCases: [],
  results: [result()],
  createdAt: '2026-07-25T10:00:00Z',
  updatedAt: '2026-07-25T10:01:00Z',
  ...over,
});

describe('caseResults', () => {
  it('reports pass when every evaluator passes', () => {
    expect(caseResults(run(), ['c1'])).toEqual([
      { testCaseId: 'c1', verdict: 'pass', result: expect.objectContaining({ id: 'r1' }) },
    ]);
  });

  it('reports fail when an evaluator scores it down', () => {
    const runIt = run({ results: [result({ evaluations: [evaluation({ score: EvaluationScore.Terrible })] })] });
    expect(caseResults(runIt, ['c1'])[0].verdict).toBe('fail');
  });

  it('reports evaluator-error ahead of fail when a judge crashed', () => {
    const runIt = run({
      results: [result({ evaluations: [evaluation({ score: null, errorMessage: 'judge timed out' })] })],
    });
    expect(caseResults(runIt, ['c1'])[0].verdict).toBe('evaluator-error');
  });

  it('reports unjudged when no evaluator scored the case', () => {
    const runIt = run({ results: [result({ evaluations: [] })] });
    expect(caseResults(runIt, ['c1'])[0].verdict).toBe('unjudged');
  });

  it('reports not-in-run for a case the run never executed, never pass', () => {
    expect(caseResults(run(), ['ghost'])).toEqual([
      { testCaseId: 'ghost', verdict: 'not-in-run', result: null },
    ]);
  });

  it('reports run-incomplete for every requested case while the run has not completed', () => {
    const runIt = run({ status: TestRunStatus.Running });
    expect(caseResults(runIt, ['c1', 'c2']).map((c) => c.verdict)).toEqual([
      'run-incomplete',
      'run-incomplete',
    ]);
  });

  it('still reports what is failing so far when no case was asked about', () => {
    // "show me what's going wrong" is answerable mid-run; only an assertion about a named case
    // needs the run to have settled.
    const runIt = run({
      status: TestRunStatus.Running,
      results: [result({ evaluations: [evaluation({ score: EvaluationScore.Terrible })] })],
    });
    expect(caseResults(runIt).map((c) => c.verdict)).toEqual(['fail']);
  });

  it('answers in the order asked, so a caller can zip results back to its own ids', () => {
    const runIt = run({
      results: [result({ id: 'r1', testCaseId: 'c1' }), result({ id: 'r2', testCaseId: 'c2' })],
    });
    expect(caseResults(runIt, ['c2', 'ghost', 'c1']).map((c) => c.testCaseId)).toEqual([
      'c2',
      'ghost',
      'c1',
    ]);
  });

  it('without caseIds returns only the non-passing cases, each labelled', () => {
    const runIt = run({
      results: [
        result({ id: 'r1', testCaseId: 'c1' }),
        result({ id: 'r2', testCaseId: 'c2', evaluations: [evaluation({ score: EvaluationScore.Bad })] }),
        result({ id: 'r3', testCaseId: 'c3', evaluations: [evaluation({ score: null, errorMessage: 'boom' })] }),
      ],
    });
    expect(caseResults(runIt).map((c) => [c.testCaseId, c.verdict])).toEqual([
      ['c2', 'fail'],
      ['c3', 'evaluator-error'],
    ]);
  });
});
