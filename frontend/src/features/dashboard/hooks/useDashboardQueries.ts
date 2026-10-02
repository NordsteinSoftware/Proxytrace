// Single TanStack Query hook for the Dashboard. The whole page is served by one
// aggregate endpoint (GET /api/statistics/dashboard) instead of a per-widget fan-out.
// Key sourced from QUERY_KEYS; no raw useQuery in the page component.

import { useQuery } from '@tanstack/react-query';
import { statisticsApi } from '../../../api/statistics';
import { QUERY_KEYS } from '../../../api/query-keys';
import { REFETCH_INTERVAL_FAST } from '../../../lib/constants';
import { DASHBOARD_RECENT_TRACES } from '../dashboardMeta';

interface DashboardQueryOptions {
  /** Lower time bound, or `undefined` for the all-time bucket. */
  from: string | undefined;
  projectId: string | undefined;
  /** Narrows every aggregate to one scope (use-case group of agents); undefined = all scopes. */
  scopeId: string | undefined;
  enabled: boolean;
}

/** The entire dashboard payload (summary, telemetry, trends, breakdowns, recent traces, agents) in one request. */
export function useDashboardView({ from, projectId, scopeId, enabled }: DashboardQueryOptions) {
  return useQuery({
    queryKey: QUERY_KEYS.statisticsDashboard(from, projectId, scopeId),
    queryFn: () => statisticsApi.dashboard({ from, projectId, scopeId, recentTraceCount: DASHBOARD_RECENT_TRACES }),
    refetchInterval: REFETCH_INTERVAL_FAST,
    enabled,
  });
}
