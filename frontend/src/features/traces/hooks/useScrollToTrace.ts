import { useEffect, useRef } from 'react';
import type { Virtualizer } from '@tanstack/react-virtual';
import type { TraceListRow } from '../traceDayDividers';
import { findFocusIndex, nextFocusStep } from '../traceFocus';
import type { TraceVirtualizerPaging } from './useTraceVirtualizer';

export interface ScrollToTraceOptions {
  items: TraceListRow[];
  virtualizer: Virtualizer<HTMLDivElement, Element>;
  paging: TraceVirtualizerPaging;
  /** Trace to bring into view (deep link), or null when there is none pending. */
  scrollToTraceId?: string | null;
  /** Called once the link is resolved — landed on the row, or given up. Clears the pending id. */
  onScrolledToTrace?: () => void;
}

/**
 * Brings a deep-linked trace into view, loading chunks until it is reachable.
 *
 * A plain `querySelector` cannot find an unrendered row, which is exactly what virtualization
 * guarantees — so the id is resolved against the loaded rows and the virtualizer is scrolled by
 * index. When the trace is *not* among them the link used to be dropped silently, leaving the drawer
 * open over a list scrolled somewhere else entirely (#456). Rows arrive in list order, so an older
 * trace is simply further down: the next chunk is pulled and the effect re-runs when it lands.
 *
 * Bounded on purpose. The budget is per link (reset when a new one arrives), and running out — like
 * reaching the end of the list — resolves the link rather than retrying forever, so a link to a
 * long-since-deleted or filtered-out trace costs a fixed number of requests.
 */
export function useScrollToTrace({
  items,
  virtualizer,
  paging,
  scrollToTraceId,
  onScrolledToTrace,
}: ScrollToTraceOptions) {
  const { hasNextPage, isFetchingNextPage, onLoadMore } = paging;
  const chunksFetched = useRef(0);

  useEffect(() => {
    if (!scrollToTraceId) {
      chunksFetched.current = 0;
      return;
    }

    const index = findFocusIndex(items, scrollToTraceId);
    const step = nextFocusStep({
      index,
      hasNextPage,
      isFetchingNextPage,
      chunksFetched: chunksFetched.current,
    });

    if (step === 'wait') return;

    if (step === 'fetch-more') {
      chunksFetched.current += 1;
      onLoadMore();
      return;
    }

    if (step === 'scroll') {
      virtualizer.scrollToIndex(index, { align: 'center' });
    }

    // Both 'scroll' and 'give-up' end the search: the pending id is cleared either way, so a link
    // that cannot be resolved stops re-entering this effect on every subsequent render.
    chunksFetched.current = 0;
    onScrolledToTrace?.();
  }, [scrollToTraceId, items, virtualizer, onScrolledToTrace, hasNextPage, isFetchingNextPage, onLoadMore]);
}
