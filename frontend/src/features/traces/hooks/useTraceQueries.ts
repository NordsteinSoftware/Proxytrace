import { keepPreviousData, useInfiniteQuery, useQuery } from '@tanstack/react-query';
import { agentCallsApi } from '../../../api/agent-calls';
import { QUERY_KEYS } from '../../../api/query-keys';
import useCurrentProject from '../../../hooks/useCurrentProject';
import type { AgentCallFilter } from '../../../api/models';
import { advancedFilterParams, DEFAULT_TRACE_SORT, SORT_FIELD_TO_API, type TraceAdvancedFilters, type TraceSort } from '../tracesMeta';
import { dedupeById } from '../traceHeadMerge';

/**
 * Rows fetched per scroll chunk. Internal, not a user-facing choice: the list scrolls continuously,
 * so "how many at a time" is a network-batching detail rather than a setting anyone should tune.
 */
export const TRACE_CHUNK_SIZE = 50;

export interface TraceQueryArgs {
  advanced: TraceAdvancedFilters;
  debouncedSearch: string;
  showSystem: boolean;
  from: string | undefined;
  to: string | undefined;
  sort: TraceSort;
}

/**
 * The filter shared by the trace list, the histogram, and the KPI summary. Extracted so those three
 * can never drift into describing different sets — the moment one of them builds its own filter,
 * the table and the numbers above it start disagreeing.
 *
 * Excludes paging: the list adds `page` per chunk from its `pageParam`, and the aggregates have no
 * paging at all.
 *
 * `withSort` is false for aggregates. An aggregate has no order, so carrying the sort would split
 * its cache across sorts that return byte-identical numbers.
 */
export function buildTraceFilter(
  { advanced, debouncedSearch, showSystem, from, to, sort }: TraceQueryArgs,
  projectId: string | undefined,
  withSort = true,
): AgentCallFilter {
  const trimmedSearch = debouncedSearch.trim();
  const sortsByDefault = sort.field === DEFAULT_TRACE_SORT.field && sort.desc === DEFAULT_TRACE_SORT.desc;
  return {
    includeSystemAgents: showSystem,
    ...advancedFilterParams(advanced),
    ...(projectId ? { projectId } : {}),
    ...(from ? { from } : {}),
    ...(to ? { to } : {}),
    ...(trimmedSearch.length >= 2 ? { q: trimmedSearch } : {}),
    // Default (time desc) stays implicit so existing query keys — and the backend default — hold.
    ...(withSort && !sortsByDefault
      ? { sortBy: SORT_FIELD_TO_API[sort.field], sortDesc: sort.desc }
      : {}),
  };
}

/**
 * Two queries serve the Traces page: the trace list, loaded a chunk at a time as the reader scrolls,
 * and a filter-bar overview (agents + breakdown + latency, keyed only on range/agent/project so it
 * survives scrolling).
 *
 * The list is an infinite query, so a filter/sort/range change reshapes the query key and resets it
 * to the first chunk on its own — there is no page state to reset by hand.
 */
export function useTraceQueries(args: TraceQueryArgs) {
  const { currentProjectId } = useCurrentProject();
  const projectId = currentProjectId ?? undefined;
  const enabled = currentProjectId !== null;

  const filter = buildTraceFilter(args, projectId);

  const tracesQuery = useInfiniteQuery({
    queryKey: QUERY_KEYS.agentCalls(filter),
    // `page` rides the pageParam rather than the key: one cache entry holds every loaded chunk.
    queryFn: ({ pageParam }) => agentCallsApi.list({ ...filter, page: pageParam, pageSize: TRACE_CHUNK_SIZE }),
    initialPageParam: 1,
    getNextPageParam: (lastPage, allPages) => {
      const loaded = allPages.reduce((sum, page) => sum + page.items.length, 0);
      return loaded < lastPage.total ? allPages.length + 1 : undefined;
    },
    placeholderData: keepPreviousData,
    enabled,
  });

  const overviewQuery = useQuery({
    queryKey: QUERY_KEYS.agentCallsOverview(args.from, args.advanced.agent || undefined, projectId),
    queryFn: () => agentCallsApi.overview({ from: args.from, agentId: args.advanced.agent || undefined, projectId }),
    placeholderData: keepPreviousData,
    enabled,
  });

  return {
    // Deduped because the head grows under live arrivals while chunks are addressed by offset: the
    // next chunk then starts at a shifted offset and repeats rows already loaded (see `dedupeById`).
    traces: dedupeById(tracesQuery.data?.pages.flatMap(page => page.items) ?? []),
    total: tracesQuery.data?.pages[0]?.total ?? 0,
    isFetching: tracesQuery.isFetching,
    isFetchingNextPage: tracesQuery.isFetchingNextPage,
    hasNextPage: tracesQuery.hasNextPage,
    fetchNextPage: () => { void tracesQuery.fetchNextPage(); },
    allAgents: overviewQuery.data?.agents ?? [],
    agentBreakdown: overviewQuery.data?.agentBreakdown ?? [],
    p95: overviewQuery.data?.latency?.[0]?.p95Ms ?? null,
  };
}
