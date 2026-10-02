import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { scopesApi } from '../../../api/scopes';
import { QUERY_KEYS } from '../../../api/query-keys';
import type { UpdateScopeRequest } from '../../../api/models';

/** One scope with its member agents — the detail pane; the list rows stay on the light DTO. */
export function useScopeDetail(id: string | null) {
  return useQuery({
    queryKey: QUERY_KEYS.scope(id ?? ''),
    queryFn: () => scopesApi.get(id ?? ''),
    enabled: !!id,
  });
}

/** Edits a scope's display name / description, then refreshes the list and that scope's detail. */
export function useUpdateScope(projectId: string | null) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, body }: { id: string; body: UpdateScopeRequest }) => scopesApi.update(id, body),
    onSuccess: saved => {
      qc.setQueryData(QUERY_KEYS.scope(saved.id), saved);
      if (projectId) qc.invalidateQueries({ queryKey: QUERY_KEYS.scopes(projectId) });
    },
  });
}
