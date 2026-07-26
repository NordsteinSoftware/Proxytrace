import { useQuery } from '@tanstack/react-query';
import { costsApi } from '../../../api/costs';
import { QUERY_KEYS } from '../../../api/query-keys';
import useCurrentProject from '../../../hooks/useCurrentProject';
import type { StatisticsBucket } from '../../../lib/time-range';
import type { TimeRange } from '../../../lib/timeRange';
import { quantizedCostWindow } from '../costSeries';

/**
 * The whole Costs page payload for the current project and the selected window. The window is
 * resolved to concrete `from`/`to` here (the API requires both) and the resolution is
 * bucket-quantized, so a relative range keeps the same query key across renders instead of
 * refetching on every one.
 */
export function useCostOverview(timeRange: TimeRange, bucket: StatisticsBucket) {
  const { currentProjectId } = useCurrentProject();
  const { from, to } = quantizedCostWindow(timeRange, bucket);

  const query = useQuery({
    queryKey: QUERY_KEYS.costOverview(currentProjectId, `${from}|${to}|${bucket}`),
    queryFn: () =>
      costsApi.overview({ projectId: currentProjectId ?? '', from, to, bucket }),
    enabled: !!currentProjectId,
  });

  return {
    overview: query.data,
    from,
    to,
    isLoading: query.isLoading,
    isError: query.isError,
  };
}
