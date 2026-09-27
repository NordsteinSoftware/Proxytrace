import { describe, it, expect } from 'vitest';
import { EvaluationScore, EvaluatorKind, TestRunStatus } from '../../api/models';
import type { EvaluationResultDto, TestResultDto, TestRunDto } from '../../api/models';
import {
  buildCohorts,
  buildCohortRows,
  caseChange,
  cohortPassRate,
  matrixCounts,
  filterSortMatrixRows,
} from './cohorts';
import { buildLeaderboard } from './comparison';

const evaluation = (over: Partial<EvaluationResultDto> = {}): EvaluationResultDto => ({
  evaluatorId: 'ev',
  evaluatorKind: EvaluatorKind.ExactMatch,
  evaluatorName: 'Exact',
  score: EvaluationScore.Good,
  reasoning: null,
  errorMessage: null,
  ...over,
});

const PASS = evaluation({ score: EvaluationScore.Good });
const FAIL = evaluation({ score: EvaluationScore.Bad });

const res = (caseId: string, evals: EvaluationResultDto[], durationMs = 100): TestResultDto => ({
  id: `${caseId}`,
  testCaseId: caseId,
  testCaseSummary: caseId.toUpperCase(),
  actualResponse: '',
  evaluations: evals,
  durationMs,
});

function run(over: Partial<TestRunDto>): TestRunDto {
  return {
    id: 'r', endpointId: 'ep', endpointName: 'ep', sampleIndex: 0, status: TestRunStatus.Completed,
    totalCases: 1, passedCases: 0, failedCases: 0, durationMs: 100, costEur: 0.1,
    tokensIn: 10, tokensOut: 5, cachedTokensIn: 0,
    results: [], testCases: [], evaluators: [{ id: 'ev', kind: EvaluatorKind.ExactMatch, name: 'Exact' }],
    ...over,
  } as TestRunDto;
}

describe('buildCohorts', () => {
  it('groups runs by endpoint in first-appearance order, samples by index', () => {
    const cohorts = buildCohorts([
      run({ id: 'a1', endpointId: 'a', endpointName: 'A', sampleIndex: 1, passedCases: 1 }),
      run({ id: 'a0', endpointId: 'a', endpointName: 'A', sampleIndex: 0, passedCases: 1 }),
      run({ id: 'b0', endpointId: 'b', endpointName: 'B', sampleIndex: 0, passedCases: 1 }),
    ]);
    expect(cohorts.map(c => c.endpointId)).toEqual(['a', 'b']);
    expect(cohorts[0].sampleCount).toBe(2);
    expect(cohorts[0].runs.map(r => r.id)).toEqual(['a0', 'a1']); // ordered by sampleIndex
    expect(cohorts[1].sampleCount).toBe(1);
  });

  it('single run per endpoint → one cohort per run, the run is its representative (N=1 invariant)', () => {
    const r = run({ id: 'only', endpointId: 'e', passedCases: 1, results: [res('c', [PASS])] });
    const [cohort] = buildCohorts([r]);
    expect(cohort.sampleCount).toBe(1);
    expect(cohort.representative).toBe(r);
  });

  it('picks the median sample by pass count, tie → lowest sample index', () => {
    // pass counts [2,5,3,1,4] at sample indices 0..4 → sorted [1,2,3,4,5] → median (index 2) is pass=3 → sample 2
    const runs = [2, 5, 3, 1, 4].map((p, i) =>
      run({ id: `s${i}`, sampleIndex: i, passedCases: p, failedCases: 5 - p }));
    expect(buildCohorts(runs)[0].representative.id).toBe('s2');

    // even-N tie on the median pass count → lowest sample index wins (lower-middle element)
    const tied = [3, 3].map((p, i) => run({ id: `t${i}`, sampleIndex: i, passedCases: p, failedCases: 2 }));
    expect(buildCohorts(tied)[0].representative.id).toBe('t0');
  });
});

