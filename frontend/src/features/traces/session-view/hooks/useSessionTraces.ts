import { keepPreviousData, useInfiniteQuery } from '@tanstack/react-query';
import { agentCallsApi } from '../../../../api/agent-calls';
import { QUERY_KEYS } from '../../../../api/query-keys';
import type { AgentCallFilter } from '../../../../api/models';
import { SORT_FIELD_TO_API, type TraceSort } from '../../tracesMeta';
import { TRACE_CHUNK_SIZE } from '../../hooks/useTraceQueries';

/**
 * The session's traces, via the existing agent-calls list scoped by `sessionId`. Also carries the
 * session's `projectId` so the list authorizes for project-scoped (non-admin) members — the
 * agent-calls list denies a query with neither project nor agent scope — while the `sessionId`
 * WHERE clause still narrows to the one session. Keyed on the full filter through
 * {@link QUERY_KEYS.agentCalls} so the trace stream's list refresh reaches it.
 *
 * Loaded a chunk at a time as the reader scrolls. That is also what fixed live arrivals in a long
 * session: under paging, an arrival landed on the *last* page while the viewer sat on page 1, so the
 * header counters climbed but no row ever appeared. Defaults to chronological (createdAt ascending)
 * so arrivals append at the bottom; the table header can re-sort via `sort`.
 */
export function useSessionTraces(
  sessionId: string | null,
  projectId: string | null,
  sort: TraceSort,
) {
  const filter: AgentCallFilter = {
    projectId: projectId ?? undefined,
    sessionId: sessionId ?? undefined,
    pageSize: TRACE_CHUNK_SIZE,
    sortBy: SORT_FIELD_TO_API[sort.field],
    sortDesc: sort.desc,
    // System agents are part of a session's real activity — never hide them here.
    includeSystemAgents: true,
  };

  const query = useInfiniteQuery({
    queryKey: QUERY_KEYS.agentCalls(filter),
    // `page` rides the pageParam rather than the key: one cache entry holds every loaded chunk.
    queryFn: ({ pageParam }) => agentCallsApi.list({ ...filter, page: pageParam }),
    initialPageParam: 1,
    getNextPageParam: (lastPage, allPages) => {
      const loaded = allPages.reduce((sum, page) => sum + page.items.length, 0);
      return loaded < lastPage.total ? allPages.length + 1 : undefined;
    },
    placeholderData: keepPreviousData,
    enabled: !!sessionId && !!projectId,
  });

  return {
    traces: query.data?.pages.flatMap(page => page.items) ?? [],
    total: query.data?.pages[0]?.total ?? 0,
    isFetching: query.isFetching,
    isFetchingNextPage: query.isFetchingNextPage,
    hasNextPage: query.hasNextPage,
    fetchNextPage: () => { void query.fetchNextPage(); },
  };
}
