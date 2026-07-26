// Pure verdict / scoring / status / color primitives for test runs. No JSX, no I/O.
// These are the cross-cutting "how a test result is judged" rules: a case's pass
// verdict, its score, run status, and the threshold→color mappings. Both the runs
// feature (features/runs/results.ts) and the Tracey assistant's live-run tool UI
// consume them, so they live in lib/ rather than inside a feature (no cross-feature
// import). Runs-specific aggregation (matrix pivot, SSE cache patching, fixture
// rollups) stays in features/runs/results.ts, which re-exports this module.

import { TestRunStatus, EvaluationScore } from '../api/models';
import type { EvaluationResultDto, TestResultDto, TestRunDto } from '../api/models';
import { PASS_RATE_WARN, PASS_RATE_DANGER, SCORE_WARN, SCORE_DANGER } from './constants';

const SUCCESS = 'var(--success)';
const WARN = 'var(--warn)';
const DANGER = 'var(--danger)';
const MUTED = 'var(--text-muted)';
const ACCENT = 'var(--accent-primary)';

const PASSING_SCORES = new Set<EvaluationScore>([
  EvaluationScore.Acceptable,
  EvaluationScore.Good,
  EvaluationScore.Excellent,
]);

// ── Evaluator-level ──────────────────────────────────────────────────────────

export const isErrored = (e: EvaluationResultDto): boolean => e.errorMessage !== null;

export const isEvalPass = (e: EvaluationResultDto): boolean =>
  !isErrored(e) && e.score !== null && PASSING_SCORES.has(e.score);

/** Maps a backend evaluation score (1..5) to its label, e.g. 5 → "Excellent". */
const SCORE_LABEL_BY_VALUE: Record<number, EvaluationScore> = {
  1: EvaluationScore.Terrible,
  2: EvaluationScore.Bad,
  3: EvaluationScore.Acceptable,
  4: EvaluationScore.Good,
  5: EvaluationScore.Excellent,
};

/**
 * Human label for an evaluator score. The backend serializes the {@link EvaluationScore}
 * enum as its underlying byte (1..5), so the value is an ordinal — not a 0..1 fraction.
 * Returns the score word ("Excellent"), the raw number for unexpected values, or "—" for null.
 */
export function scoreLabel(score: number | null): string {
  if (score === null) return '—';
  return SCORE_LABEL_BY_VALUE[score] ?? String(score);
}

// ── Case-level ───────────────────────────────────────────────────────────────

/**
 * A case passes only if every evaluator that produced a verdict passed. Errored evaluators are
 * excluded rather than counted as failures — a judge that crashed says nothing about the agent, and
 * counting it as a fail makes a broken evaluator look like a defect. Mirrors the backend's
 * `TestResultExtensions.IsPass`. `null` when nothing judged the case (no evaluators, or all errored).
 */
export function resultPass(r: TestResultDto): boolean | null {
  const judged = r.evaluations.filter(e => !isErrored(e));
  if (judged.length === 0) return null;
  return judged.every(isEvalPass);
}

/**
 * Case score = fraction of the evaluators that produced a verdict which passed (0..1). Errored
 * evaluators are out of both numerator and denominator, matching {@link resultPass}. `null` when
 * nothing judged the case.
 */
export function resultScore(r: TestResultDto): number | null {
  const judged = r.evaluations.filter(e => !isErrored(e));
  if (judged.length === 0) return null;
  return judged.filter(isEvalPass).length / judged.length;
}

/** Composite percent (0..100) from passed/total evaluators; `null` when total is 0. */
export function compositePercent(passed: number, total: number): number | null {
  return total > 0 ? Math.round((passed / total) * 100) : null;
}

/** Pass rate percent (0..100) from passed/total cases; `null` when total is 0. Same math as {@link compositePercent}. */
export const passRatePercent = compositePercent;

// ── Run status ───────────────────────────────────────────────────────────────

export const isActive = (s: TestRunStatus): boolean =>
  s === TestRunStatus.Running || s === TestRunStatus.Pending;

/**
 * True once every run in the group has settled (none pending/running). Single source
 * of truth for when comparative verdicts (winner badges, "best" coloring) become
 * authoritative — until then the UI shows progress, not conclusions.
 */
export const runsComplete = (runs: TestRunDto[]): boolean =>
  runs.length > 0 && !runs.some(r => isActive(r.status));

export function runStatusColor(s: TestRunStatus): string {
  if (s === TestRunStatus.Completed) return SUCCESS;
  if (s === TestRunStatus.Running) return ACCENT;
  if (s === TestRunStatus.Failed) return DANGER;
  return MUTED;
}

// ── Color mappers (single source of truth for thresholds) ────────────────────

/** Color for a pass rate on the 0..100 scale (PASS_RATE_WARN / PASS_RATE_DANGER). */
export function passRateColor(rate: number | null): string {
  if (rate === null) return MUTED;
  return rate >= PASS_RATE_WARN ? SUCCESS : rate >= PASS_RATE_DANGER ? WARN : DANGER;
}

/**
 * Semantic tone for a pass rate on the 0..100 scale — the class-based sibling of
 * {@link passRateColor}, for leaves that color via a semantic variant instead of a runtime hex.
 */
export function passRateTone(rate: number | null): 'success' | 'warn' | 'danger' | undefined {
  if (rate === null) return undefined;
  return rate >= PASS_RATE_WARN ? 'success' : rate >= PASS_RATE_DANGER ? 'warn' : 'danger';
}

/** Color for a score on the 0..1 scale (SCORE_WARN / SCORE_DANGER). */
export function scoreColor(score: number | null): string {
  if (score === null) return MUTED;
  return score >= SCORE_WARN ? SUCCESS : score >= SCORE_DANGER ? WARN : DANGER;
}

/** Color for a composite percent on the 0..100 scale, using the score thresholds. */
export const compositeColor = (percent: number | null): string =>
  scoreColor(percent === null ? null : percent / 100);

export function dotColor(pass: boolean | null): string {
  return pass === true ? SUCCESS : pass === false ? DANGER : MUTED;
}

// ── Divergence (model comparison) ────────────────────────────────────────────

/** True when a set of pass states contains both a pass and a fail. */
export const isDivergent = (states: boolean[]): boolean =>
  states.includes(true) && states.includes(false);