describe('buildCohortRows — per-case aggregation across samples', () => {
  it('a single-sample cohort yields a pass/fail cell exactly like the run', () => {
    const r = run({ endpointId: 'e', passedCases: 1, results: [res('c', [PASS])], testCases: [{ id: 'c', summary: 'C' }] });
    const [row] = buildCohortRows(buildCohorts([r]));
    const cell = row.cells[0];
    expect(cell.sampleCount).toBe(1);
    expect(cell.verdict).toBe('pass');
    expect(cell.passCount).toBe(1);
    expect(cell.judgedCount).toBe(1);
  });

  it('aggregates pass fraction, avg score and a mixed verdict across samples', () => {
    const samples = [PASS, PASS, FAIL, PASS, FAIL].map((e, i) =>
      run({ id: `s${i}`, endpointId: 'e', sampleIndex: i, results: [res('c', [e])], testCases: [{ id: 'c', summary: 'C' }] }));
    const [row] = buildCohortRows(buildCohorts(samples));
    const cell = row.cells[0];
    expect(cell.passCount).toBe(3);
    expect(cell.judgedCount).toBe(5);
    expect(cell.verdict).toBe('mixed');
    expect(cell.avgScore).toBeCloseTo(0.6); // 3 of 5 single-evaluator results pass
    expect(row.flaky).toBe(true);
    expect(row.divergent).toBe(true);
  });

  it('all-pass and all-fail cohorts are not flaky', () => {
    const allPass = [0, 1].map(i => run({ id: `p${i}`, endpointId: 'e', sampleIndex: i, results: [res('c', [PASS])], testCases: [{ id: 'c', summary: 'C' }] }));
    const [row] = buildCohortRows(buildCohorts(allPass));
    expect(row.cells[0].verdict).toBe('pass');
    expect(row.flaky).toBe(false);
    expect(row.divergent).toBe(false);
  });

  it('flags cross-endpoint divergence when endpoints disagree', () => {
    const a = run({ id: 'a', endpointId: 'a', results: [res('c', [PASS])], testCases: [{ id: 'c', summary: 'C' }] });
    const b = run({ id: 'b', endpointId: 'b', results: [res('c', [FAIL])], testCases: [{ id: 'c', summary: 'C' }] });
    const [row] = buildCohortRows(buildCohorts([a, b]));
    expect(row.divergent).toBe(true);
    expect(row.flaky).toBe(false);
  });
});

describe('cohortPassRate', () => {
  it('is the mean of per-case pass fractions over judged cases', () => {
    // one endpoint, 2 samples; case c1 passes 2/2, case c2 passes 1/2 → mean(100%, 50%) = 75%
    const samples = [
      run({ id: 's0', endpointId: 'e', sampleIndex: 0, results: [res('c1', [PASS]), res('c2', [PASS])], testCases: [{ id: 'c1', summary: 'C1' }, { id: 'c2', summary: 'C2' }] }),
      run({ id: 's1', endpointId: 'e', sampleIndex: 1, results: [res('c1', [PASS]), res('c2', [FAIL])], testCases: [{ id: 'c1', summary: 'C1' }, { id: 'c2', summary: 'C2' }] }),
    ];
    const rows = buildCohortRows(buildCohorts(samples));
    expect(cohortPassRate(rows, 0)).toBe(75);
  });
});

