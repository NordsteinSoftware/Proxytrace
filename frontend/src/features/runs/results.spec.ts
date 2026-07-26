import { describe, it, expect } from 'vitest';
import { EvaluationScore, EvaluatorKind, TestRunStatus } from '../../api/models';
import type {
  EvaluationResultDto,
  TestCaseFixtureDto,
  TestResultArrivedEvent,
  TestResultDto,
  TestRunDto,
  TestRunGroupDto,
} from '../../api/models';
import {
  isErrored,
  isEvalPass,
  resultPass,
  resultScore,
  compositePercent,
  avgLatency,
  isActive,
  runsComplete,
  runGroupProgress,
  isDivergent,
  buildMatrixRows,
  fixtureSummary,
  patchGroupWithResult,
  patchGroupRunStatus,
  markRunRunning,
  scoreLabel,
} from './results';
import {
  buildLeaderboard,
  buildEvaluatorHeatmap,
  comparisonGrid,
} from './comparison';
import { buildCohorts } from './cohorts';

function evaluation(over: Partial<EvaluationResultDto> = {}): EvaluationResultDto {
  return {
    evaluatorId: 'ev',
    evaluatorKind: EvaluatorKind.ExactMatch,
    evaluatorName: 'Exact',
    score: EvaluationScore.Good,
    reasoning: null,
    errorMessage: null,
    ...over,
  };
}

function result(evaluations: EvaluationResultDto[], durationMs = 100): TestResultDto {
  return {
    id: 'r',
    testCaseId: 'c',
    testCaseSummary: 'case',
    actualResponse: '',
    evaluations,
    durationMs,
  };
}

const PASS = evaluation({ score: EvaluationScore.Good });
const FAIL = evaluation({ score: EvaluationScore.Bad });
const ERR = evaluation({ score: null, errorMessage: 'boom' });

describe('isErrored / isEvalPass', () => {
  it('treats a non-null errorMessage as errored and not passing', () => {
    expect(isErrored(ERR)).toBe(true);
    expect(isEvalPass(ERR)).toBe(false);
  });

  it('passes only for passing scores', () => {
    expect(isEvalPass(evaluation({ score: EvaluationScore.Excellent }))).toBe(true);
    expect(isEvalPass(evaluation({ score: EvaluationScore.Acceptable }))).toBe(true);
    expect(isEvalPass(evaluation({ score: EvaluationScore.Bad }))).toBe(false);
    expect(isEvalPass(evaluation({ score: null }))).toBe(false);
  });
});

describe('resultPass', () => {
  it('returns null when there are no evaluators', () => {
    expect(resultPass(result([]))).toBeNull();
  });

  it('passes only when every evaluator that produced a verdict passes', () => {
    expect(resultPass(result([PASS, PASS]))).toBe(true);
    expect(resultPass(result([PASS, FAIL]))).toBe(false);
  });

  it('ignores errored evaluators rather than counting them as failures', () => {
    // A judge that crashed says nothing about the agent — it must not read as a defect.
    expect(resultPass(result([PASS, ERR]))).toBe(true);
    expect(resultPass(result([FAIL, ERR]))).toBe(false);
  });

  it('returns null when every evaluator errored — nothing judged the case', () => {
    expect(resultPass(result([ERR, ERR]))).toBeNull();
  });
});

describe('resultScore', () => {
  it('returns null when there are no evaluators', () => {
    expect(resultScore(result([]))).toBeNull();
  });

  it('is the fraction of the judging evaluators that passed', () => {
    expect(resultScore(result([PASS, PASS]))).toBe(1);
    expect(resultScore(result([PASS, FAIL]))).toBe(0.5);
  });

  it('leaves errored evaluators out of both numerator and denominator', () => {
    expect(resultScore(result([PASS, ERR]))).toBe(1);
    expect(resultScore(result([ERR, ERR]))).toBeNull();
  });
});

describe('compositePercent', () => {
  it('rounds passed/total to a percent', () => {
    expect(compositePercent(1, 2)).toBe(50);
    expect(compositePercent(2, 3)).toBe(67);
  });

  it('returns null when total is zero', () => {
    expect(compositePercent(0, 0)).toBeNull();
  });
});

