import { Trans } from '@lingui/react/macro';
import { SkeletonList } from '../../../components/ui/Skeleton';
import { EYEBROW_CLS } from '../../../components/ui/classes';
import { cn } from '../../../lib/cn';

interface Props {
  isFetchingNextPage: boolean;
  hasNextPage: boolean;
  /** Whether the list has any rows at all — an empty list gets its own empty state, not an ending. */
  hasRows: boolean;
}

/**
 * The tail of the scrolling list: either the next chunk arriving, or the end of the set.
 *
 * The loading state reserves height so the rows above it do not jump when the chunk lands.
 */
export function TraceListFooter({ isFetchingNextPage, hasNextPage, hasRows }: Props) {
  if (isFetchingNextPage) {
    return (
      <div data-testid="trace-list-loading-more" className="px-3 py-2">
        <SkeletonList rows={3} height={36} gap={4} />
      </div>
    );
  }

  if (!hasNextPage && hasRows) {
    return (
      <div
        data-testid="trace-list-end"
        className="flex items-center gap-3 px-4 py-3 border-t border-border-subtle"
      >
        <span aria-hidden className="flex-1 h-px bg-border-subtle" />
        <span className={cn(EYEBROW_CLS, 'whitespace-nowrap')}><Trans>End of results</Trans></span>
        <span aria-hidden className="flex-1 h-px bg-border-subtle" />
      </div>
    );
  }

  return null;
}
