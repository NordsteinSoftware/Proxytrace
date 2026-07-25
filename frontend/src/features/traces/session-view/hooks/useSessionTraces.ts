import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { agentCallsApi } from '../../../../api/agent-calls';
import { QUERY_KEYS } from '../../../../api/query-keys';
import type { AgentCallFilter } from '../../../../api/models';
import { SORT_FIELD_TO_API, type TraceSort } from '../../tracesMeta';

/**
 * The session's traces, via the existing agent-calls list scoped by `sessionId`. Also carries the
 * session's `projectId` so the list authorizes for project-scoped (non-admin) members — the
 * agent-calls list denies a query with neither project nor agent scope — while the `sessionId`
 * WHERE clause still narrows to the one session. Keyed on the full filter through
 * {@link QUERY_KEYS.agentCalls} so the SSE stream's `['agent-calls']` invalidation refreshes it.
 * Defaults to chronological (createdAt ascending) so live arrivals append at the bottom; the table
 * header can re-sort via `sort`.
 */
export function useSessionTraces(
  sessionId: string | null,
  projectId: string | null,
  page: number,
  pageSize: number,
  sort: TraceSort,
) {
  const filter: AgentCallFilter = {
    projectId: projectId ?? undefined,
    sessionId: sessionId ?? undefined,
    page,
    pageSize,
    sortBy: SORT_FIELD_TO_API[sort.field],
    sortDesc: sort.desc,
    // System agents are part of a session's real activity — never hide them here.
    includeSystemAgents: true,
  };
  const query = useQuery({
    queryKey: QUERY_KEYS.agentCalls(filter),
    queryFn: () => agentCallsApi.list(filter),
    placeholderData: keepPreviousData,
    enabled: !!sessionId && !!projectId,
  });
  return { traces: query.data?.items ?? [], total: query.data?.total ?? 0, isFetching: query.isFetching };
}