describe('avgLatency', () => {
  it('averages result durations', () => {
    const run = { results: [result([], 100), result([], 300)] } as TestRunDto;
    expect(avgLatency(run)).toBe(200);
  });

  it('returns null with no results', () => {
    expect(avgLatency({ results: [] } as unknown as TestRunDto)).toBeNull();
  });
});

describe('isActive', () => {
  it('is true for running and pending only', () => {
    expect(isActive(TestRunStatus.Running)).toBe(true);
    expect(isActive(TestRunStatus.Pending)).toBe(true);
    expect(isActive(TestRunStatus.Completed)).toBe(false);
    expect(isActive(TestRunStatus.Failed)).toBe(false);
  });
});

describe('runsComplete', () => {
  const r = (status: TestRunStatus): TestRunDto => ({ status } as TestRunDto);

  it('is false when there are no runs', () => {
    expect(runsComplete([])).toBe(false);
  });

  it('is false while any run is pending or running', () => {
    expect(runsComplete([r(TestRunStatus.Completed), r(TestRunStatus.Running)])).toBe(false);
    expect(runsComplete([r(TestRunStatus.Pending)])).toBe(false);
  });

  it('is true once every run is in a terminal state', () => {
    expect(runsComplete([r(TestRunStatus.Completed), r(TestRunStatus.Failed)])).toBe(true);
  });
});

describe('runGroupProgress', () => {
  const run = (totalCases: number, durations: number[]): TestRunDto => ({
    totalCases,
    results: durations.map((durationMs, i) => ({ testCaseId: `c${i}`, durationMs })),
  } as TestRunDto);

  it('returns zeroed progress with no ETA before anything runs', () => {
    expect(runGroupProgress([run(4, [])])).toEqual({ done: 0, total: 4, percent: 0, etaMs: null });
  });

  it('sums done/total across runs and computes percent', () => {
    const p = runGroupProgress([run(4, [100, 100]), run(4, [100])]);
    expect(p.done).toBe(3);
    expect(p.total).toBe(8);
    expect(p.percent).toBe(38); // round(3/8 * 100)
  });

  it('estimates ETA from average case duration times remaining cases', () => {
    const p = runGroupProgress([run(4, [200, 200])]); // avg 200ms, 2 remaining
    expect(p.etaMs).toBe(400);
  });

  it('has no ETA once everything is done', () => {
    expect(runGroupProgress([run(2, [100, 100])]).etaMs).toBeNull();
  });
});

describe('isDivergent', () => {
  it('is true only when both pass and fail are present', () => {
    expect(isDivergent([true, false])).toBe(true);
    expect(isDivergent([true, true])).toBe(false);
    expect(isDivergent([false, false])).toBe(false);
    expect(isDivergent([])).toBe(false);
  });
});

describe('fixtureSummary', () => {
  function fixture(evals: boolean[], endpoints: { costEur: number; tokIn: number; tokOut: number }[]): TestCaseFixtureDto {
    return {
      evaluators: evals.map(pass => ({ pass })),
      endpoints,
    } as unknown as TestCaseFixtureDto;
  }

  it('zeroes everything when the fixture is undefined', () => {
    expect(fixtureSummary(undefined)).toEqual({
      passed: 0, total: 0, allPass: false, composite: null, totalCost: 0, totalTokens: 0, tokensOut: 0,
    });
  });

  it('marks allPass and 100% only when every evaluator passes', () => {
    const s = fixtureSummary(fixture([true, true], []));
    expect(s).toMatchObject({ passed: 2, total: 2, allPass: true, composite: 100 });
  });

  it('computes a partial composite and sums cost and tokens', () => {
    const s = fixtureSummary(fixture([true, false], [
      { costEur: 0.01, tokIn: 10, tokOut: 5 },
      { costEur: 0.02, tokIn: 20, tokOut: 5 },
    ]));
    expect(s).toMatchObject({ passed: 1, total: 2, allPass: false, composite: 50 });
    expect(s.totalCost).toBeCloseTo(0.03);
    expect(s.totalTokens).toBe(40);
    expect(s.tokensOut).toBe(10);
  });
});

