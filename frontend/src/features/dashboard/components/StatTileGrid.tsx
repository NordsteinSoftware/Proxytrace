// Grid of KPI stat tiles in the hero row.

import { useLingui } from '@lingui/react/macro';
import { ActivityIcon, ClockIcon, ZapIcon, TargetIcon, ServerIcon, SigmaIcon } from '../../../components/icons';
import type { SummaryDto, LiveTelemetryDto, DashboardTrendsDto } from '../../../api/models';
import { fmtLatency } from '../../../lib/format';
import { callSeriesDelta, passRateDelta, type LatencyStats } from '../dashboardMeta';
import { StatTile } from './StatTile';

interface StatTileGridProps {
  summary: SummaryDto | undefined;
  telemetry: LiveTelemetryDto | undefined;
  trends: DashboardTrendsDto | undefined;
  latencyStats: LatencyStats | null;
}

export function StatTileGrid({ summary, telemetry, trends, latencyStats }: StatTileGridProps) {
  const { t } = useLingui();
  const passPct = summary?.overallPassRate == null ? null : Math.round(summary.overallPassRate * 100);
  // Real deltas from the trend series that already feed the sparklines below — first vs last half
  // of the window (percentage-point change for the pass rate). A series with too little data
  // yields no chip rather than a fabricated one.
  const tracesDelta = callSeriesDelta(trends?.traces ?? []);
  const latencyDelta = callSeriesDelta(trends?.latencyMs ?? []);
  const passDelta = passRateDelta(trends?.passRate ?? []);

  return (
    <div data-testid="dashboard-stat-tiles" className="grid grid-cols-2 lg:grid-cols-3 gap-2">
      <StatTile
        accent
        testId="stat-tile-traces"
        icon={<ActivityIcon size={11} />}
        label={t`Traces`}
        value={(summary?.totalCalls ?? 0).toLocaleString()}
        countTo={summary?.totalCalls ?? 0}
        formatCount={v => Math.round(v).toLocaleString()}
        sub={t`LLM calls captured`}
        delta={tracesDelta?.text}
        deltaUp={tracesDelta?.up}
        trace={trends?.traces}
        traceColor="var(--accent-primary)"
        traceFormat={v => t`${Math.round(v)} traces`}
      />
      <StatTile
        testId="stat-tile-latency"
        icon={<ClockIcon size={11} />}
        label={t`Avg Latency`}
        value={String(Math.round(summary?.avgLatencyMs ?? 0))}
        countTo={summary?.avgLatencyMs ?? 0}
        formatCount={v => String(Math.round(v))}
        unit={t`ms`}
        sub={latencyStats ? t`p95 ${fmtLatency(latencyStats.p95)} · p99 ${fmtLatency(latencyStats.p99)}` : '—'}
        delta={latencyDelta?.text}
        deltaUp={latencyDelta?.up}
        trace={trends?.latencyMs}
        traceColor="var(--warn)"
        traceFormat={v => fmtLatency(v)}
      />
      <StatTile
        testId="stat-tile-throughput"
        icon={<ZapIcon size={11} />}
        label={t`Live throughput`}
        value={telemetry ? String(Math.round(telemetry.tokensPerSecond)) : '—'}
        countTo={telemetry ? telemetry.tokensPerSecond : undefined}
        formatCount={v => String(Math.round(v))}
        unit={t`t/s`}
        sub={telemetry ? t`last 5 min · p95 ${fmtLatency(telemetry.p95Ms)}` : t`awaiting telemetry`}
        traceColor="var(--teal)"
      />
      <StatTile
        testId="stat-tile-pass-rate"
        icon={<TargetIcon size={11} />}
        label={t`Pass Rate`}
        value={passPct === null ? '—' : String(passPct)}
        countTo={passPct ?? undefined}
        formatCount={v => String(Math.round(v))}
        unit={passPct === null ? undefined : '%'}
        sub={summary === undefined ? '—' : passPct === null ? t`No runs in range` : t`run cases in selected range`}
        delta={passPct === null ? undefined : passDelta?.text}
        deltaUp={passDelta?.up}
        trace={passPct === null ? undefined : trends?.passRate}
        traceColor="var(--success)"
        traceFormat={v => t`${v.toFixed(0)}% pass`}
      />
      <StatTile
        testId="stat-tile-queue"
        icon={<ServerIcon size={11} />}
        label={t`Queue`}
        value={telemetry ? String(telemetry.queueDepth) : '—'}
        countTo={telemetry ? telemetry.queueDepth : undefined}
        formatCount={v => String(Math.round(v))}
        sub={t`current ingestion backlog`}
        traceColor="var(--teal)"
      />
      <StatTile
        testId="stat-tile-p95"
        icon={<SigmaIcon size={11} />}
        label={t`p95 Latency`}
        value={latencyStats ? String(Math.round(latencyStats.p95)) : '—'}
        countTo={latencyStats?.p95}
        formatCount={v => String(Math.round(v))}
        unit={t`ms`}
        sub={latencyStats ? t`p99 ${fmtLatency(latencyStats.p99)}` : t`awaiting samples`}
        traceColor="var(--warn)"
      />
    </div>
  );
}
