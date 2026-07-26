import { useVirtualizer } from '@tanstack/react-virtual';
import { useCallback, useEffect, type RefObject } from 'react';
import { listRowKey, type TraceListRow } from '../traceDayDividers';

/**
 * Starting heights only — every item is re-measured once mounted, so these exist to keep the initial
 * scrollbar roughly honest rather than to be accurate.
 */
const ESTIMATED_ROW_PX = 44;
const ESTIMATED_DIVIDER_PX = 26;

/** Rows rendered beyond the viewport on each side, so scrolling doesn't reveal blank space. */
const OVERSCAN = 8;

export interface TraceVirtualizerPaging {
  hasNextPage: boolean;
  isFetchingNextPage: boolean;
  onLoadMore: () => void;
}

/**
 * Windows the trace list and pulls the next chunk as the reader approaches the end.
 *
 * Fetching is driven off the virtualizer's own last rendered index rather than a separate
 * IntersectionObserver: one scroll authority, so there is nothing to fall out of sync with the
 * rendered window.
 */
export function useTraceVirtualizer(
  scrollRef: RefObject<HTMLDivElement | null>,
  items: TraceListRow[],
  { hasNextPage, isFetchingNextPage, onLoadMore }: TraceVirtualizerPaging,
) {
  const estimateSize = useCallback(
    (index: number) => (items[index]?.kind === 'divider' ? ESTIMATED_DIVIDER_PX : ESTIMATED_ROW_PX),
    [items],
  );

  // Identity, not position. The measured-height cache is keyed by this, and a live arrival splices
  // rows into the head — so under the default index key, an expanded group that shifts down inherits
  // the collapsed height cached at its new index (and `measureElement` returns that stale value
  // instead of reading the DOM, since the node's box never changed and no resize fires). Every row
  // below it then lands short and the expanded turns paint over them.
  const getItemKey = useCallback(
    (index: number) => {
      const item = items[index];
      return item ? listRowKey(item) : index;
    },
    [items],
  );

  // ESLint's react-hooks/incompatible-library warns here: `useVirtualizer` returns functions React
  // Compiler cannot safely memoize, so it skips compiling this hook. That is expected and harmless —
  // the virtualizer is inherently stateful and re-reads the DOM on scroll, which is exactly the case
  // the compiler declines to memoize. Left un-suppressed so the trade-off stays visible.
  const virtualizer = useVirtualizer({
    count: items.length,
    getScrollElement: () => scrollRef.current,
    estimateSize,
    getItemKey,
    overscan: OVERSCAN,
  });

  const virtualItems = virtualizer.getVirtualItems();
  const lastRenderedIndex = virtualItems.length > 0 ? virtualItems[virtualItems.length - 1].index : -1;

  // An effect rather than a render-time call: fetching is a side effect, and firing it during render
  // would trip react-hooks lint and re-enter the query on every re-render the fetch itself causes.
  useEffect(() => {
    if (!hasNextPage || isFetchingNextPage || items.length === 0) return;
    if (lastRenderedIndex >= items.length - 1) {
      onLoadMore();
    }
  }, [lastRenderedIndex, items.length, hasNextPage, isFetchingNextPage, onLoadMore]);

  return { virtualizer, virtualItems };
}