describe('scoreLabel', () => {
  it('maps each backend score ordinal (1..5) to its label', () => {
    expect(scoreLabel(1)).toBe(EvaluationScore.Terrible);
    expect(scoreLabel(2)).toBe(EvaluationScore.Bad);
    expect(scoreLabel(3)).toBe(EvaluationScore.Acceptable);
    expect(scoreLabel(4)).toBe(EvaluationScore.Good);
    expect(scoreLabel(5)).toBe(EvaluationScore.Excellent);
  });

  it('returns "—" for null and the raw number for out-of-range values', () => {
    expect(scoreLabel(null)).toBe('—');
    expect(scoreLabel(7)).toBe('7');
  });
});

describe('buildMatrixRows', () => {
  function caseResult(testCaseId: string, summary: string, evals: EvaluationResultDto[]): TestResultDto {
    return { id: `${testCaseId}-r`, testCaseId, testCaseSummary: summary, actualResponse: '', evaluations: evals, durationMs: 100 };
  }
  function run(id: string, results: TestResultDto[], testCases: { id: string; summary: string }[] = [], evaluatorCount = 0): TestRunDto {
    return { id, endpointName: id, results, testCases, evaluators: new Array(evaluatorCount).fill({ id: 'ev', kind: EvaluatorKind.ExactMatch, name: 'E' }) } as TestRunDto;
  }

  it('keeps stable suite order (no reshuffle) and flags divergence per row', () => {
    const r1 = run('m1', [caseResult('a', 'A', [PASS]), caseResult('b', 'B', [PASS])]);
    const r2 = run('m2', [caseResult('a', 'A', [FAIL]), caseResult('b', 'B', [PASS])]);
    const rows = buildMatrixRows([r1, r2]);
    expect(rows.map(r => r.caseId)).toEqual(['a', 'b']);
    expect(rows.find(r => r.caseId === 'a')?.divergent).toBe(true);
    expect(rows.find(r => r.caseId === 'b')?.divergent).toBe(false);
  });

  it('does not reorder by fail count — ordering is left to filterSortMatrixRows', () => {
    const r1 = run('m1', [caseResult('ok', 'Zok', [PASS]), caseResult('bad', 'Abad', [FAIL])]);
    const rows = buildMatrixRows([r1]);
    expect(rows.map(r => r.caseId)).toEqual(['ok', 'bad']); // insertion order, not fail-count order
  });

  it('emits null cells for a case missing from a run', () => {
    const cases = [{ id: 'a', summary: 'A' }, { id: 'b', summary: 'B' }];
    const r1 = run('m1', [caseResult('a', 'A', [PASS])], cases);
    const r2 = run('m2', [caseResult('a', 'A', [PASS])], cases);
    const bRow = buildMatrixRows([r1, r2]).find(r => r.caseId === 'b');
    expect(bRow?.summary).toBe('B');
    expect(bRow?.cells.every(c => c.result === null && c.pass === null)).toBe(true);
  });

  it('gives pending cells a zero-of-N evaluator slot count', () => {
    const cases = [{ id: 'a', summary: 'A' }];
    const r = run('m1', [], cases, 3); // 3 evaluators, no results yet
    const cell = buildMatrixRows([r])[0].cells[0];
    expect(cell.status).toBe('pending');
    expect(cell.progress).toEqual({ done: 0, total: 3 });
  });
});

