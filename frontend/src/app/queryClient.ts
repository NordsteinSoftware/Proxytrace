import { MutationCache, QueryCache, QueryClient } from '@tanstack/react-query';
import { ReadOnlyModeError, UpgradeRequiredError } from '../api/client';
import { showToast } from '../components/ui/Toast';
import { showUpgradeModal } from '../components/license/UpgradeModal';
import { fetchAuthMode } from '../auth/authMode';
import { configApi } from '../api/config';
import { QUERY_KEYS } from '../api/query-keys';

// A 402 license rejection is surfaced as an upgrade dialog rather than the
// generic error toast / page crash. Routing it from both caches catches every
// mutation and query without per-call wiring.
function handleUpgradeError(error: unknown): boolean {
  if (error instanceof UpgradeRequiredError) {
    showUpgradeModal({ errorType: error.errorType, message: error.message });
    return true;
  }
  return false;
}

// A mutation refused by read-only mode is an expected outcome of the kiosk demo, not a failure:
// tell the user in a neutral toast rather than the red error one. Only on the mutation cache —
// mutations are user-initiated, so this cannot fire for a background query.
function handleReadOnlyError(error: unknown): boolean {
  if (error instanceof ReadOnlyModeError) {
    showToast(error.message, 'info');
    return true;
  }
  return false;
}

export const queryClient = new QueryClient({
  defaultOptions: { queries: { retry: 1, staleTime: 30_000, throwOnError: true } },
  queryCache: new QueryCache({ onError: handleUpgradeError }),
  mutationCache: new MutationCache({
    onError: error => {
      if (!handleUpgradeError(error)) handleReadOnlyError(error);
    },
  }),
});

// Prefetch auth-mode + app config so children can render synchronously.
queryClient.prefetchQuery({ queryKey: QUERY_KEYS.authMode, queryFn: fetchAuthMode, staleTime: Infinity });
queryClient.prefetchQuery({ queryKey: QUERY_KEYS.appConfig, queryFn: configApi.get, staleTime: Infinity });
