import { useLingui } from '@lingui/react/macro';
import { EYEBROW_CLS } from '../../../components/ui/classes';
import { cn } from '../../../lib/cn';

interface Props {
  /** An instant on the day this divider opens. */
  timestamp: string;
}

/**
 * Marks a day boundary in the time-ordered trace list. Scroll depth in a time series is depth in
 * time, so once the reader is thousands of rows down the useful question is "which day am I in?" —
 * which a page number never answered.
 *
 * Rendered only under a time sort (see `withDayDividers`), because under a metric sort consecutive
 * rows have no temporal relationship and this would assert an order the list does not have.
 */
export function TraceDayDivider({ timestamp }: Props) {
  const { i18n } = useLingui();

  return (
    <div
      data-testid="trace-day-divider"
      className="sticky top-0 z-[5] flex items-center gap-3 px-4 py-1 bg-card border-b border-border-subtle"
    >
      <span className={cn(EYEBROW_CLS, 'whitespace-nowrap')}>
        {i18n.date(new Date(timestamp), { weekday: 'short', day: 'numeric', month: 'short' })}
      </span>
      <span aria-hidden className="flex-1 h-px bg-border-subtle" />
    </div>
  );
}