describe('buildLeaderboard', () => {
  function lbRun(over: Partial<TestRunDto>): TestRunDto {
    // The leaderboard derives latency from the per-case result durations (the model's inference
    // latency), not the wall-clock run-level `durationMs`. Stamp each case with the run's intended
    // latency so a test that sets `durationMs` to control the speed comparison still works.
    const perCaseLatencyMs = over.durationMs ?? 1000;
    return {
      id: 'r', endpointId: 'ep', endpointName: 'm', sampleIndex: 0, status: TestRunStatus.Completed,
      totalCases: 10, passedCases: 8, failedCases: 2,
      results: new Array(10).fill(0).map((_, i) => ({ testCaseId: `c${i}`, durationMs: perCaseLatencyMs })),
      durationMs: perCaseLatencyMs, costEur: 0.5, tokensIn: 100, tokensOut: 50, ...over,
    } as TestRunDto;
  }

  // The leaderboard now works on cohorts (one per endpoint); with one run per endpoint each cohort
  // holds a single run, so these single-sample cases read identically to the pre-sampling behaviour.
  const lb = (runs: TestRunDto[], complete: boolean, ep: string | null = null) =>
    buildLeaderboard(buildCohorts(runs), complete, ep);

  it('flags best pass rate, fastest and cheapest among completed runs', () => {
    const a = lbRun({ id: 'a', endpointId: 'a', endpointName: 'a', passedCases: 9, failedCases: 1, durationMs: 3000, costEur: 0.9 });
    const b = lbRun({ id: 'b', endpointId: 'b', endpointName: 'b', passedCases: 6, failedCases: 4, durationMs: 1000, costEur: 0.1 });
    const [ea, eb] = lb([a, b], true);
    expect(ea.passRate).toBe(90);
    expect(ea.isBest).toBe(true);
    expect(eb.isFastest).toBe(true);
    expect(eb.isCheapest).toBe(true);
  });

  it('derives latency from per-case inference durations, not the wall-clock run.durationMs', () => {
    // The wall-clock run-level durationMs says A is faster (100ms vs 9000ms), but the per-case
    // inference latencies say the opposite (A averages 2000ms, B averages 500ms). The leaderboard
    // must rank on the inference latency, so B wins "fastest".
    const cases = (ms: number) => new Array(10).fill(0).map((_, i) => ({ testCaseId: `c${i}`, durationMs: ms })) as TestResultDto[];
    const a = lbRun({ id: 'a', endpointId: 'a', endpointName: 'a', results: cases(2000), durationMs: 100 });
    const b = lbRun({ id: 'b', endpointId: 'b', endpointName: 'b', results: cases(500), durationMs: 9000 });
    const [ea, eb] = lb([a, b], true);
    expect(ea.durationMs).toBe(2000);
    expect(eb.durationMs).toBe(500);
    expect(eb.isFastest).toBe(true);
    expect(ea.isFastest).toBe(false);
  });

  it('without a deployed endpoint, the best performer is the baseline and others read against it', () => {
    const a = lbRun({ id: 'a', endpointId: 'a', endpointName: 'a', passedCases: 9, failedCases: 1, durationMs: 3000, costEur: 0.9 });
    const b = lbRun({ id: 'b', endpointId: 'b', endpointName: 'b', passedCases: 6, failedCases: 4, durationMs: 1000, costEur: 0.1 });
    const [ea, eb] = lb([a, b], true);
    expect(ea.isBaseline).toBe(true);
    expect(ea.isProduction).toBe(false); // fallback baseline, not a deployed model
    expect(ea.delta).toBeNull();
    expect(eb.delta?.passPoints).toBe(-30); // 60 − 90
    expect(eb.delta?.passBetter).toBe(false);
    expect(eb.delta?.durationBetter).toBe(true); // 1000ms vs baseline 3000ms ⇒ faster
    expect(eb.delta?.costBetter).toBe(true);
  });

  it('makes the in-production endpoint the baseline even when it is not the best', () => {
    const prod = lbRun({ id: 'p', endpointId: 'prod', endpointName: 'prod', passedCases: 8, failedCases: 2, durationMs: 2000, costEur: 0.4 });
    const cand = lbRun({ id: 'c', endpointId: 'cand', endpointName: 'cand', passedCases: 10, failedCases: 0, durationMs: 1000, costEur: 0.2 });
    const entries = lb([prod, cand], true, 'prod');
    const ep = entries.find(e => e.run.id === 'p');
    const ec = entries.find(e => e.run.id === 'c');
    expect(ep?.isBaseline).toBe(true);
    expect(ep?.isProduction).toBe(true);
    expect(ec?.isBest).toBe(true); // candidate beats production on pass rate…
    expect(ec?.delta?.passPoints).toBe(20); // 100 − 80
    expect(ec?.delta?.passBetter).toBe(true); // …shown as a green +20pt vs production
  });

  it('identifies the production baseline before the group settles, but withholds deltas', () => {
    const prod = lbRun({ id: 'p', endpointId: 'prod', endpointName: 'prod', status: TestRunStatus.Running });
    const cand = lbRun({ id: 'c', endpointId: 'cand', endpointName: 'cand', status: TestRunStatus.Running });
    const entries = lb([prod, cand], false, 'prod');
    expect(entries.find(e => e.run.id === 'p')?.isBaseline).toBe(true);
    expect(entries.find(e => e.run.id === 'p')?.isProduction).toBe(true);
    expect(entries.every(e => e.delta === null)).toBe(true);
    expect(entries.every(e => !e.isBest && !e.isFastest && !e.isCheapest)).toBe(true);
  });

  it('derives pending from totalCases minus delivered results', () => {
    const r = lbRun({ totalCases: 10, passedCases: 4, failedCases: 1, results: [{ testCaseId: 'c0' }, { testCaseId: 'c1' }, { testCaseId: 'c2' }, { testCaseId: 'c3' }, { testCaseId: 'c4' }] as TestResultDto[] });
    const [e] = lb([r], true);
    expect(e.pending).toBe(5);
    expect(e.passRate).toBe(80); // 4 / (4+1)
  });

  it('excludes non-completed runs from winner selection', () => {
    const done = lbRun({ id: 'd', endpointId: 'd', endpointName: 'd', passedCases: 5, failedCases: 5 });
    const running = lbRun({ id: 'x', endpointId: 'x', endpointName: 'x', status: TestRunStatus.Running, passedCases: 10, failedCases: 0 });
    const entries = lb([done, running], true);
    expect(entries.find(e => e.run.id === 'd')?.isBest).toBe(true);
    expect(entries.find(e => e.run.id === 'x')?.isBest).toBe(false);
  });

  it('reports no winners, no baseline and null deltas while a candidates-only group is not yet complete', () => {
    const a = lbRun({ id: 'a', endpointId: 'a', endpointName: 'a', status: TestRunStatus.Running, passedCases: 9, failedCases: 1, durationMs: 1000, costEur: 0.1 });
    const b = lbRun({ id: 'b', endpointId: 'b', endpointName: 'b', status: TestRunStatus.Running, passedCases: 6, failedCases: 4, durationMs: 3000, costEur: 0.9 });
    const entries = lb([a, b], false);
    expect(entries.every(e => !e.isBest && !e.isFastest && !e.isCheapest && !e.isBaseline)).toBe(true);
    expect(entries.every(e => e.delta === null)).toBe(true);
    expect(entries.find(e => e.run.id === 'a')?.passRate).toBe(90);
  });

  it('rounds the cohort mean pass rate to an integer (no 96.666… leaking to the card)', () => {
    // Three samples of one endpoint at 100 / 100 / 90 average to 96.66… — must round to 97.
    const s = (sampleIndex: number, passed: number, failed: number) =>
      lbRun({ id: `s${sampleIndex}`, endpointId: 'ep', endpointName: 'ep', sampleIndex, passedCases: passed, failedCases: failed });
    const [entry] = lb([s(0, 10, 0), s(1, 10, 0), s(2, 9, 1)], true);
    expect(entry.sampleCount).toBe(3);
    expect(entry.passRate).toBe(97);
    expect(Number.isInteger(entry.passRate)).toBe(true);
  });
});

