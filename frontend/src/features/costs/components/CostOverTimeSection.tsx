import { useMemo } from 'react';
import { Trans, useLingui } from '@lingui/react/macro';
import { Card } from '../../../components/ui/Card';
import { EmptyState } from '../../../components/ui/EmptyState';
import { Skeleton } from '../../../components/ui/Skeleton';
import { StackedBar } from '../../../components/charts';
import { fmtCost } from '../../../lib/format';
import type { StatisticsBucket } from '../../../lib/time-range';
import { toStackedCostData, totalOf, type DenseCostSeries } from '../costSeries';

interface CostOverTimeSectionProps {
  series: DenseCostSeries;
  bucket: StatisticsBucket;
  agentName: (agentId: string) => string;
  isLoading: boolean;
  isError: boolean;
}

/** Spend over the selected window, stacked per agent so a single runaway agent is visible at a glance. */
export function CostOverTimeSection({ series, bucket, agentName, isLoading, isError }: CostOverTimeSectionProps) {
  const { t } = useLingui();
  const data = useMemo(() => toStackedCostData(series, bucket, agentName), [series, bucket, agentName]);
  const total = useMemo(() => totalOf(series), [series]);

  return (
    <Card padding="md" data-testid="cost-over-time">
      <Card.Header
        title={t`Spend over time`}
        description={series.truncated
          ? t`Showing the most recent buckets — narrow the window or widen the bucket for the full range.`
          : undefined}
        action={<span className="font-mono text-body-sm text-secondary">{fmtCost(total)}</span>}
      />
      <Card.Body>
        {isLoading && <Skeleton height={220} />}
        {!isLoading && isError && (
          <p className="text-body-sm text-danger"><Trans>Could not load the cost series.</Trans></p>
        )}
        {!isLoading && !isError && total === 0 && (
          <div data-testid="cost-over-time-empty-state">
            <EmptyState
              title={t`No spend in this window`}
              description={t`Proxied calls with a priced model endpoint appear here.`}
            />
          </div>
        )}
        {!isLoading && !isError && total > 0 && (
          <StackedBar data={data} height={220} formatValue={fmtCost} formatAxisTick={fmtCost} />
        )}
      </Card.Body>
    </Card>
  );
}
