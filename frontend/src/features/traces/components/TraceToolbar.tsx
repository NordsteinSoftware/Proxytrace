import type { ReactNode } from 'react';
import { SearchIcon } from '../../../components/icons';
import { TimeRangePicker } from '../../../components/ui/TimeRangePicker';
import { Input } from '../../../components/ui/Input';
import { Button } from '../../../components/ui/Button';
import type { TimeRange } from '../../../lib/timeRange';
import { Trans, useLingui } from '@lingui/react/macro';

interface Props {
  search: string;
  timeRange: TimeRange;
  onSearchChange: (v: string) => void;
  onTimeRangeChange: (r: TimeRange) => void;
  /** Trailing controls on the same line — the "+ Filter" picker sits here (see TraceFilterPicker). */
  trailing?: ReactNode;
  onClearSelection?: () => void;
}

/**
 * The always-visible toolbar row: search, time range, and the "+ Filter" picker (passed as
 * `trailing`). Active filters — agent, tool, model, status, numeric ranges, and the system-traces
 * view toggle — compose through that picker and surface as chips in TraceFilterBar below.
 */
export function TraceToolbar({ search, timeRange, onSearchChange, onTimeRangeChange, trailing, onClearSelection }: Props) {
  const { t } = useLingui();
  return (
    <div className="fade-up relative z-20 flex items-center gap-2 flex-wrap shrink-0 [animation-delay:80ms]">
      <div className="flex-1 min-w-[260px] max-w-[420px]">
        <Input
          leftAddon={<SearchIcon size={13} />}
          value={search}
          onChange={e => onSearchChange(e.target.value)}
          placeholder={t`Search by trace ID, content, or model…`}
          className="h-9"
        />
      </div>

      <TimeRangePicker value={timeRange} onChange={onTimeRangeChange} testId="traces-time" />

      {trailing}
      <Button
        variant="ghost"
        size="sm"
        className="ml-auto"
        data-testid="traces-clear-selection"
        disabled={!onClearSelection}
        onClick={onClearSelection}
      >
        <Trans>Clear selection</Trans>
      </Button>
    </div>
  );
}
