import { Link } from 'react-router';
import { Trans, useLingui } from '@lingui/react/macro';
import { msg } from '@lingui/core/macro';
import type { MessageDescriptor } from '@lingui/core';
import { ActivityIcon, CheckIcon, ClockIcon, ExternalLinkIcon, XIcon } from '../../components/icons';
import { cn } from '../../lib/cn';
import { Card } from '../../components/ui/Card';
import type { AbTestRunSummaryDto } from '../../api/models';
import { TestRunStatus } from '../../api/models';
import { fmtDuration } from '../../lib/format';
import { TONE_TEXT, TONE_BG, TONE_SUBTLE_BG, type DisplayTone } from './shared';

interface Props {
  ab: AbTestRunSummaryDto | null;
  expectedPassRateDelta?: number | null;
}

const STATUS_META: Record<TestRunStatus, { label: MessageDescriptor; tone: DisplayTone; icon: React.ReactNode; pulse: boolean }> = {
  [TestRunStatus.Pending]:   { label: msg`Queued`,    tone: 'teal',    icon: <ClockIcon size={11}/>,    pulse: true  },
  [TestRunStatus.Running]:   { label: msg`Running`,   tone: 'teal',    icon: <ActivityIcon size={11}/>, pulse: true  },
  [TestRunStatus.Completed]: { label: msg`Completed`, tone: 'success', icon: <CheckIcon size={11}/>,    pulse: false },
  [TestRunStatus.Failed]:    { label: msg`Failed`,    tone: 'danger',  icon: <XIcon size={11}/>,        pulse: false },
  [TestRunStatus.Cancelled]: { label: msg`Cancelled`, tone: 'muted',   icon: <XIcon size={11}/>,        pulse: false },
};