describe('comparisonGrid', () => {
  it('widens the baseline column and picks a breakpoint by card count', () => {
    expect(comparisonGrid(1, true).cols).toBe('minmax(0, 1fr)');
    expect(comparisonGrid(2, true).cols).toBe('1.2fr 1fr');
    expect(comparisonGrid(3, true).cols).toBe('1.3fr repeat(2, minmax(0, 1fr))');
    expect(comparisonGrid(2, true).breakpoint).toContain('@xl');
    expect(comparisonGrid(3, true).breakpoint).toContain('@3xl');
  });

  it('uses even columns when there is no baseline', () => {
    expect(comparisonGrid(3, false).cols).toBe('repeat(3, minmax(0, 1fr))');
  });
});

describe('buildEvaluatorHeatmap', () => {
  function hmRun(id: string, evaluations: EvaluationResultDto[][]): TestRunDto {
    return {
      id, endpointId: id, endpointName: id, sampleIndex: 0, status: TestRunStatus.Completed,
      evaluators: [{ id: 'ev', kind: EvaluatorKind.ExactMatch, name: 'Exact' }],
      results: evaluations.map((evals, i) => ({ id: `${id}-${i}`, testCaseId: `c${i}`, testCaseSummary: '', actualResponse: '', evaluations: evals, durationMs: 1 })),
    } as TestRunDto;
  }

  it('tallies score buckets and pass rate per evaluator × model', () => {
    const run = hmRun('m', [
      [evaluation({ score: EvaluationScore.Excellent })],
      [evaluation({ score: EvaluationScore.Bad })],
      [evaluation({ score: null, errorMessage: 'boom' })],
    ]);
    const group = { runs: [run] } as TestRunGroupDto;
    const [row] = buildEvaluatorHeatmap(buildCohorts(group.runs));
    expect(row.evaluator.name).toBe('Exact');
    const cell = row.cells[0];
    expect(cell.total).toBe(3);
    expect(cell.dist[EvaluationScore.Excellent]).toBe(1);
    expect(cell.dist[EvaluationScore.Bad]).toBe(1);
    expect(cell.dist.Error).toBe(1);
    expect(cell.passRate).toBe(33); // 1 of 3 passing
  });

  it('returns a null pass rate for a model with no judgements', () => {
    const group = { runs: [hmRun('m', [])] } as TestRunGroupDto;
    const [row] = buildEvaluatorHeatmap(buildCohorts(group.runs));
    expect(row.cells[0].passRate).toBeNull();
  });

  it('folds live in-flight evaluations into the distribution', () => {
    const group = { runs: [hmRun('m', [[evaluation({ score: EvaluationScore.Good })]])] } as TestRunGroupDto;
    const live = new Map([['m:c-live', {
      runId: 'm', testCaseId: 'c-live', inferenceDone: true,
      evaluations: [evaluation({ evaluatorId: 'ev', score: EvaluationScore.Bad })],
    }]]);
    const [row] = buildEvaluatorHeatmap(buildCohorts(group.runs), live);
    const cell = row.cells[0];
    expect(cell.total).toBe(2); // one finalized + one live
    expect(cell.dist[EvaluationScore.Good]).toBe(1);
    expect(cell.dist[EvaluationScore.Bad]).toBe(1);
  });
});

