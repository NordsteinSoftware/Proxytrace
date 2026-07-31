import { useQuery } from '@tanstack/react-query';
import { testSuitesApi } from '../../api/test-suites';
import { QUERY_KEYS } from '../../api/query-keys';

const SUITE_PICKER_PAGE_SIZE = 200;

/** Suites owned by the trace's agent — the destinations the Generate-tests panel can write to. */
export function useAgentSuites(agentId: string | null) {
  return useQuery({
    queryKey: QUERY_KEYS.testSuites(agentId ?? undefined),
    queryFn: () => testSuitesApi.list({ agentId: agentId ?? undefined, pageSize: SUITE_PICKER_PAGE_SIZE }),
    enabled: !!agentId,
  });
}
