import { useMemo, useState } from 'react';
import { Trans, useLingui } from '@lingui/react/macro';
import { msg } from '@lingui/core/macro';
import type { MessageDescriptor } from '@lingui/core';
import { Card } from '../../../components/ui/Card';
import { EmptyState } from '../../../components/ui/EmptyState';
import { Skeleton } from '../../../components/ui/Skeleton';
import { SegmentedControl } from '../../../components/ui/SegmentedControl';
import { StackedBar } from '../../../components/charts';
import { fmtCost } from '../../../lib/format';
import type { StatisticsBucket } from '../../../lib/time-range';
import { toStackedCostData, totalOf, type DenseCostSeries } from '../costSeries';

/** Which dimension the stack is broken down by. */
export type CostDimension = 'agent' | 'apiKey';

interface CostOverTimeSectionProps {
  byAgent: DenseCostSeries;
  byApiKey: DenseCostSeries;
  /** The granularity the data actually came back at — see `requestedBucket`. */
  bucket: StatisticsBucket;
  /**
   * What the toolbar asked for. When it differs from `bucket` the API coarsened the aggregate to
   * fit the window, and the chart says so rather than leaving the toolbar contradicting the axis.
   */
  requestedBucket: StatisticsBucket;
  /** Resolves a series key to its display name; receives null for the unattributed key group. */
  nameOf: (dimension: CostDimension, seriesKey: string | null) => string;
  isLoading: boolean;
  isError: boolean;
}

const BUCKET_LABEL: Record<StatisticsBucket, MessageDescriptor> = {
  fiveMinutes: msg`5-minute`,
  hourly: msg`hourly`,
  daily: msg`daily`,
};

/**
 * Spend over the selected window, stacked so a single runaway series is visible at a glance. The
 * toggle switches which dimension the stack is cut by — *who* spent it (agent) or *what credential*
 * spent it (API key). Both come from the same window, so switching never refetches.
 */
export function CostOverTimeSection({
  byAgent,
  byApiKey,
  bucket,
  requestedBucket,
  nameOf,
  isLoading,
  isError,
}: CostOverTimeSectionProps) {
  const { t, i18n } = useLingui();
  // eslint-disable-next-line lingui/no-unlocalized-strings -- CostDimension token, not UI copy
  const [dimension, setDimension] = useState<CostDimension>('agent');

  const series = dimension === 'agent' ? byAgent : byApiKey;
  const data = useMemo(
    () => toStackedCostData(series, bucket, key => nameOf(dimension, key)),
    [series, bucket, nameOf, dimension],
  );
  const total = useMemo(() => totalOf(series), [series]);

  // The API coarsened the aggregate because the requested granularity would have produced far more
  // buckets than this chart draws. Saying so keeps the toolbar from silently contradicting the axis.
  const coarsened = bucket !== requestedBucket;
  const description = coarsened
    ? t`This window is too wide for ${i18n._(BUCKET_LABEL[requestedBucket])} buckets — showing ${i18n._(BUCKET_LABEL[bucket])} spend instead.`
    : series.truncated
      ? t`Showing the most recent buckets — narrow the window or widen the bucket for the full range.`
      : undefined;

  return (
    <Card padding="md" data-testid="cost-over-time">
      <Card.Header
        title={t`Spend over time`}
        description={description}
        action={
          <div className="flex items-center gap-3">
            <SegmentedControl
              value={dimension}
              onChange={setDimension}
              segments={[
                { value: 'agent', label: t`By agent`, testId: 'cost-dimension-agent' },
                { value: 'apiKey', label: t`By API key`, testId: 'cost-dimension-api-key' },
              ]}
            />
            <span className="font-mono text-body-sm text-secondary">{fmtCost(total)}</span>
          </div>
        }
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