describe('patchGroupWithResult', () => {
  function group(run: Partial<TestRunDto>): TestRunGroupDto {
    const fullRun = {
      id: 'run1', totalCases: 2, passedCases: 0, failedCases: 0, passRate: 0,
      testCases: [{ id: 'c1', summary: 'Case one' }, { id: 'c2', summary: 'Case two' }],
      results: [], ...run,
    } as TestRunDto;
    return { id: 'g1', runs: [fullRun] } as TestRunGroupDto;
  }

  function event(over: Partial<TestResultArrivedEvent> = {}): TestResultArrivedEvent {
    return {
      type: 'test-result-arrived', runId: 'run1', groupId: 'g1', testCaseId: 'c1',
      overallScore: EvaluationScore.Good, evaluations: [PASS], durationMs: 120, ...over,
    };
  }

  it('appends the result and recomputes counts with a judged-case pass rate', () => {
    const run = patchGroupWithResult(group({}), event({ evaluations: [PASS] })).runs[0];
    expect(run.results).toHaveLength(1);
    expect(run.results[0]).toMatchObject({ testCaseId: 'c1', testCaseSummary: 'Case one', durationMs: 120 });
    expect(run.passedCases).toBe(1);
    expect(run.failedCases).toBe(0);
    // 1 of 1 judged case passed — not 1 of 2 total. The running rate matches the final rate.
    expect(run.passRate).toBe(100);
  });

  it('counts a failing result against failedCases', () => {
    const run = patchGroupWithResult(group({}), event({ evaluations: [FAIL] })).runs[0];
    expect(run.passedCases).toBe(0);
    expect(run.failedCases).toBe(1);
  });

  it('replaces an already-present result instead of dropping the late event', () => {
    const existing = group({ results: [{ ...result([FAIL]), testCaseId: 'c1' } as TestResultDto], passedCases: 0, failedCases: 1, passRate: 0 });
    const next = patchGroupWithResult(existing, event({ evaluations: [PASS] })).runs[0];
    expect(next.results).toHaveLength(1);
    expect(next.passedCases).toBe(1);
    expect(next.failedCases).toBe(0);
  });

  it('leaves the data unchanged when the event is for another group', () => {
    const input = group({});
    expect(patchGroupWithResult(input, event({ groupId: 'other' }))).toEqual(input);
  });

  it('flips a run’s status on run-complete', () => {
    const next = patchGroupRunStatus(group({}), {
      type: 'run-complete', runId: 'run1', groupId: 'g1', status: TestRunStatus.Completed, completedAt: '2024-01-01T00:00:00Z',
    });
    expect(next.runs[0].status).toBe(TestRunStatus.Completed);
    expect(next.runs[0].completedAt).toBe('2024-01-01T00:00:00Z');
  });

  it('carries per-case cost/tokens onto the patched result and sums them into live run totals', () => {
    const run = patchGroupWithResult(
      group({}),
      event({ costEur: 0.012, tokensIn: 100, tokensOut: 40, cachedTokensIn: 10 }),
    ).runs[0];
    expect(run.results[0]).toMatchObject({ costEur: 0.012, tokensIn: 100, tokensOut: 40, cachedTokensIn: 10 });
    // Run-level totals are summed from the per-case usage so the cards tick up live mid-run.
    expect(run.costEur).toBeCloseTo(0.012);
    expect(run.tokensIn).toBe(100);
    expect(run.tokensOut).toBe(40);
    expect(run.cachedTokensIn).toBe(10);
  });

  it('accumulates cost/tokens across multiple cases', () => {
    const afterFirst = patchGroupWithResult(group({}), event({ testCaseId: 'c1', costEur: 0.01, tokensIn: 100, tokensOut: 40 }));
    const run = patchGroupWithResult(afterFirst, event({ testCaseId: 'c2', costEur: 0.02, tokensIn: 50, tokensOut: 30 })).runs[0];
    expect(run.costEur).toBeCloseTo(0.03);
    expect(run.tokensIn).toBe(150);
    expect(run.tokensOut).toBe(70);
  });

  it('leaves run totals null when no case reported usage (so the cards read "—", not 0)', () => {
    const run = patchGroupWithResult(group({}), event({})).runs[0];
    expect(run.costEur).toBeNull();
    expect(run.tokensIn).toBeNull();
  });

  it('flips a Pending run to Running on its first event, and no-ops once past Pending', () => {
    const pending = group({ status: TestRunStatus.Pending });
    const started = markRunRunning(pending, 'run1');
    expect(started.runs[0].status).toBe(TestRunStatus.Running);

    // Idempotent: a run already Running (or terminal) is returned by reference, no re-render churn.
    expect(markRunRunning(started, 'run1')).toBe(started);
    const completed = group({ status: TestRunStatus.Completed });
    expect(markRunRunning(completed, 'run1')).toBe(completed);
  });
});