export function AbTestHero({ ab, expectedPassRateDelta }: Props) {
  const { t, i18n } = useLingui();
  if (!ab) {
    return (
      <Card elevation="flat" padding="md">
        <div className="flex items-center gap-3">
          <div className="size-8 rounded-md bg-card-2 flex items-center justify-center text-muted">
            <ClockIcon size={14}/>
          </div>
          <div className="flex-1 min-w-0">
            <div className="text-title font-semibold text-primary mb-0.5"><Trans>No A/B test attached</Trans></div>
            <div className="text-body-sm text-muted"><Trans>Proposal has not been benchmarked against a test suite yet.</Trans></div>
          </div>
        </div>
      </Card>
    );
  }

  const meta = STATUS_META[ab.status];
  const isRunning = ab.status === TestRunStatus.Running || ab.status === TestRunStatus.Pending;
  const hasResults = ab.completedCases > 0;
  const passRate = Math.round(ab.passRate);
  const deltaPts = expectedPassRateDelta != null ? Math.round(expectedPassRateDelta * 100) : null;

  const passPct    = ab.totalCases > 0 ? (ab.passedCases / ab.totalCases) * 100 : 0;
  const failPct    = ab.totalCases > 0 ? (ab.failedCases / ab.totalCases) * 100 : 0;
  const pendingPct = Math.max(0, 100 - passPct - failPct);

  const passTone: DisplayTone = !hasResults ? 'muted' : passRate >= 80 ? 'success' : passRate >= 50 ? 'accent' : 'danger';
  const deltaTone: DisplayTone | null = deltaPts == null || deltaPts === 0
    ? null
    // eslint-disable-next-line lingui/no-unlocalized-strings -- DisplayTone token, not UI copy
    : deltaPts > 0 ? 'success' : 'danger';

  return (
    <Card
      elevation="raised"
      padding="none"
      className={cn('overflow-hidden', isRunning && 'streaming-border')}
    >
      {/* Header strip */}
      <div className="flex items-center gap-2 px-4 py-2.5 border-b border-hairline">
        <span
          className={cn('inline-flex items-center gap-1.5 rounded-none px-2 py-0.5 text-body-sm font-semibold', TONE_SUBTLE_BG[meta.tone], TONE_TEXT[meta.tone])}
        >
          <span
            className={cn('inline-block size-1.5 rounded-full', TONE_BG[meta.tone], meta.pulse && 'pulse-dot')}
          />
          <Trans>A/B test</Trans> · {i18n._(meta.label)}
        </span>
        <span className="mono text-caption text-muted">{ab.id.slice(0, 8)}</span>
        <Link
          to={`/runs?run=${ab.id}`}
          className="ml-auto inline-flex items-center gap-1 text-body-sm text-secondary hover:text-primary transition-colors"
        >
          <Trans>View run</Trans> <ExternalLinkIcon size={11}/>
        </Link>
      </div>

      {/* Hero numbers */}
      <div className="flex items-stretch gap-6 px-4 py-4">
        <div className="flex-1 min-w-0">
          <div className="text-caption text-secondary font-semibold uppercase tracking-[0.07em] mb-1"><Trans>Pass rate</Trans></div>
          <div className="flex items-baseline gap-2.5">
            <span
              className={cn('text-display font-bold tracking-[-0.02em] mono leading-none', TONE_TEXT[passTone])}
            >
              {hasResults ? `${passRate}%` : '—'}
            </span>
            {deltaTone && deltaPts != null && (
              <span
                className={cn('mono text-body-sm font-semibold', TONE_TEXT[deltaTone])}
              >
                {deltaPts > 0 ? '+' : '−'}{Math.abs(deltaPts)}<Trans>pt</Trans>
              </span>
            )}
            {!deltaTone && deltaPts == null && (
              <span className="text-body-sm text-muted"><Trans>no delta predicted</Trans></span>
            )}
          </div>
        </div>

        <div className="w-px bg-hairline"/>

        <div className="grid grid-cols-2 gap-x-6 gap-y-1.5 items-center">
          <Stat label={t`Sample`}   value={`${ab.completedCases}/${ab.totalCases}`}/>
          <Stat label={t`Duration`} value={fmtDuration(ab.durationMs)}/>
          <Stat label={t`Passed`}   value={ab.passedCases} tone="success"/>
          <Stat label={t`Failed`}   value={ab.failedCases} tone="danger"/>
        </div>
      </div>

      {/* Segmented progress bar */}
      <div className="px-4 pb-4">
        <div className="flex h-1.5 overflow-hidden bg-card-2">
          {passPct > 0    && <div className="bg-success"                                                           style={{ width: `${passPct}%` }}/>}
          {failPct > 0    && <div className="bg-danger"                                                            style={{ width: `${failPct}%` }}/>}
          {pendingPct > 0 && <div className="bg-[color-mix(in_srgb,var(--text-muted)_30%,transparent)]"           style={{ width: `${pendingPct}%` }}/>}
        </div>
        {hasResults && (
          <div className="flex items-center gap-3 mt-1.5 text-caption text-muted">
            <LegendDot tone="success" label={t`${ab.passedCases} passed`}/>
            <LegendDot tone="danger"  label={t`${ab.failedCases} failed`}/>
            {ab.totalCases - ab.completedCases > 0 && (
              <LegendDot
                tone="neutral"
                label={t`${ab.totalCases - ab.completedCases} pending`}
              />
            )}
          </div>
        )}
      </div>
    </Card>
  );
}

function Stat({ label, value, tone }: { label: string; value: string | number; tone?: DisplayTone }) {
  return (
    <>
      <span className="text-caption text-secondary font-medium uppercase tracking-[0.07em]">{label}</span>
      <span className={cn('mono text-body font-semibold', tone ? TONE_TEXT[tone] : 'text-primary')}>
        {value}
      </span>
    </>
  );
}

function LegendDot({ tone, label }: { tone: DisplayTone; label: string }) {
  return (
    <span className="inline-flex items-center gap-1 mono">
      <span className={cn('inline-block size-1.5 rounded-full', TONE_BG[tone])}/>
      {label}
    </span>
  );
}
