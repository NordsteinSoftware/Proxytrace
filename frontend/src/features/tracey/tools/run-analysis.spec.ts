import { describe, expect, it } from 'vitest';
import { EvaluationScore } from '../../../api/models';
import type { EvaluationResultDto, TestResultDto, TestRunDto } from '../../../api/models';
import { clip, compareRuns } from './run-analysis';

function evaluation(score: EvaluationScore | null, errorMessage: string | null = null): EvaluationResultDto {
  return { evaluatorId: 'e1', evaluatorKind: 'ExactMatch', evaluatorName: 'Exact', score, reasoning: null, errorMessage } as EvaluationResultDto;
}

function result(testCaseId: string, pass: boolean | null): TestResultDto {
  const evaluations =
    pass == null ? [] : [evaluation(pass ? EvaluationScore.Good : EvaluationScore.Bad)];
  return {
    id: `r-${testCaseId}`,
    testCaseId,
    testCaseSummary: `case ${testCaseId}`,
    actualResponse: 'out',
    evaluations,
    durationMs: 10,
  };
}

function run(id: string, results: TestResultDto[], passRate = 50): TestRunDto {
  return {
    id, results, passRate,
    agentName: 'Agent', endpointName: 'gpt-4o', suiteName: 'Suite',
  } as TestRunDto;
}

describe('clip', () => {
  it('passes short values through and truncates long ones with a marker', () => {
    expect(clip('short', 10)).toBe('short');
    expect(clip('a'.repeat(12), 10)).toBe(`${'a'.repeat(10)}…`);
    expect(clip('  padded  ', 10)).toBe('padded');
  });
});

// Failure/verdict classification moved to case-verdict.ts — see case-verdict.spec.ts, which also
// covers the cases this could never express: a case that passed, and one absent from the run.

describe('compareRuns', () => {
  it('classifies fixed, regressed and unchanged cases', () => {
    const baseline = run('b', [result('a', false), result('b', true), result('c', false), result('d', true)], 50);
    const candidate = run('c', [result('a', true), result('b', false), result('c', false), result('d', true)], 50);

    const cmp = compareRuns(baseline, candidate);

    expect(cmp.fixed).toBe(1);
    expect(cmp.regressed).toBe(1);
    expect(cmp.stillFailing).toBe(1);
    expect(cmp.stillPassing).toBe(1);
    expect(cmp.unmatched).toBe(0);
    expect(cmp.cases.find((c) => c.testCaseId === 'a')?.movement).toBe('fixed');
    expect(cmp.cases.find((c) => c.testCaseId === 'b')?.movement).toBe('regressed');
  });

  it('counts cases present in only one run (or unjudged) as unmatched', () => {
    const baseline = run('b', [result('a', true), result('only-before', false), result('unjudged', null)]);
    const candidate = run('c', [result('a', true), result('only-after', true), result('unjudged', true)]);

    const cmp = compareRuns(baseline, candidate);

    expect(cmp.cases.map((c) => c.testCaseId)).toEqual(['a']);
    // only-before + only-after + the pair unjudged on one side
    expect(cmp.unmatched).toBe(3);
  });

  it('carries run identities and pass rates for the card', () => {
    const cmp = compareRuns(run('b', [], 40), run('c', [], 70));
    expect(cmp.baseline).toMatchObject({ runId: 'b', passRate: 40 });
    expect(cmp.candidate).toMatchObject({ runId: 'c', passRate: 70 });
    expect(cmp.suiteName).toBe('Suite');
  });
});