describe('buildMatrixRows live overlay', () => {
  function run(id: string, results: TestResultDto[], evaluatorCount = 2): TestRunDto {
    return {
      id, endpointName: id, results,
      testCases: [{ id: 'c1', summary: 'Case one' }],
      evaluators: new Array(evaluatorCount).fill(0).map((_, i) => ({ id: `ev${i}` })),
    } as TestRunDto;
  }

  it('marks a case with no result but live progress as running with per-evaluator progress', () => {
    const live = new Map([['r:c1', { runId: 'r', testCaseId: 'c1', evaluations: [{ evaluatorId: 'ev0' } as EvaluationResultDto], inferenceDone: true }]]);
    const [row] = buildMatrixRows([run('r', [])], live);
    expect(row.cells[0].status).toBe('running');
    expect(row.cells[0].progress).toEqual({ done: 1, total: 2 });
    expect(row.cells[0].pass).toBeNull();
  });

  it('marks a case with no result and no live entry as pending', () => {
    const [row] = buildMatrixRows([run('r', [])]);
    expect(row.cells[0].status).toBe('pending');
  });

  it('marks a finalized case as done regardless of live state', () => {
    const finalized = { id: 'c1-r', testCaseId: 'c1', testCaseSummary: 'Case one', actualResponse: '', evaluations: [PASS], durationMs: 1 } as TestResultDto;
    const live = new Map([['r:c1', { runId: 'r', testCaseId: 'c1', evaluations: [], inferenceDone: true }]]);
    const [row] = buildMatrixRows([run('r', [finalized])], live);
    expect(row.cells[0].status).toBe('done');
    expect(row.cells[0].pass).toBe(true);
  });
});
