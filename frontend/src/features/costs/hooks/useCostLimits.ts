import { useMutation, useQuery, useQueryClient, type QueryKey } from '@tanstack/react-query';
import { useLingui } from '@lingui/react/macro';
import {
  costsApi,
  type CostLimitDto,
  type CreateCostLimitRequest,
  type UpdateCostLimitRequest,
} from '../../../api/costs';
import { QUERY_KEYS } from '../../../api/query-keys';
import useCurrentProject from '../../../hooks/useCurrentProject';
import useToast from '../../../hooks/useToast';
import { dropBudget, upsertBudget, type CostOverviewCache } from '../budgetPatch';

/** The project's configured budgets. Free for every member — only mutating one is licensed. */
export function useCostLimits() {
  const { currentProjectId } = useCurrentProject();
  return useQuery({
    queryKey: QUERY_KEYS.costLimits(currentProjectId),
    queryFn: () => costsApi.limits.list(currentProjectId ?? ''),
    enabled: !!currentProjectId,
  });
}

/**
 * Budget mutations.
 *
 * Every one **patches the cached cost overview first, then invalidates it**. The meters render from
 * `overview.budgets`, and one overview refetch re-derives the whole page — eight aggregate scans of
 * the trace table — for a change to a single ~200-byte configuration row. Waiting on it meant the
 * dialog closed, a success toast appeared, and the budget list sat unchanged for seconds: a
 * response indistinguishable from "nothing happened". The refetch still runs; it just reconciles in
 * the background instead of gating the UI.
 *
 * The invalidation stays broad (`costOverviewRoot`) on purpose: every cached window embeds the same
 * `budgets` array, so narrowing it to the mounted window would serve a stale budget list to anyone
 * switching the range back within the stale time. The patch is applied to every cached window for
 * the same reason.
 */
export function useCostLimitMutations() {
  const queryClient = useQueryClient();
  const { currentProjectId } = useCurrentProject();
  const { t } = useLingui();
  const { show: toast } = useToast();

  const limitsKey = QUERY_KEYS.costLimits(currentProjectId);

  /**
   * Applies `patch` to every cached overview **of the current project**, then queues the
   * authoritative refetch.
   *
   * Two things are load-bearing here. The patch runs *before* the invalidation, because
   * `refetchQueries` cancels an in-flight request — so a response already on the wire can never
   * land on top of the optimistic row. And the project id is checked per entry: `costOverviewRoot`
   * is the bare `['cost-overview']` prefix, which matches every project the user has visited, and
   * writing this project's budget into another project's cached page would be a straightforward
   * data leak between tenants.
   *
   * The *invalidation* stays deliberately broad — marking another project's overview stale only
   * costs it a refetch it would have done anyway.
   */
  function patchOverviews(patch: (prev: CostOverviewCache, rangeKey: string) => CostOverviewCache) {
    const entries = queryClient.getQueriesData<CostOverviewCache>({ queryKey: QUERY_KEYS.costOverviewRoot });
    for (const [key, prev] of entries) {
      if (!prev || key[1] !== currentProjectId) continue;
      queryClient.setQueryData<CostOverviewCache>(key, patch(prev, rangeKeyOf(key)));
    }
    queryClient.invalidateQueries({ queryKey: QUERY_KEYS.costOverviewRoot });
  }

  function settle(saved: CostLimitDto | null, message: string) {
    if (saved) {
      queryClient.setQueryData<CostLimitDto[]>(limitsKey, prev => {
        const rest = (prev ?? []).filter(l => l.id !== saved.id);
        return [...rest, saved];
      });
      patchOverviews((prev, rangeKey) => upsertBudget(prev, saved, rangeKey));
    } else {
      // Nothing to fold in, but the overview still has to be re-read — the budget did change.
      queryClient.invalidateQueries({ queryKey: QUERY_KEYS.costOverviewRoot });
    }
    queryClient.invalidateQueries({ queryKey: limitsKey });
    toast(message, 'success');
  }

  const create = useMutation({
    mutationFn: (body: CreateCostLimitRequest) => costsApi.limits.create(body),
    onSuccess: saved => settle(saved, t`Budget created`),
  });

  const update = useMutation({
    mutationFn: ({ id, body }: { id: string; body: UpdateCostLimitRequest }) =>
      costsApi.limits.update(id, body),
    onSuccess: saved => settle(saved, t`Budget saved`),
  });

  const remove = useMutation({
    mutationFn: (id: string) => costsApi.limits.remove(id),
    onSuccess: (_result, id) => {
      queryClient.setQueryData<CostLimitDto[]>(limitsKey, prev => (prev ?? []).filter(l => l.id !== id));
      queryClient.invalidateQueries({ queryKey: limitsKey });
      patchOverviews(prev => dropBudget(prev, id));
      toast(t`Budget deleted`, 'success');
    },
  });

  return { create, update, remove };
}

/** The `${from}|${to}|${bucket}` segment of a cost-overview key — what tells the patch which
 *  window it is looking at, and therefore whether the window's totals are month-to-date. */
function rangeKeyOf(key: QueryKey): string {
  const segment = key[2];
  return typeof segment === 'string' ? segment : '';
}
