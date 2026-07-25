import { Link } from 'react-router';
import { Trans, useLingui } from '@lingui/react/macro';
import { CheckIcon, PlayIcon, XIcon } from '../../../../components/icons';
import { Badge } from '../../../../components/ui/Badge';
import { Spinner } from '../../../../components/ui/Spinner';
import { ProgressBar } from '../../../../components/ui/ProgressBar';
import { agentColor } from '../../../../lib/colors';
import { TestRunStatus, type TestRunGroupDto } from '../../../../api/models';
import { isActive, passRateColor } from '../../../../lib/runResults';
import { ToolUIFrame } from './ToolUIFrame';
import { RUN_STATUS_LABEL, RUN_STATUS_VARIANT } from './badge-variants';
import { useLiveTestRunGroup } from './useLiveTestRunGroup';
import { groupProgress } from './live-run-progress';

const ACCENT = 'var(--accent-primary)';

/**
 * The live test-run card Tracey renders after starting a run. Streams completion + pass/fail as
 * cases finish (queued → running → completed/failed/cancelled) and, once done, links to the run.
 */
export function LiveRunCard({ initial }: { initial: TestRunGroupDto }) {
  const { t, i18n } = useLingui();
  const group = useLiveTestRunGroup(initial);
  const { total, completed, passed, failed, passPercent } = groupProgress(group);
  const running = isActive(group.status);
  const color = agentColor(group.agentId);
  const primaryRun = group.runs[0];

  return (
    <ToolUIFrame
      state="ready"
      title={`${group.suiteName} → ${group.agentName}`}
      icon={<PlayIcon size={14} />}
      accentBar={color}
      live={running}
      testId="tracey-run-progress-card"
    >
      <div className="flex flex-col gap-2.5">
        <div className="flex flex-wrap items-center gap-1.5">
          <Badge label={i18n._(RUN_STATUS_LABEL[group.status])} variant={RUN_STATUS_VARIANT[group.status]} size="sm" />
          <Badge label={t`${completed}/${total} cases`} variant="neutral" size="sm" />
          {passPercent !== null && (
            <Badge label={t`${passPercent}% pass`} variant="neutral" size="sm" />
          )}
        </div>

        <ProgressBar
          value={completed}
          max={total || 1}
          color={running ? ACCENT : passRateColor(passPercent ?? 0)}
          showLabel
        />

        <div
          className="mt-0.5 flex items-center gap-2 border-t border-hairline pt-2 text-body-sm"
          data-testid="tracey-run-progress-status"
        >
          {running && (
            <>
              <Spinner size={12} />
              <span className="text-secondary">
                <Trans>Running… {completed}/{total} cases</Trans>
              </span>
            </>
          )}
          {group.status === TestRunStatus.Completed && (
            <>
              <span className="text-success"><CheckIcon size={14} /></span>
              <span className="text-secondary">
                {failed > 0
                  ? t`${passed}/${total} passed · ${failed} failed`
                  : t`${passed}/${total} passed`}
              </span>
            </>
          )}
          {group.status === TestRunStatus.Failed && (
            <>
              <span className="text-danger"><XIcon size={14} /></span>
              <span className="text-secondary"><Trans>Run failed.</Trans></span>
            </>
          )}
          {group.status === TestRunStatus.Cancelled && (
            <span className="text-muted"><Trans>Run cancelled.</Trans></span>
          )}
          {primaryRun && (
            <Link
              to={`/runs?run=${primaryRun.id}`}
              data-testid="tracey-run-progress-link"
              className="ml-auto font-medium text-accent hover:text-[var(--accent-hover)] focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[color-mix(in_srgb,var(--accent-primary)_60%,transparent)]"
            >
              {running ? t`Open run` : t`View run`}
            </Link>
          )}
        </div>
      </div>
    </ToolUIFrame>
  );
}
