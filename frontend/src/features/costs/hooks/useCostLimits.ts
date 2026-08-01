import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
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
import { dropBudget, upsertBudget, type BudgetRow } from '../budgetPatch';

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
 * Every one **patches the cached budget status first, then invalidates it**, so the meter list
 * reacts in the same tick as the toast rather than after a round trip: the dialog closing, a
 * success toast, and an unchanged list is a response indistinguishable from "nothing happened".
 *
 * The *cost overview* is deliberately not touched. Budget state left that payload precisely so a
 * change to one ~200-byte configuration row stops re-deriving seven aggregate scans of the trace
 * table; the overview's spend telemetry does not depend on which budgets exist.
 */
export function useCostLimitMutations() {
  const queryClient = useQueryClient();
  const { currentProjectId } = useCurrentProject();
  const { t } = useLingui();
  const { show: toast } = useToast();

  const limitsKey = QUERY_KEYS.costLimits(currentProjectId);
  const statusKey = QUERY_KEYS.costBudgetStatus(currentProjectId);

  /**
   * Applies `patch` to the current project's cached budget status, then queues the authoritative
   * refetch.
   *
   * The patch runs *before* the invalidation, because `refetchQueries` cancels an in-flight
   * request — so a response already on the wire can never land on top of the optimistic row. The
   * key is per-project, so no other tenant's cached list is ever written to.
   */
  function patchStatus(patch: (prev: readonly BudgetRow[]) => BudgetRow[]) {
    queryClient.setQueryData<BudgetRow[]>(statusKey, prev => patch(prev ?? []));
    queryClient.invalidateQueries({ queryKey: statusKey });
  }

  function settle(saved: CostLimitDto | null, message: string) {
    if (saved) {
      queryClient.setQueryData<CostLimitDto[]>(limitsKey, prev => {
        const rest = (prev ?? []).filter(l => l.id !== saved.id);
        return [...rest, saved];
      });
      patchStatus(prev => upsertBudget(prev, saved));
    } else {
      // Nothing to fold in, but the status still has to be re-read — the budget did change.
      queryClient.invalidateQueries({ queryKey: statusKey });
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
      patchStatus(prev => dropBudget(prev, id));
      toast(t`Budget deleted`, 'success');
    },
  });

  return { create, update, remove };
}
