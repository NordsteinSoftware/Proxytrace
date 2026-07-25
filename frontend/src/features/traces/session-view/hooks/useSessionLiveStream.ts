import { useCallback } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { useTraceStream } from '../../../../api/event-stream';
import { QUERY_KEYS } from '../../../../api/query-keys';
import type { TraceCreatedEvent } from '../../../../api/models';

/**
 * Refreshes this session — its trace list and its counters — when a trace belonging to it arrives on
 * the shared trace SSE stream (filtered client-side by `sessionId`).
 *
 * NOTE: {@link TraceCreatedEvent} carries only partial data (id, agentId, model, createdAt, …), not
 * the full row the list renders, so we invalidate rather than `setQueryData`-patch. This mirrors
 * {@link useTraceSseStream} and is the same documented deviation from BEST_PRACTICES §3.2 ("SSE
 * patches the cache") — a full patch would need a per-event GET; tracked as a future improvement.
 */
export function useSessionLiveStream(sessionId: string | null) {
  const qc = useQueryClient();
  const handleTrace = useCallback(
    (e: TraceCreatedEvent) => {
      if (!sessionId || e.sessionId !== sessionId) return;
      // Prefix key: refreshes the session's paged trace list regardless of page/sort.
      qc.invalidateQueries({ queryKey: QUERY_KEYS.agentCallsRoot });
      qc.invalidateQueries({ queryKey: QUERY_KEYS.session(sessionId) });
    },
    [qc, sessionId],
  );
  useTraceStream(handleTrace);
}
