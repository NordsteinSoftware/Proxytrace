// A "cohort" is all the runs in a group that share one endpoint — the N samples of that endpoint.
// Everything that used to iterate `group.runs` pivots to iterate cohorts: the matrix shows one column
// per endpoint, aggregating its samples per case (pass-fraction X/N + avg score) and letting the user
// drill into the individual sample runs. When every endpoint has exactly one run (sampleCount=1, the
// shape of every pre-sampling group) each cohort holds a single run and the UI renders exactly as
// before. Pure derive helpers — no JSX, no I/O; unit-tested in cohorts.spec.ts.

import type { TestRunDto } from '../../api/models';
import { TestRunStatus } from '../../api/models';
import {
  buildMatrixCell,
  isDivergent,
  type CellStatus,
  type MatrixCell,
} from './results';
import type { LiveProgress } from './live';

// ── Cohort grouping ───────────────────────────────────────────────────────────

/** Minimal run shape a cohort needs — satisfied by both `TestRunDto` and `TestRunSummaryDto`. */
export interface CohortRunLike {
  id: string;
  endpointId: string;
  endpointName: string;
  sampleIndex: number;
  passedCases: number;
  failedCases: number;
}

export interface Cohort<R extends CohortRunLike> {
  endpointId: string;
  endpointName: string;
  /** The cohort's runs, ordered by sample index. */
  runs: R[];
  sampleCount: number;
  /** The sample that stands in for the cohort in drill-downs (median pass count, tie → lowest index). */
  representative: R;
}

/**
 * Groups runs by endpoint into cohorts, preserving the order endpoints first appear (so columns keep
 * their existing left-to-right order). One run per endpoint → one cohort per run, `sampleCount === 1`.
 */
export function buildCohorts<R extends CohortRunLike>(runs: readonly R[]): Cohort<R>[] {
  const byEndpoint = new Map<string, R[]>();
  for (const run of runs) {
    const list = byEndpoint.get(run.endpointId);
    if (list) list.push(run);
    else byEndpoint.set(run.endpointId, [run]);
  }
  return [...byEndpoint.entries()].map(([endpointId, group]) => {
    const ordered = [...group].sort((a, b) => a.sampleIndex - b.sampleIndex);
    return {
      endpointId,
      endpointName: ordered[0].endpointName,
      runs: ordered,
      sampleCount: ordered.length,
      representative: selectRepresentative(ordered),
    };
  });
}

/**
 * The median sample by judged pass count (tie → lowest sample index). Mirrors the backend's
 * representative-run rule so UI drill-down and the optimization loop's evidence agree on which sample
 * is "typical" — deliberately not the best, worst, or first sample.
 */
function selectRepresentative<R extends CohortRunLike>(ordered: R[]): R {
  const judged = ordered.filter(r => r.passedCases + r.failedCases > 0);
  const pool = judged.length > 0 ? judged : ordered;
  const byPass = [...pool].sort((a, b) => a.passedCases - b.passedCases || a.sampleIndex - b.sampleIndex);
  return byPass[Math.floor((byPass.length - 1) / 2)];
}

// ── Cohort matrix cells (case × endpoint, averaged across samples) ─────────────

/** A cohort cell's verdict across its judged samples: all pass, all fail, a mix (flaky), or none judged. */
export type CohortVerdict = 'pass' | 'mixed' | 'fail' | null;

export interface CohortCell {
  cohort: Cohort<TestRunDto>;
  /** Per-sample cells (by sample index), reusing the single-run cell builder. */
  samples: MatrixCell[];
  sampleCount: number;
  /** Samples whose case passed. */
  passCount: number;
  /** Samples that produced a verdict (pass or fail). */
  judgedCount: number;
  verdict: CohortVerdict;
  /** Mean per-sample score (0..1) across samples that have one; `null` when none scored. */
  avgScore: number | null;
  status: CellStatus;
  /** Samples whose case has finished — for the in-flight "k/N" progress hint. */
  doneSamples: number;
}

export interface CohortRow {
  caseId: string;
  summary: string;
  cells: CohortCell[];
  /** Endpoints disagree (a pass and a fail) **or** any cohort is internally flaky. */
  divergent: boolean;
  /** Any cohort's samples disagree among themselves (a `mixed` verdict). */
  flaky: boolean;
  /** Cohorts whose verdict is fail or mixed. */
  failCount: number;
}

/**
 * Pivots a group's cohorts into a (case × endpoint) grid in stable suite order. Each cell aggregates
 * the cohort's per-sample cells. `live` overlays in-flight progress just like the single-run matrix.
 */
export function buildCohortRows(cohorts: Cohort<TestRunDto>[], live?: LiveProgress): CohortRow[] {
  const caseMap = new Map<string, string>();
  cohorts.forEach(cohort => cohort.runs.forEach(run => {
    run.testCases.forEach(tc => { if (!caseMap.has(tc.id)) caseMap.set(tc.id, tc.summary); });
    run.results.forEach(r => { if (!caseMap.has(r.testCaseId)) caseMap.set(r.testCaseId, r.testCaseSummary); });
  }));

  return [...caseMap.entries()].map(([caseId, summary]) => {
    const cells = cohorts.map(cohort => buildCohortCell(cohort, caseId, live));
    // Cross-endpoint divergence considers only decided cohorts (a pass and a fail); flakiness is a
    // mixed cohort. Either makes the row "divergent" for the filter/stripe.
    const decided = cells.flatMap(c => (c.verdict === 'pass' ? [true] : c.verdict === 'fail' ? [false] : []));
    const flaky = cells.some(c => c.verdict === 'mixed');
    return {
      caseId,
      summary,
      cells,
      divergent: isDivergent(decided) || flaky,
      flaky,
      failCount: cells.filter(c => c.verdict === 'fail' || c.verdict === 'mixed').length,
    };
  });
}

