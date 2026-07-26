// Pure per-case verdicts for a test run. No JSX, no I/O — unit-tested in case-verdict.spec.ts.
//
// Pass/fail semantics are NOT re-derived here: they come from lib/runResults.ts so a case's verdict
// matches the Runs UI exactly. What this module adds is DISAMBIGUATION. `resultPass` is tri-state
// (`boolean | null`) and `failingResults` keeps only `=== false`, so "absent from the failure list"
// silently unions passed / unjudged / not-in-run — and a case whose judge broke lands in whichever
// of those its *other* evaluators put it in, saying nothing about the broken judge. A red/green loop
// has to tell those apart: "the judge broke" is not evidence that the agent is wrong, and "I didn't
// see it in the failures" is not evidence that it passed.

import { TestRunStatus } from '../../../api/models';
import type { TestResultDto, TestRunDto } from '../../../api/models';
import { isErrored, resultPass } from '../../../lib/runResults';

/** What happened to one test case in one run. Only `pass` means the case passed. */
export type CaseVerdict =
  | 'pass'
  | 'fail'
  | 'unjudged'
  | 'evaluator-error'
  | 'not-in-run'
  | 'run-incomplete';

export interface CaseResult {
  testCaseId: string;
  verdict: CaseVerdict;
  /** The underlying result, or null when there is none (`not-in-run` / `run-incomplete`). */
  result: TestResultDto | null;
}

/**
 * Classifies one executed result. `evaluator-error` is checked BEFORE pass/fail on purpose: a broken
 * judge is reported as such rather than folded into the surviving evaluators' verdict, so a fix is
 * never theorized against a bug that was never demonstrated — nor a case called clean on evidence
 * that partly failed to materialize.
 */
function verdictOf(result: TestResultDto): CaseVerdict {
  if (result.evaluations.some(isErrored)) return 'evaluator-error';
  const pass = resultPass(result);
  if (pass === null) return 'unjudged';
  return pass ? 'pass' : 'fail';
}

/**
 * Per-case verdicts for a run.
 *
 * With `caseIds`, reports on exactly those cases **in the order given**, so a caller can zip the
 * answers back to its own ids; an id the run never executed comes back `not-in-run` rather than
 * being silently dropped. Without `caseIds`, returns only the non-passing cases (the
 * `get_run_failures` behavior this replaces), each carrying its specific verdict.
 *
 * A run that has not completed cannot answer the question at all, so every requested case is
 * `run-incomplete` — never `not-in-run`, which would wrongly read as "your case isn't in this suite".
 */
export function caseResults(run: TestRunDto, caseIds?: string[]): CaseResult[] {
  if (caseIds) {
    // An unfinished run cannot answer "did MY case pass?": a case with no result yet is not absent
    // from the suite, and one judged so far may still be re-run. Refuse the whole question rather
    // than hand back a verdict that would be read as settled.
    if (run.status !== TestRunStatus.Completed) {
      return caseIds.map((testCaseId) => ({
        testCaseId,
        verdict: 'run-incomplete' as const,
        result: null,
      }));
    }
    const byCase = new Map(run.results.map((r) => [r.testCaseId, r]));
    return caseIds.map((testCaseId) => {
      const result = byCase.get(testCaseId);
      return result
        ? { testCaseId, verdict: verdictOf(result), result }
        : { testCaseId, verdict: 'not-in-run' as const, result: null };
    });
  }

  // Nothing specific was asked about, so this is "show me what is going wrong" — answer it for a
  // running run too. The caller reports `runStatus` alongside, which is what carries the caveat
  // that more cases may yet fail.
  return run.results
    .map((result) => ({ testCaseId: result.testCaseId, verdict: verdictOf(result), result }))
    .filter((c) => c.verdict !== 'pass');
}
