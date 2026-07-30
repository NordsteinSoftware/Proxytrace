import { useQuery } from '@tanstack/react-query';
import { providersApi } from '../../../api/providers';
import { QUERY_KEYS } from '../../../api/query-keys';
import useCurrentProject from '../../../hooks/useCurrentProject';
import type { ApiKeyDto } from '../../../api/models';

/**
 * The current project's inbound API keys — the pickable scopes for a key budget.
 *
 * Read off the providers overview rather than a dedicated endpoint: keys are embedded there
 * already, so the Costs page shares whatever the Providers page has cached instead of adding a
 * second source of truth for the same rows. (The per-key *spend* breakdown comes from the cost
 * overview and lists only keys that spent something; this lists every key, including brand-new
 * ones with no traffic yet, which is exactly who you want to budget before they cost anything.)
 */
export function useProjectApiKeys(): { apiKeys: ApiKeyDto[]; isLoading: boolean } {
  const { currentProjectId } = useCurrentProject();

  const query = useQuery({
    queryKey: QUERY_KEYS.providersOverview,
    queryFn: providersApi.overview,
  });

  const apiKeys = (query.data?.providers ?? [])
    .flatMap(provider => provider.keys)
    .filter(key => key.projectId === currentProjectId);

  return { apiKeys, isLoading: query.isLoading };
}
