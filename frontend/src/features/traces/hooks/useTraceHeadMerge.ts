import { useCallback, useEffect, useMemo, useRef } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { agentCallsApi } from '../../../api/agent-calls';
import { QUERY_KEYS } from '../../../api/query-keys';
import useCurrentProject from '../../../hooks/useCurrentProject';
import { mergeHeadChunk, type TraceListCache } from '../traceHeadMerge';
import { buildTraceFilter, TRACE_CHUNK_SIZE, type TraceQueryArgs } from './useTraceQueries';
import { useFreshRowIds } from './useFreshRowIds';

/**
 * Trailing window before a head fetch. A busy proxy emits many `trace-created` events a second, and
 * each fold costs one request; waiting a beat turns a burst into a single fetch that inserts several
 * rows at once. Short enough that a quiet stream still feels immediate.
 */
export const HEAD_MERGE_COALESCE_MS = 400;

const delay = (ms: number) => new Promise<void>(resolve => { setTimeout(resolve, ms); });

/**
 * Folds live arrivals into the head of the trace list.
 *
 * One page-1 GET under the list's current filter *and* sort, then a `setQueryData` patch — never a
 * refetch of the loaded chunks. That is what keeps the rendered rows mounted (a reset leaves the query
 * empty until its refetch lands, and an empty list renders the loading skeleton), and it satisfies
 * BEST_PRACTICES §3.2: SSE patches the cache.
 *
 * The GET is needed because `TraceCreatedEvent` carries only ids and timestamps, not the row the list
 * renders. Fetching page 1 rather than the single trace by id is deliberate: it re-reads the head
 * under the live filter and sort, so a trace that does not match — or does not rank into the head —
 * is simply absent instead of being force-fed to the top.
 */
export function useTraceHeadMerge(args: TraceQueryArgs) {
  const qc = useQueryClient();
  const { currentProjectId } = useCurrentProject();
  const { freshIds, markFresh } = useFreshRowIds();

  const filter = useMemo(
    () => buildTraceFilter(args, currentProjectId ?? undefined),
    [args, currentProjectId],
  );

  // A fold in flight; a second arrival meanwhile just re-arms it rather than firing its own request.
  const running = useRef(false);
  const queued = useRef(false);
  const alive = useRef(true);
  useEffect(() => {
    alive.current = true;
    return () => { alive.current = false; };
  }, []);

  const mergeHead = useCallback(async () => {
    if (running.current) {
      queued.current = true;
      return;
    }
    running.current = true;
    try {
      const key = QUERY_KEYS.agentCalls(filter);
      do {
        queued.current = false;
        await delay(HEAD_MERGE_COALESCE_MS);
        // Nothing cached means the list is loading its first chunk (or was never opened) — that fetch
        // already returns the arrival, and patching would race it.
        if (!alive.current || !qc.getQueryData<TraceListCache>(key)) return;

        const head = await agentCallsApi.list({ ...filter, page: 1, pageSize: TRACE_CHUNK_SIZE });
        if (!alive.current) return;

        // Re-read the cache: the reader may have pulled another chunk while the head was in flight.
        const merged = mergeHeadChunk(qc.getQueryData<TraceListCache>(key), head);
        if (!merged) continue;

        qc.setQueryData(key, merged.cache);
        markFresh(merged.freshIds);
      } while (queued.current);
    } catch {
      // A dropped fold is not worth escalating: `api/client.ts` has already surfaced the failure, the
      // list keeps the rows it has, and the next arrival retries.
    } finally {
      running.current = false;
    }
  }, [qc, filter, markFresh]);

  return { mergeHead, freshIds };
}
