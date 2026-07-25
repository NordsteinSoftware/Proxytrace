import { useQuery } from '@tanstack/react-query';
import { sessionsApi } from '../../../../api/sessions';
import { QUERY_KEYS } from '../../../../api/query-keys';

/**
 * The session's identity + counters (external key, first-seen/last-activity, trace/token totals).
 * Gated on `sessionId`; the live stream ({@link useSessionLiveStream}) invalidates this key so the
 * counters refresh as new traces arrive.
 */
export function useSessionDetail(sessionId: string | null) {
  const query = useQuery({
    queryKey: QUERY_KEYS.session(sessionId ?? ''),
    queryFn: () => sessionsApi.get(sessionId ?? ''),
    enabled: !!sessionId,
  });
  return { session: query.data ?? null, isLoading: query.isLoading, isError: query.isError };
}
