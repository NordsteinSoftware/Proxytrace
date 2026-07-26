import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { agentCallsApi } from '../../../api/agent-calls';
import { QUERY_KEYS } from '../../../api/query-keys';
import useCurrentProject from '../../../hooks/useCurrentProject';
import { buildTraceFilter, type TraceQueryArgs } from './useTraceQueries';

/**
 * Aggregate over every trace matching the current filters — the KPI band above the list.
 *
 * Computed server-side rather than over the loaded rows: the list scrolls, so there is no "page" to
 * summarize, and numbers that climbed as the reader scrolled would read as noise rather than signal.
 *
 * Shares {@link buildTraceFilter} with the list query so the two can never describe different sets,
 * but drops the sort — an aggregate has no order, and keying on it would split the cache across
 * sorts that return identical numbers.
 */
export function useTraceSummary(args: TraceQueryArgs) {
  const { currentProjectId } = useCurrentProject();
  const projectId = currentProjectId ?? undefined;

  const filter = buildTraceFilter(args, projectId, false);

  const query = useQuery({
    queryKey: QUERY_KEYS.agentCallsSummary(filter),
    queryFn: () => agentCallsApi.summary(filter),
    placeholderData: keepPreviousData,
    enabled: currentProjectId !== null,
  });

  return { summary: query.data ?? null, isFetching: query.isFetching };
}
