// Pure derive helpers for Tracey's tuning tools (`compare_runs`, and `clip` for every digest).
// No JSX, no I/O — unit-tested in run-analysis.spec.ts. Pass/fail semantics are NOT
// re-derived here: they come from lib/runResults.ts so a case's verdict is
// computed identically to the Runs UI. Per-case verdicts live in case-verdict.ts.

import type { TestRunDto } from '../../../api/models';
import { resultPass } from '../../../lib/runResults';

/** Truncates a string for a model-facing digest, marking the cut. */
export function clip(value: string, max: number): string {
  const trimmed = value.trim();
  return trimmed.length <= max ? trimmed : `${trimmed.slice(0, max)}…`;
}

/** One case's movement between a baseline and a candidate run. */
export type CaseMovement = 'fixed' | 'regressed' | 'still-failing' | 'still-passing';

export interface ComparedCase {
  testCaseId: string;
  summary: string;
  movement: CaseMovement;
}

/** The full payload `compare_runs` stores for its card. */
export interface RunComparison {
  baseline: { runId: string; agentName: string; endpointName: string; passRate: number };
  candidate: { runId: string; agentName: string; endpointName: string; passRate: number };
  suiteName: string | null;
  cases: ComparedCase[];
  fixed: number;
  regressed: number;
  stillFailing: number;
  stillPassing: number;
  /** Cases present in only one of the runs (suite changed between them); excluded from `cases`. */
  unmatched: number;
}

function movementOf(before: boolean | null, after: boolean | null): CaseMovement | null {
  if (before == null || after == null) return null; // unjudged on either side — not comparable
  if (before && after) return 'still-passing';
  if (!before && !after) return 'still-failing';
  return before ? 'regressed' : 'fixed';
}

/**
 * Joins two runs' results by test case and classifies each case's movement. Runs over different
 * suites still work — cases missing from either side are counted as `unmatched`, not guessed.
 */
export function compareRuns(baseline: TestRunDto, candidate: TestRunDto): RunComparison {
  const after = new Map(candidate.results.map((r) => [r.testCaseId, r]));
  const cases: ComparedCase[] = [];
  let unmatched = 0;

  for (const beforeResult of baseline.results) {
    const afterResult = after.get(beforeResult.testCaseId);
    if (!afterResult) {
      unmatched++;
      continue;
    }
    after.delete(beforeResult.testCaseId);
    const movement = movementOf(resultPass(beforeResult), resultPass(afterResult));
    if (!movement) {
      unmatched++;
      continue;
    }
    cases.push({ testCaseId: beforeResult.testCaseId, summary: beforeResult.testCaseSummary, movement });
  }
  unmatched += after.size;

  const count = (movement: CaseMovement) => cases.filter((c) => c.movement === movement).length;
  return {
    baseline: {
      runId: baseline.id,
      agentName: baseline.agentName,
      endpointName: baseline.endpointName,
      passRate: baseline.passRate,
    },
    candidate: {
      runId: candidate.id,
      agentName: candidate.agentName,
      endpointName: candidate.endpointName,
      passRate: candidate.passRate,
    },
    suiteName: baseline.suiteName ?? candidate.suiteName,
    cases,
    fixed: count('fixed'),
    regressed: count('regressed'),
    stillFailing: count('still-failing'),
    stillPassing: count('still-passing'),
    unmatched,
  };
}
