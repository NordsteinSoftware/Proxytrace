import { useLingui } from '@lingui/react/macro';
import { plural } from '@lingui/core/macro';
import { KpiCard } from '../../../components/ui/KpiCard';
import { Skeleton } from '../../../components/ui/Skeleton';
import { fmtCost, fmtPct } from '../../../lib/format';
import { monthDelta, projectMonthEnd } from '../costSeries';

interface CostKpiRowProps {
  monthToDateEur: number;
  previousMonthEur: number;
  windowTotalEur: number;
  blockedCount: number;
  isLoading: boolean;
}

/**
 * The management summary strip: month-to-date spend with its month-over-month delta, the
 * straight-line month-end projection, the selected window's total, and how many budgets are
 * currently blocking calls.
 */
export function CostKpiRow({
  monthToDateEur,
  previousMonthEur,
  windowTotalEur,
  blockedCount,
  isLoading,
}: CostKpiRowProps) {
  const { t } = useLingui();

  if (isLoading) {
    return (
      <div className="grid grid-cols-[repeat(auto-fit,minmax(180px,1fr))] gap-3" data-testid="cost-kpis-loading">
        {[0, 1, 2, 3].map(i => <Skeleton key={i} height={104} />)}
      </div>
    );
  }

  const delta = monthDelta(monthToDateEur, previousMonthEur);
  const projected = projectMonthEnd(monthToDateEur);

  return (
    <div className="grid grid-cols-[repeat(auto-fit,minmax(180px,1fr))] gap-3" data-testid="cost-kpis">
      <KpiCard
        label={t`Month to date`}
        value={fmtCost(monthToDateEur)}
        sub={t`Current calendar month (UTC)`}
        // Spending more than last month is the bad direction, so the arrow reads inverted.
        trend={delta === null ? undefined : {
          direction: delta >= 0 ? 'up' : 'down',
          pct: fmtPct(Math.abs(delta)),
          positive: false,
        }}
        accent
      />
      <KpiCard
        label={t`Projected month end`}
        value={projected === null ? '—' : fmtCost(projected)}
        sub={projected === null ? t`Too early in the month to project` : t`Straight-line from spend so far`}
      />
      <KpiCard label={t`Previous month`} value={fmtCost(previousMonthEur)} sub={t`Full calendar month`} />
      <KpiCard
        label={t`Selected window`}
        value={fmtCost(windowTotalEur)}
        sub={blockedCount > 0
          ? plural(blockedCount, { one: '# budget blocking calls', other: '# budgets blocking calls' })
          : t`No budget is blocking`}
        valueColor={blockedCount > 0 ? 'var(--danger)' : undefined}
      />
    </div>
  );
}
