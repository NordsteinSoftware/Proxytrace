// Locks the cross-cutting verdict/score/status/color contract at its lib home — the
// surface both the runs feature and the Tracey live-run tool UI import directly. The
// runs-specific aggregation built on top of these is covered by features/runs/results.spec.ts.
import { describe, it, expect } from 'vitest';
import { EvaluationScore, EvaluatorKind, TestRunStatus } from '../api/models';
import type { EvaluationResultDto, TestResultDto } from '../api/models';
import {
  resultPass,
  resultScore,
  compositePercent,
  isActive,
  passRateColor,
  scoreColor,
  runStatusColor,
  isDivergent,
  scoreLabel,
} from './runResults';

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

function result(evaluations: EvaluationResultDto[]): TestResultDto {
  return { id: 'r', testCaseId: 'c', testCaseSummary: 'case', actualResponse: '', evaluations, durationMs: 100 };
}

const PASS = evaluation({ score: EvaluationScore.Good });
const FAIL = evaluation({ score: EvaluationScore.Bad });
const ERR = evaluation({ score: null, errorMessage: 'boom' });

const SUCCESS = 'var(--success)';
const WARN = 'var(--warn)';
const DANGER = 'var(--danger)';
const MUTED = 'var(--text-muted)';

describe('resultPass / resultScore', () => {
  it('passes only when every evaluator passes; null with no evaluators', () => {
    expect(resultPass(result([PASS, PASS]))).toBe(true);
    expect(resultPass(result([PASS, FAIL]))).toBe(false);
    expect(resultPass(result([]))).toBeNull();
  });

  it('leaves errored evaluators out of the verdict; null when nothing judged the case', () => {
    // A judge that crashed is not evidence about the agent, so it must not read as a failure.
    expect(resultPass(result([PASS, ERR]))).toBe(true);
    expect(resultPass(result([FAIL, ERR]))).toBe(false);
    expect(resultPass(result([ERR, ERR]))).toBeNull();
  });

  it('scores the fraction of passing evaluators; null with no evaluators', () => {
    expect(resultScore(result([PASS, FAIL]))).toBe(0.5);
    expect(resultScore(result([PASS, PASS]))).toBe(1);
    expect(resultScore(result([PASS, ERR]))).toBe(1);
    expect(resultScore(result([]))).toBeNull();
  });
});

describe('compositePercent', () => {
  it('rounds passed/total to 0..100; null when total is 0', () => {
    expect(compositePercent(1, 3)).toBe(33);
    expect(compositePercent(2, 2)).toBe(100);
    expect(compositePercent(0, 0)).toBeNull();
  });
});

describe('isActive', () => {
  it('is true only for pending/running', () => {
    expect(isActive(TestRunStatus.Running)).toBe(true);
    expect(isActive(TestRunStatus.Pending)).toBe(true);
    expect(isActive(TestRunStatus.Completed)).toBe(false);
    expect(isActive(TestRunStatus.Failed)).toBe(false);
  });
});

describe('threshold colors', () => {
  it('passRateColor maps the 0..100 scale at WARN=75 / DANGER=55', () => {
    expect(passRateColor(90)).toBe(SUCCESS);
    expect(passRateColor(75)).toBe(SUCCESS);
    expect(passRateColor(60)).toBe(WARN);
    expect(passRateColor(40)).toBe(DANGER);
    expect(passRateColor(null)).toBe(MUTED);
  });

  it('scoreColor maps the 0..1 scale at WARN=0.8 / DANGER=0.5', () => {
    expect(scoreColor(0.9)).toBe(SUCCESS);
    expect(scoreColor(0.6)).toBe(WARN);
    expect(scoreColor(0.3)).toBe(DANGER);
    expect(scoreColor(null)).toBe(MUTED);
  });

  it('runStatusColor distinguishes terminal vs running states', () => {
    expect(runStatusColor(TestRunStatus.Completed)).toBe(SUCCESS);
    expect(runStatusColor(TestRunStatus.Failed)).toBe(DANGER);
    expect(runStatusColor(TestRunStatus.Pending)).toBe(MUTED);
  });
});

describe('isDivergent / scoreLabel', () => {
  it('is divergent only with both a pass and a fail', () => {
    expect(isDivergent([true, false])).toBe(true);
    expect(isDivergent([true, true])).toBe(false);
    expect(isDivergent([])).toBe(false);
  });

  it('labels an ordinal score, falling back to a dash for null', () => {
    expect(scoreLabel(5)).toBe(EvaluationScore.Excellent);
    expect(scoreLabel(1)).toBe(EvaluationScore.Terrible);
    expect(scoreLabel(null)).toBe('—');
  });
});
