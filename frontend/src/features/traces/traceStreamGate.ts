/**
 * Decides what a live trace arrival should do to the traces view. Pure, so the policy is testable
 * without a fake SSE stream or a real scroll container.
 *
 * At the top an arrival is folded into the head of the loaded list in place (see `traceHeadMerge.ts`)
 * — the rows already rendered stay mounted and only the arrivals appear. While the reader is scrolled
 * it is withheld instead: under offset paging, inserting rows above the viewport shifts every offset
 * below it, so the reader would watch rows jump while trying to read one. The withheld arrival is
 * flushed when they return to the top.
 *
 * The histogram and the KPI summary are position-independent, so they refresh regardless of scroll —
 * but they are aggregates over a high-volume table, so a burst of arrivals is coalesced into at most
 * one refresh per {@link SUMMARY_COALESCE_MS}.
 */
export interface StreamGateState {
  /** An arrival was withheld; the list owes a head merge once the reader returns to the top. */
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
  /** Fold the freshly arrived traces into the head of the loaded list. Only ever true at the top. */
  mergeHead: boolean;
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
    mergeHead: isAtTop,
    refreshAggregates,
  };
}

export function onReturnedToTop(
  state: StreamGateState,
  now: number,
): { state: StreamGateState; mergeHead: boolean } {
  if (!state.pending) {
    return { state, mergeHead: false };
  }

  return { state: { pending: false, lastFlushedAt: now }, mergeHead: true };
}
