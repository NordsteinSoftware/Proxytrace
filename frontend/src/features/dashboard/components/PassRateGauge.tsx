// Evaluation pass-rate gauge section.

import { Trans, useLingui } from '@lingui/react/macro';
import { msg } from '@lingui/core/macro';
import { SegmentedGauge } from '../../../components/charts';
import type { SummaryDto } from '../../../api/models';
import { useCountUp } from '../../../hooks/useCountUp';
import { Skeleton } from '../../../components/ui/Skeleton';
import { cn } from '../../../lib/cn';
import { fmtPct100 } from '../../../lib/format';
import { COL_HEADER_CLS, computePassRateGaugeStats, formatDeltaPt } from '../dashboardMeta';

interface PassRateGaugeProps {
  summary: SummaryDto | undefined;
  /** Recent test-run pass-rate cohorts (0–100, oldest → newest) — the honest source for the footer. */
  passRateTrend: number[] | undefined;
}

export function PassRateGauge({ summary, passRateTrend }: PassRateGaugeProps) {
  const { i18n } = useLingui();
  const passPct = summary?.overallPassRate == null ? null : Math.round(summary.overallPassRate * 100);
  // Sweep the gauge up on load — the arc fills as the number climbs to the real rate.
  const animatedPct = Math.round(useCountUp(passPct ?? 0));
  // Footer stats derive from real run history; with no cohorts the footer is dropped entirely
  // rather than showing a placeholder.
  const stats = computePassRateGaugeStats(passRateTrend ?? []);
  const lastRunDeltaPt = stats?.lastRunDeltaPt ?? null;

  return (
    <section data-testid="pass-rate-gauge" className="rounded-lg bg-card px-3.5 pt-2.5 pb-3 flex flex-col gap-1 shadow-[var(--shadow-card)]">
      <header>
        <h3 className="text-h2 font-semibold"><Trans>Evaluation pass rate</Trans></h3>
        <p className="text-body-sm text-muted mt-0.5 font-mono"><Trans>run cases in selected range</Trans></p>
      </header>
      <div className="flex justify-center">
        {summary === undefined
          ? <Skeleton height={180} className="w-[180px]" />
          : passPct === null
            ? <div className="h-[180px] flex items-center text-body text-muted" role="status"><Trans>No runs in range</Trans></div>
            : <SegmentedGauge value={animatedPct} size={180} label={i18n._(msg`PASS RATE`)} />}
      </div>
      {stats && (
        <div className="grid grid-cols-2 gap-2 mt-auto">
          <div data-testid="gauge-last-run" className="px-3 py-2.5 bg-card-2 rounded-md">
            <div className={COL_HEADER_CLS}><Trans>last run</Trans></div>
            <div
              className={cn(
                'text-h1 font-bold mt-0.5 tabular-nums',
                lastRunDeltaPt === null ? 'text-muted' : lastRunDeltaPt >= 0 ? 'text-success' : 'text-danger',
              )}
            >
              {lastRunDeltaPt === null ? '—' : formatDeltaPt(lastRunDeltaPt)}
            </div>
          </div>
          <div data-testid="gauge-best" className="px-3 py-2.5 bg-card-2 rounded-md">
            <div className={COL_HEADER_CLS}><Trans>best</Trans></div>
            <div className="text-h1 font-bold mt-0.5 tabular-nums text-primary">{fmtPct100(stats.best)}</div>
          </div>
        </div>
      )}
    </section>
  );
}
