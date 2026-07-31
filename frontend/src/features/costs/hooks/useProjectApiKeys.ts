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
 *
 * That overview is **Admin-only**, so `enabled` is required rather than defaulted — the caller has
 * to pass its own admin check, and a future call site cannot reintroduce #490 by leaving it off.
 * Reading the Costs page is free for every project member, and firing this unconditionally earned
 * them a 403 that the global `throwOnError: true` rethrew during render, replacing the whole page
 * with the error boundary. Only the budget scope picker needs these rows, and that is admin-only
 * anyway; the chart legend names keys from the cost overview instead (`apiKeyNames`).
 * `throwOnError: false` keeps any *other* failure a degraded picker rather than a dead page — the
 * boundary is a backstop for bugs, not for a request that can routinely fail.
 */
export function useProjectApiKeys(enabled: boolean): { apiKeys: ApiKeyDto[]; isLoading: boolean } {
  const { currentProjectId } = useCurrentProject();

  const query = useQuery({
    queryKey: QUERY_KEYS.providersOverview,
    queryFn: providersApi.overview,
    enabled,
    throwOnError: false,
  });

  const apiKeys = (query.data?.providers ?? [])
    .flatMap(provider => provider.keys)
    .filter(key => key.projectId === currentProjectId);

  return { apiKeys, isLoading: query.isLoading };
}