describe('matrixCounts / filterSortMatrixRows (cohort rows)', () => {
  // Two endpoints (a, b), single sample each. Cases: x divergent, y all fail, z all pass.
  const a = run({ id: 'a', endpointId: 'a', results: [res('x', [PASS]), res('y', [FAIL]), res('z', [PASS])], testCases: [{ id: 'x', summary: 'X' }, { id: 'y', summary: 'Y' }, { id: 'z', summary: 'Z' }] });
  const b = run({ id: 'b', endpointId: 'b', results: [res('x', [FAIL]), res('y', [FAIL]), res('z', [PASS])], testCases: [{ id: 'x', summary: 'X' }, { id: 'y', summary: 'Y' }, { id: 'z', summary: 'Z' }] });
  const rows = buildCohortRows(buildCohorts([a, b]));

  it('counts each filter category including flaky', () => {
    // Change counts need a baseline endpoint; with none every row is uncomparable.
    expect(matrixCounts(rows)).toEqual({
      all: 3, divergent: 1, flaky: 0, failing: 2, passing: 1,
      regressions: 0, improvements: 0, unchanged: 0,
    });
  });

  it('classifies change against the baseline endpoint', () => {
    // x: a passes, b fails; y: both fail; z: both pass.
    expect(matrixCounts(rows, 'a')).toEqual({
      all: 3, divergent: 1, flaky: 0, failing: 2, passing: 1,
      regressions: 1, improvements: 0, unchanged: 2,
    });
    expect(matrixCounts(rows, 'b')).toEqual({
      all: 3, divergent: 1, flaky: 0, failing: 2, passing: 1,
      regressions: 0, improvements: 1, unchanged: 2,
    });
  });

  it('filters to regressions / improvements / unchanged', () => {
    expect(filterSortMatrixRows(rows, 'regressions', 'order', false, 'a').map(r => r.caseId)).toEqual(['x']);
    expect(filterSortMatrixRows(rows, 'improvements', 'order', false, 'b').map(r => r.caseId)).toEqual(['x']);
    expect(filterSortMatrixRows(rows, 'unchanged', 'order', false, 'a').map(r => r.caseId).sort()).toEqual(['y', 'z']);
    // Without a baseline, no row can be classified.
    expect(filterSortMatrixRows(rows, 'regressions', 'order')).toEqual([]);
  });

  it('filters to divergent / failing / passing', () => {
    expect(filterSortMatrixRows(rows, 'divergent', 'order').map(r => r.caseId)).toEqual(['x']);
    expect(filterSortMatrixRows(rows, 'passing', 'order').map(r => r.caseId)).toEqual(['z']);
    expect(filterSortMatrixRows(rows, 'failing', 'order').map(r => r.caseId).sort()).toEqual(['x', 'y']);
  });

  it('the flaky filter selects rows with an internally-disagreeing cohort', () => {
    const flakyRuns = [PASS, FAIL].map((e, i) => run({ id: `f${i}`, endpointId: 'e', sampleIndex: i, results: [res('c', [e])], testCases: [{ id: 'c', summary: 'C' }] }));
    const flakyRows = buildCohortRows(buildCohorts(flakyRuns));
    expect(matrixCounts(flakyRows).flaky).toBe(1);
    expect(filterSortMatrixRows(flakyRows, 'flaky', 'order').map(r => r.caseId)).toEqual(['c']);
  });

  it('a change needs a baseline match and a comparable pair of cohorts', () => {
    const [x] = rows; // case x, endpoints a (pass) and b (fail)
    expect(caseChange(x, 'a')).toBe('regressions');
    expect(caseChange(x, 'b')).toBe('improvements');
    expect(caseChange(x, null)).toBeNull();
    expect(caseChange(x, 'missing-endpoint')).toBeNull();
    const [single] = buildCohortRows(buildCohorts([a]));
    expect(single.cells).toHaveLength(1);
    expect(caseChange(single, 'a')).toBeNull();
  });

  it('does not classify incomplete or flaky data as a change', () => {
    const running = run({ id: 'b', endpointId: 'b', status: TestRunStatus.Running, results: [res('x', [FAIL])], testCases: [{ id: 'x', summary: 'X' }] });
    const [pendingX] = buildCohortRows(buildCohorts([a, running]));
    expect(caseChange(pendingX, 'a')).toBeNull();

    const flakyRuns = [
      run({ id: 'a0', endpointId: 'a', sampleIndex: 0, results: [res('c', [PASS])], testCases: [{ id: 'c', summary: 'C' }] }),
      run({ id: 'a1', endpointId: 'a', sampleIndex: 1, results: [res('c', [FAIL])], testCases: [{ id: 'c', summary: 'C' }] }),
      run({ id: 'b0', endpointId: 'b', sampleIndex: 0, results: [res('c', [PASS])], testCases: [{ id: 'c', summary: 'C' }] }),
    ];
    const [mixed] = buildCohortRows(buildCohorts(flakyRuns));
    expect(mixed.cells[0].verdict).toBe('mixed');
    expect(caseChange(mixed, 'a')).toBeNull();
  });
});

describe('buildLeaderboard — sampled cohorts', () => {
  it('averages an endpoint\'s samples into one entry', () => {
    const samples = [6, 8].map((p, i) =>
      run({ id: `s${i}`, endpointId: 'e', endpointName: 'E', sampleIndex: i, totalCases: 10, passedCases: p, failedCases: 10 - p, results: new Array(10).fill(0).map((_, j) => ({ testCaseId: `c${j}` })) as TestResultDto[] }));
    const [entry] = buildLeaderboard(buildCohorts(samples), true);
    expect(entry.sampleCount).toBe(2);
    expect(entry.passRate).toBe(70); // mean(60%, 80%)
    expect(entry.passed).toBe(7); // round(mean(6, 8))
  });
});