function buildCohortCell(cohort: Cohort<TestRunDto>, caseId: string, live?: LiveProgress): CohortCell {
  const samples = cohort.runs.map(run => buildMatrixCell(run, caseId, live));
  const passCount = samples.filter(s => s.pass === true).length;
  const judgedCount = samples.filter(s => s.pass !== null).length;
  const scores = samples.map(s => s.score).filter((x): x is number => x !== null);
  const avgScore = scores.length > 0 ? scores.reduce((a, b) => a + b, 0) / scores.length : null;
  const verdict: CohortVerdict = judgedCount === 0
    ? null
    : passCount === judgedCount ? 'pass'
      : passCount === 0 ? 'fail'
        : 'mixed';
  const status: CellStatus = samples.every(s => s.status === 'done')
    ? 'done'
    : samples.every(s => s.status === 'pending') ? 'pending' : 'running';

  return {
    cohort,
    samples,
    sampleCount: cohort.sampleCount,
    passCount,
    judgedCount,
    verdict,
    avgScore,
    status,
    doneSamples: samples.filter(s => s.status === 'done').length,
  };
}

/**
 * A cohort column's pass rate (0..100): the mean of its per-case pass fractions over judged cases.
 * For a single-sample cohort this equals the run's judged-denominator pass rate, so the footer reads
 * identically to before. `null` when the cohort has no judged cases.
 */
export function cohortPassRate(rows: CohortRow[], cohortIndex: number): number | null {
  const fractions = rows
    .map(row => row.cells[cohortIndex])
    .filter(cell => cell.judgedCount > 0)
    .map(cell => cell.passCount / cell.judgedCount);
  if (fractions.length === 0) return null;
  return Math.round((fractions.reduce((a, b) => a + b, 0) / fractions.length) * 100);
}

// ── Matrix filter / sort (cohort rows) ─────────────────────────────────────────

export type CaseChange = 'regressions' | 'improvements' | 'unchanged';
export type MatrixFilter = 'all' | 'divergent' | 'flaky' | 'failing' | 'passing' | CaseChange;
export type MatrixSort = 'order' | 'worst';

/** Compare only complete, consistently judged cohorts; missing or flaky data is not unchanged. */
export function caseChange(row: CohortRow, baselineEndpointId: string | null): CaseChange | null {
  const baseline = row.cells.find(c => c.cohort.endpointId === baselineEndpointId);
  if (!baseline || row.cells.length < 2 || row.cells.some(c =>
    c.status !== 'done' || c.judgedCount !== c.sampleCount || c.verdict === null || c.verdict === 'mixed'
    || c.cohort.runs.some(r => r.status !== TestRunStatus.Completed))) return null;
  if (row.cells.every(c => c.verdict === baseline.verdict)) return 'unchanged';
  return baseline.verdict === 'pass' ? 'regressions' : 'improvements';
}

const rowFailing = (row: CohortRow): boolean => row.cells.some(c => c.verdict === 'fail' || c.verdict === 'mixed');
const rowPassing = (row: CohortRow): boolean =>
  row.cells.some(c => c.verdict !== null) && row.cells.filter(c => c.verdict !== null).every(c => c.verdict === 'pass');
const rowMinScore = (row: CohortRow): number => {
  const scores = row.cells.filter(c => c.verdict !== null).map(c => c.avgScore ?? 0);
  return scores.length ? Math.min(...scores) : 1;
};

export interface MatrixCounts { all: number; divergent: number; flaky: number; failing: number; passing: number; regressions: number; improvements: number; unchanged: number; }

export const matrixCounts = (rows: CohortRow[], baselineEndpointId: string | null = null): MatrixCounts => ({
  all: rows.length,
  divergent: rows.filter(r => r.divergent).length,
  flaky: rows.filter(r => r.flaky).length,
  failing: rows.filter(rowFailing).length,
  passing: rows.filter(rowPassing).length,
  regressions: rows.filter(r => caseChange(r, baselineEndpointId) === 'regressions').length,
  improvements: rows.filter(r => caseChange(r, baselineEndpointId) === 'improvements').length,
  unchanged: rows.filter(r => caseChange(r, baselineEndpointId) === 'unchanged').length,
});

/**
 * Applies the toolbar filter, then orders the rows (stable suite order in; see {@link buildCohortRows}).
 * `freezeOrder` (set while a run is active) keeps stable order so rows don't reshuffle mid-run; otherwise
 * `'order'` surfaces divergent rows first then most failures then alphabetical, `'worst'` orders by
 * lowest per-cell score.
 */
export function filterSortMatrixRows(
  rows: CohortRow[],
  filter: MatrixFilter,
  sort: MatrixSort,
  freezeOrder = false,
  baselineEndpointId: string | null = null,
): CohortRow[] {
  let out = rows;
  if (filter === 'divergent') out = out.filter(r => r.divergent);
  else if (filter === 'flaky') out = out.filter(r => r.flaky);
  else if (filter === 'failing') out = out.filter(rowFailing);
  else if (filter === 'passing') out = out.filter(rowPassing);
  else if (filter === 'regressions' || filter === 'improvements' || filter === 'unchanged') {
    out = out.filter(r => caseChange(r, baselineEndpointId) === filter);
  }

  if (freezeOrder) return out;
  if (sort === 'worst') return [...out].sort((a, b) => rowMinScore(a) - rowMinScore(b));
  return [...out].sort((a, b) =>
    (Number(b.divergent) - Number(a.divergent)) || (b.failCount - a.failCount) || a.summary.localeCompare(b.summary));
}
