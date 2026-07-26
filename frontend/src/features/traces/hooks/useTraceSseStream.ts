import { useCallback, useRef, useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { useTraceStream } from '../../../api/event-stream';
import { QUERY_KEYS } from '../../../api/query-keys';
import { initialGateState, onReturnedToTop, onTraceArrived } from '../traceStreamGate';

/**
 * Applies live trace arrivals to the traces view, gated on where the reader is (see
 * {@link onTraceArrived} for the policy).
 *
 * At the top, an arrival **resets** the list query — deliberately not `invalidateQueries`, which on
 * an infinite query refetches *every* loaded chunk. A reader twenty chunks deep would fire twenty
 * requests; resetting drops back to one fresh chunk, which is invisible precisely because it only
 * happens at the top. While scrolled, the arrival is withheld entirely so rows never shift mid-read,
 * and is flushed by {@link markAtTop} when the reader comes back.
 *
 * The histogram and summary are position-independent, so they refresh either way — but they are
 * aggregates over a high-volume table, so a burst coalesces to at most one refresh per window.
 *
 * NOTE: this remains a deliberate deviation from BEST_PRACTICES §3.2 ("SSE patches the cache; it
 * does not trigger refetches"). The TraceCreatedEvent carries only partial data (id, agentId, model,
 * provider, createdAt), not the full row the list renders, so a `setQueryData` patch is not possible
 * without a per-event GET. It is a narrower deviation than the previous blanket invalidation of the
 * whole `['agent-calls']` prefix: refetches now happen only at the top, and only for the list.
 */
export function useTraceSseStream() {
  const qc = useQueryClient();
  // The gate's coalescing clock lives in a ref (it must not drive renders); `pending` is mirrored
  // into state because the header's live-arrival indicator has to re-render when it flips.
  const gate = useRef(initialGateState());
  const atTop = useRef(true);
  const [pendingRefresh, setPendingRefresh] = useState(false);

  const resetList = useCallback(() => {
    // Only the lists — resetting the whole 'agent-calls' prefix would drop the overview, histogram
    // and summary too, flashing the entire page back to its loading state on every arrival.
    void qc.resetQueries({ queryKey: QUERY_KEYS.agentCallsListRoot, exact: false });
  }, [qc]);

  const refreshAggregates = useCallback(() => {
    void qc.invalidateQueries({ queryKey: QUERY_KEYS.agentCallsOverviewRoot, exact: false });
    void qc.invalidateQueries({ queryKey: QUERY_KEYS.agentCallsHistogramRoot, exact: false });
    void qc.invalidateQueries({ queryKey: QUERY_KEYS.agentCallsSummaryRoot, exact: false });
  }, [qc]);

  const handleTrace = useCallback(() => {
    const result = onTraceArrived(gate.current, atTop.current, Date.now());
    gate.current = result.state;
    setPendingRefresh(result.state.pending);
    if (result.resetList) resetList();
    if (result.refreshAggregates) refreshAggregates();
  }, [resetList, refreshAggregates]);

  useTraceStream(handleTrace);

  /** Called by the list as it scrolls; flushes a withheld arrival on return to the top. */
  const markAtTop = useCallback((isAtTop: boolean) => {
    atTop.current = isAtTop;
    if (!isAtTop) return;
    const result = onReturnedToTop(gate.current, Date.now());
    gate.current = result.state;
    if (result.resetList) {
      setPendingRefresh(false);
      resetList();
    }
  }, [resetList]);

  return { markAtTop, pendingRefresh };
}
