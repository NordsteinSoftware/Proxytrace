import { useQuery } from '@tanstack/react-query';
import { sessionsApi } from '../../../api/sessions';
import { QUERY_KEYS } from '../../../api/query-keys';
import useCurrentProject from '../../../hooks/useCurrentProject';
import type { SessionDto } from '../../../api/models';

/**
 * Recent sessions for the current project, feeding the trace filter bar's session picker. Mirrors
 * {@link useTraceToolNames}: project-scoped, gated on a selected project, capped at the first page
 * (newest first, backend-sorted) so the picker stays a "recent sessions" shortlist, not the full set.
 */
export function useRecentSessions(): { sessions: SessionDto[]; isLoading: boolean } {
  const { currentProjectId } = useCurrentProject();
  const query = useQuery({
    queryKey: QUERY_KEYS.sessions(currentProjectId ?? '', 1, 50),
    queryFn: () => sessionsApi.list({ projectId: currentProjectId ?? '', page: 1, pageSize: 50 }),
    enabled: currentProjectId !== null,
  });
  return { sessions: query.data?.items ?? [], isLoading: query.isLoading };
}
