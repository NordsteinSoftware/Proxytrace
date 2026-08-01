import { useQuery } from '@tanstack/react-query';
import { costsApi, type CostOverviewDto } from '../../../api/costs';
import { QUERY_KEYS } from '../../../api/query-keys';
import useCurrentProject from '../../../hooks/useCurrentProject';
import type { StatisticsBucket } from '../../../lib/time-range';
import type { TimeRange } from '../../../lib/timeRange';
import type { BudgetRow } from '../budgetPatch';
import { quantizedCostWindow } from '../costSeries';

/**
 * The Costs page's spend telemetry for the current project and the selected window. The window is
 * resolved to concrete `from`/`to` here (the API requires both) and the resolution is
 * bucket-quantized, so a relative range keeps the same query key across renders instead of
 * refetching on every one.
 *
 * `bucket` is what the user asked for; the *effective* granularity comes back on the payload, since
 * the API coarsens a fine bucket over a wide window rather than sending cells the chart discards.
 */
export function useCostOverview(timeRange: TimeRange, bucket: StatisticsBucket) {
  const { currentProjectId } = useCurrentProject();
  const { from, to } = quantizedCostWindow(timeRange, bucket);

  const query = useQuery<CostOverviewDto>({
    queryKey: QUERY_KEYS.costOverview(currentProjectId, `${from}|${to}|${bucket}`),
    queryFn: () =>
      costsApi.overview({ projectId: currentProjectId ?? '', from, to, bucket }),
    enabled: !!currentProjectId,
  });

  return {
    overview: query.data,
    from,
    to,
    // What the series was actually aggregated at — fall back to the request until it lands.
    effectiveBucket: query.data?.bucket ?? bucket,
    isLoading: query.isLoading,
    isError: query.isError,
  };
}

/**
 * The project's budgets joined with this month's spend and breach state — what the meters render
 * from.
 *
 * Its own query, not a slice of the overview: it is the read a budget mutation invalidates, and the
 * endpoint behind it costs one or two aggregate scans against the overview's seven. Typed as the
 * *cache* shape, since a mutation patches a just-created budget in with no measured spend yet.
 */
export function useBudgetStatus() {
  const { currentProjectId } = useCurrentProject();

  const query = useQuery<BudgetRow[]>({
    queryKey: QUERY_KEYS.costBudgetStatus(currentProjectId),
    queryFn: () => costsApi.limits.status(currentProjectId ?? ''),
    enabled: !!currentProjectId,
  });

  return { budgets: query.data ?? [], isLoading: query.isLoading, isError: query.isError };
}
