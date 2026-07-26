import { fmtTokens, fmtLatency, fmtPct, fmtCost, cachedPct } from '../../../lib/format';
import type { AgentCallSummaryDto } from '../../../api/models';
import { useLingui } from '@lingui/react/macro';

interface StatTileProps {
  label: string;
  value: string;
  sub: string;
  testId: string;
}

function StatTile({ label, value, sub, testId }: StatTileProps) {
  return (
    <div data-testid={testId} className="bg-card rounded-lg px-3 py-2 shadow-[var(--shadow-card)]">
      <div className="text-caption text-muted">{label}</div>
      <div className="text-h1 font-semibold text-primary tabular-nums leading-tight">{value}</div>
      <div className="text-caption text-muted truncate">{sub}</div>
    </div>
  );
}

interface Props {
  /** Null while the aggregate is still loading. */
  stats: AgentCallSummaryDto | null;
}

/**
 * Stats band for every trace matching the current filters — not just the loaded rows. The list
 * scrolls continuously, so a slice-scoped figure would climb as the reader scrolled and mean nothing.
 */
export function TraceSummary({ stats }: Props) {
  const { t } = useLingui();
  if (stats === null || stats.count === 0) return null;

  const totalTokens = stats.inputTokens + stats.outputTokens;
  const cached = cachedPct(stats.cachedInputTokens, stats.inputTokens);
  const errorRate = stats.count > 0 ? stats.errorCount / stats.count : 0;

  return (
    <div
      data-testid="trace-summary"
      className="fade-up grid gap-2 shrink-0 [animation-delay:40ms] grid-cols-[repeat(auto-fit,minmax(150px,1fr))]"
    >
      <StatTile
        testId="trace-summary-count"
        label={t`Traces`}
        value={stats.count.toLocaleString()}
        sub={t`matching filters`}
      />
      <StatTile
        testId="trace-summary-tokens"
        label={t`Tokens`}
        value={fmtTokens(totalTokens)}
        sub={cached !== null
          ? t`${fmtTokens(stats.inputTokens)} in · ${fmtTokens(stats.outputTokens)} out · ${cached}% cached`
          : t`${fmtTokens(stats.inputTokens)} in · ${fmtTokens(stats.outputTokens)} out`}
      />
      <StatTile
        testId="trace-summary-cost"
        label={t`Cost`}
        value={fmtCost(stats.totalCostEur)}
        sub={t`matching filters`}
      />
      <StatTile
        testId="trace-summary-latency"
        label={t`Avg latency`}
        value={fmtLatency(stats.avgLatencyMs)}
        sub={t`± ${fmtLatency(stats.latencyStdDevMs)}`}
      />
      <StatTile
        testId="trace-summary-errorrate"
        label={t`Error rate`}
        value={fmtPct(errorRate)}
        sub={t`${stats.errorCount.toLocaleString()} non-2xx`}
      />
    </div>
  );
}
