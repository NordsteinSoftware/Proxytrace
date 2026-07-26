import { useCallback, useRef, useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { useTraceStream } from '../../../api/event-stream';
import { QUERY_KEYS } from '../../../api/query-keys';
import { initialGateState, onReturnedToTop, onTraceArrived } from '../traceStreamGate';
import { useTraceHeadMerge } from './useTraceHeadMerge';
import type { TraceQueryArgs } from './useTraceQueries';

/**
 * Applies live trace arrivals to the traces view, gated on where the reader is (see
 * {@link onTraceArrived} for the policy).
 *
 * At the top, an arrival is **folded into the head of the cached list** — one page-1 GET, then a
 * `setQueryData` patch that inserts only the traces we do not already hold (see
 * {@link useTraceHeadMerge}). Deliberately not `resetQueries`: a reset leaves the infinite query with
 * no data until its refetch lands, and a list with no rows renders its loading skeleton, so every
 * arrival flashed the whole table away and redrew it. Patching keeps every rendered row mounted, so
 * only the arrivals mount — which is also what lets them animate in.
 *
 * While scrolled, the arrival is withheld entirely so rows never shift mid-read, and is flushed by
 * {@link markAtTop} when the reader comes back.
 *
 * The histogram and summary are position-independent, so they refresh either way — but they are
 * aggregates over a high-volume table, so a burst coalesces to at most one refresh per window.
 */
export function useTraceSseStream(args: TraceQueryArgs) {
  const qc = useQueryClient();
  const { mergeHead, freshIds } = useTraceHeadMerge(args);
  // The gate's coalescing clock lives in a ref (it must not drive renders); `pending` is mirrored
  // into state because the header's live-arrival indicator has to re-render when it flips.
  const gate = useRef(initialGateState());
  const atTop = useRef(true);
  const [pendingRefresh, setPendingRefresh] = useState(false);

  const refreshAggregates = useCallback(() => {
    void qc.invalidateQueries({ queryKey: QUERY_KEYS.agentCallsOverviewRoot, exact: false });
    void qc.invalidateQueries({ queryKey: QUERY_KEYS.agentCallsHistogramRoot, exact: false });
    void qc.invalidateQueries({ queryKey: QUERY_KEYS.agentCallsSummaryRoot, exact: false });
  }, [qc]);

  const handleTrace = useCallback(() => {
    const result = onTraceArrived(gate.current, atTop.current, Date.now());
    gate.current = result.state;
    setPendingRefresh(result.state.pending);
    if (result.mergeHead) void mergeHead();
    if (result.refreshAggregates) refreshAggregates();
  }, [mergeHead, refreshAggregates]);

  useTraceStream(handleTrace);

  /** Called by the list as it scrolls; flushes a withheld arrival on return to the top. */
  const markAtTop = useCallback((isAtTop: boolean) => {
    atTop.current = isAtTop;
    if (!isAtTop) return;
    const result = onReturnedToTop(gate.current, Date.now());
    gate.current = result.state;
    if (result.mergeHead) {
      setPendingRefresh(false);
      void mergeHead();
    }
  }, [mergeHead]);

  return { markAtTop, pendingRefresh, freshIds };
}
