import type { ToolCallMessagePartComponent } from '@assistant-ui/react';
import { Trans, useLingui } from '@lingui/react/macro';
import { AlertTriangleIcon } from '../../../../components/icons';
import { Badge, type BadgeVariant } from '../../../../components/ui/Badge';
import { EvaluationScore } from '../../../../api/models';
import type { EvaluationResultDto } from '../../../../api/models';
import { fmtPct100 } from '../../../../lib/format';
import { ToolUIFrame } from './ToolUIFrame';
import { CardOpenLink } from './CardOpenLink';
import { useArtifactResult } from '../../useArtifact';
import type { CaseResult, CaseVerdict } from '../../tools/case-verdict';

const SCORE_VARIANT: Record<EvaluationScore, BadgeVariant> = {
  [EvaluationScore.Excellent]: 'success',
  [EvaluationScore.Good]: 'success',
  [EvaluationScore.Acceptable]: 'success',
  [EvaluationScore.Bad]: 'danger',
  [EvaluationScore.Terrible]: 'danger',
};

const VERDICT_VARIANT: Record<CaseVerdict, BadgeVariant> = {
  pass: 'success',
  fail: 'danger',
  'evaluator-error': 'warn',
  unjudged: 'neutral',
  'not-in-run': 'neutral',
  'run-incomplete': 'neutral',
};

function EvaluationBadge({ evaluation }: { evaluation: EvaluationResultDto }) {
  const { t } = useLingui();
  if (evaluation.errorMessage) {
    return <Badge label={t`${evaluation.evaluatorName}: error`} variant="danger" size="sm" title={evaluation.errorMessage} />;
  }
  if (evaluation.score == null) {
    return <Badge label={`${evaluation.evaluatorName}: —`} variant="neutral" size="sm" />;
  }
  return (
    <Badge
      label={`${evaluation.evaluatorName}: ${evaluation.score}`}
      variant={SCORE_VARIANT[evaluation.score]}
      size="sm"
      title={evaluation.reasoning ?? undefined}
    />
  );
}

function VerdictBadge({ verdict }: { verdict: CaseVerdict }) {
  const { t } = useLingui();
  const label: Record<CaseVerdict, string> = {
    pass: t`passed`,
    fail: t`failed`,
    'evaluator-error': t`evaluator error`,
    unjudged: t`not scored`,
    'not-in-run': t`not in this run`,
    'run-incomplete': t`run unfinished`,
  };
  return <Badge label={label[verdict]} variant={VERDICT_VARIANT[verdict]} size="sm" />;
}

/**
 * Red/green summary for an expectation. Deliberately rendered next to the prose: the model can
 * narrate whatever it likes, but a contradicting verdict sits directly above the claim.
 */
function ExpectationBadge({ expect, cases }: { expect: 'pass' | 'fail'; cases: CaseResult[] }) {
  const { t } = useLingui();
  const met = cases.length > 0 && cases.every((c) => (c.verdict === 'pass') === (expect === 'pass'));
  if (!met) return <Badge label={t`unexpected`} variant="warn" size="sm" />;
  // GREEN/RED stay untranslated: they are the testing terms of art this card exists to report.
  // eslint-disable-next-line lingui/no-unlocalized-strings -- glossary term, per docs/i18n.md
  return <Badge label={expect === 'pass' ? 'GREEN' : 'RED'} variant={expect === 'pass' ? 'success' : 'danger'} size="sm" />;
}

/** Inline renderer for the `get_case_results` tool result: how named cases fared in a run. */
export const CaseResultsToolUI: ToolCallMessagePartComponent = ({ result, status, isError }) => {
  const { t } = useLingui();
  // eslint-disable-next-line lingui/no-unlocalized-strings -- artifact kind token, not UI copy
  const { state, data } = useArtifactResult('case-results', result, status, isError);
  return (
    <ToolUIFrame
      state={state}
      icon={<AlertTriangleIcon size={14} />}
      title={data ? t`Case results · ${data.suiteName ?? data.agentName}` : t`Case results`}
      cornerAccessory={data ? <CardOpenLink to={`/runs?run=${data.runId}`} /> : undefined}
      pendingLabel={t`Reading the run…`}
      testId="tracey-case-results"
    >
      {data && (
        <div className="flex flex-col gap-3">
          <div className="flex flex-wrap items-center gap-2 text-body-sm text-muted">
            <Trans>
              {data.cases.length} of {data.totalCases} cases ·{' '}
              <span className="font-mono tabular-nums">{fmtPct100(data.passRate)}</span> pass rate
            </Trans>
            {data.expect && <ExpectationBadge expect={data.expect} cases={data.cases} />}
          </div>
          {data.cases.length === 0 ? (
            <div className="text-body-sm text-success"><Trans>All cases passed.</Trans></div>
          ) : (
            <div className="flex flex-col divide-y divide-border-subtle">
              {data.cases.map((testCase) => (
                <div key={testCase.testCaseId} className="flex flex-col gap-1.5 py-2.5 first:pt-0 last:pb-0">
                  <div className="text-title text-primary">
                    {testCase.result?.testCaseSummary ?? testCase.testCaseId}
                  </div>
                  {testCase.result && (
                    <div className="line-clamp-2 border-l-2 border-border pl-2.5 font-mono text-body-sm text-secondary">
                      {testCase.result.actualResponse || t`(empty response)`}
                    </div>
                  )}
                  <div className="flex flex-wrap items-center gap-1.5">
                    <VerdictBadge verdict={testCase.verdict} />
                    {(testCase.result?.evaluations ?? []).map((evaluation) => (
                      <EvaluationBadge key={evaluation.evaluatorId} evaluation={evaluation} />
                    ))}
                  </div>
                </div>
              ))}
            </div>
          )}
        </div>
      )}
    </ToolUIFrame>
  );
};
