import { useLingui } from '@lingui/react/macro';
import { SegmentedControl } from '../../../components/ui/SegmentedControl';
import { TimeRangePicker } from '../../../components/ui/TimeRangePicker';
import type { TimeRange } from '../../../lib/timeRange';
import type { StatisticsBucket } from '../../../lib/time-range';

interface CostToolbarProps {
  timeRange: TimeRange;
  bucket: StatisticsBucket;
  onTimeRangeChange: (range: TimeRange) => void;
  onBucketChange: (bucket: StatisticsBucket) => void;
}

/** Window + granularity for the cost series. Budgets always measure the calendar month regardless. */
export function CostToolbar({ timeRange, bucket, onTimeRangeChange, onBucketChange }: CostToolbarProps) {
  const { t } = useLingui();

  return (
    <div className="flex items-center gap-2 flex-wrap shrink-0" data-testid="cost-toolbar">
      <TimeRangePicker value={timeRange} onChange={onTimeRangeChange} testId="cost-time" />

      {/*
        h-9 matches TimeRangePicker's trigger height. Without it the control sizes itself from its
        button padding and sits visibly shorter than the picker beside it; the flex wrapper's
        default stretch then sizes the buttons to fill.
      */}
      <SegmentedControl<StatisticsBucket>
        className="h-9"
        value={bucket}
        onChange={onBucketChange}
        segments={[
          { value: 'fiveMinutes', label: t`5 min`, testId: 'cost-bucket-fiveMinutes' },
          { value: 'hourly', label: t`Hourly`, testId: 'cost-bucket-hourly' },
          { value: 'daily', label: t`Daily`, testId: 'cost-bucket-daily' },
        ]}
      />
    </div>
  );
}
