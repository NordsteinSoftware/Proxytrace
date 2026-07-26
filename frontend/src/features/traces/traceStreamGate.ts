/**
 * Decides what a live trace arrival should do to the traces view. Pure, so the policy is testable
 * without a fake SSE stream or a real scroll container.
 *
 * The trace list is an infinite query. Refetching it while the reader is scrolled deep would refetch
 * every loaded chunk *and* shift offsets underneath them as new rows insert at the top — the reader
 * would watch rows jump while trying to read one. So arrivals are withheld while scrolled and
 * flushed when the reader returns to the top, where replacing the loaded chunks is invisible.
 *
 * The histogram and the KPI summary are position-independent, so they refresh regardless of scroll —
 * but they are aggregates over a high-volume table, so a burst of arrivals is coalesced into at most
 * one refresh per {@link SUMMARY_COALESCE_MS}.
 */
export interface StreamGateState {
  /** An arrival was withheld; the list owes a reset once the reader returns to the top. */
  pending: boolean;
  /** When the aggregates last refreshed, for the coalescing window. */
  lastFlushedAt: number;
}

/** Coalescing window for the aggregate queries (histogram + summary), trailing edge. */
export const SUMMARY_COALESCE_MS = 5000;

export function initialGateState(): StreamGateState {
  // -Infinity rather than 0 so the very first arrival always refreshes, whatever the clock reads.
  return { pending: false, lastFlushedAt: Number.NEGATIVE_INFINITY };
}

export interface ArrivalDecision {
  state: StreamGateState;
  /** Discard the loaded chunks and refetch from the first one. Only ever true at the top. */
  resetList: boolean;
  /** Invalidate the histogram + summary queries. */
  refreshAggregates: boolean;
}

export function onTraceArrived(
  state: StreamGateState,
  isAtTop: boolean,
  now: number,
): ArrivalDecision {
  const refreshAggregates = now - state.lastFlushedAt >= SUMMARY_COALESCE_MS;

  return {
    state: {
      // At the top the arrival is applied immediately, so nothing is owed.
      pending: !isAtTop,
      lastFlushedAt: refreshAggregates ? now : state.lastFlushedAt,
    },
    resetList: isAtTop,
    refreshAggregates,
  };
}

export function onReturnedToTop(
  state: StreamGateState,
  now: number,
): { state: StreamGateState; resetList: boolean } {
  if (!state.pending) {
    return { state, resetList: false };
  }

  return { state: { pending: false, lastFlushedAt: now }, resetList: true };
}
