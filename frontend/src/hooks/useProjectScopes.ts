import { useMemo } from 'react';
import { useQuery } from '@tanstack/react-query';
import { scopesApi } from '../api/scopes';
import { QUERY_KEYS } from '../api/query-keys';
import type { ScopeListItemDto } from '../api/models';
import { scopesById } from '../lib/scopes';
import useCurrentProject from './useCurrentProject';

const EMPTY: ScopeListItemDto[] = [];

/**
 * Every scope (use-case group of agents) of the current project. Shared by the Scopes page and by
 * every surface that filters or labels by scope (traces, dashboard, agents), so they read one cache
 * entry. Unpaged — the API bounds the list by the per-project scope cap.
 */
export function useProjectScopes() {
  const { currentProjectId } = useCurrentProject();
  const query = useQuery({
    queryKey: QUERY_KEYS.scopes(currentProjectId ?? ''),
    queryFn: () => scopesApi.list(currentProjectId ?? ''),
    enabled: currentProjectId !== null,
  });
  const scopes = query.data ?? EMPTY;
  const byId = useMemo(() => scopesById(scopes), [scopes]);
  return { scopes, byId, isLoading: query.isLoading, isError: query.isError };
}
