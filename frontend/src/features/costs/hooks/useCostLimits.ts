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
 * Budget mutations. Every one also invalidates the cost overview: the overview carries the joined
 * budget + breach state the meters render, and editing a limit clears its breach server-side, so a
 * stale overview would keep showing a block that has already been lifted.
 */
export function useCostLimitMutations() {
  const queryClient = useQueryClient();
  const { currentProjectId } = useCurrentProject();
  const { t } = useLingui();
  const { show: toast } = useToast();

  const limitsKey = QUERY_KEYS.costLimits(currentProjectId);

  function settle(saved: CostLimitDto | null, message: string) {
    if (saved) {
      queryClient.setQueryData<CostLimitDto[]>(limitsKey, prev => {
        const rest = (prev ?? []).filter(l => l.id !== saved.id);
        return [...rest, saved];
      });
    }
    queryClient.invalidateQueries({ queryKey: limitsKey });
    queryClient.invalidateQueries({ queryKey: QUERY_KEYS.costOverviewRoot });
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
      queryClient.invalidateQueries({ queryKey: QUERY_KEYS.costOverviewRoot });
      toast(t`Budget deleted`, 'success');
    },
  });

  return { create, update, remove };
}
