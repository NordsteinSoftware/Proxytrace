// License queries/mutations. Shared (not feature-local) because license state is consumed
// across many features (setup, settings, suites, runs, evaluators) and the layout shell, and
// feature hooks must not be imported across feature boundaries (BEST_PRACTICES §2/§15).

import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { licenseApi, type LicenseFeature } from '../api/license';
import { QUERY_KEYS } from '../api/query-keys';

/**
 * The current license snapshot. License state changes rarely, so it is cached
 * for an hour and not refetched on window focus.
 */
export function useLicense() {
  return useQuery({
    queryKey: QUERY_KEYS.license,
    queryFn: licenseApi.get,
    staleTime: 60 * 60 * 1000,
    refetchOnWindowFocus: false,
    // Rendered in masthead chrome (the three license banners) — a routine license-endpoint
    // failure must degrade the banner in place, not rethrow and collapse the whole top bar via
    // the chrome boundary (BEST_PRACTICES §9.1), matching sibling useNotifications/useUpdateStatus.
    throwOnError: false,
  });
}

/**
 * Whether a given feature is enabled by the current license. Defaults to false
 * while the license is still loading so gated UI stays hidden until confirmed.
 */
export function useFeature(feature: LicenseFeature): boolean {
  const { data } = useLicense();
  return data?.features.includes(feature) ?? false;
}

/** Admin-only: force a re-check against the license server, then refresh the cache. */
export function useRefreshLicense() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: licenseApi.refresh,
    onSuccess: (dto) => {
      queryClient.setQueryData(QUERY_KEYS.license, dto);
    },
  });
}

/** Dry-run validation of a license key — nothing is stored or applied. */
export function useValidateLicense() {
  return useMutation({ mutationFn: licenseApi.validate });
}

/**
 * Sets the installation's license key (stores + activates it without a restart).
 * Allowed for admins, and anonymously while setup is incomplete (the wizard's
 * Welcome step runs before the first admin exists).
 */
export function useSetLicense() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: licenseApi.set,
    onSuccess: (dto) => {
      queryClient.setQueryData(QUERY_KEYS.license, dto);
    },
  });
}

/** Admin-only: removes the stored key; falls back to the environment license or Free. */
export function useRemoveLicense() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: licenseApi.remove,
    onSuccess: (dto) => {
      queryClient.setQueryData(QUERY_KEYS.license, dto);
    },
  });
}
