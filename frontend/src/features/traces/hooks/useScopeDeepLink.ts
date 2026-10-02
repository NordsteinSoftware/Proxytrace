import { useEffect } from 'react';
import { useSearchParams } from 'react-router';
import type { TraceAdvancedFilters } from '../tracesMeta';

/**
 * Applies a `?scope=<id>` deep link (e.g. "View traces" on the Scopes page) as the trace filter
 * bar's scope chip, then drops the param so the URL reflects the persisted filter state rather
 * than re-applying it on every visit. A URL → filter-state sync is a genuine external side effect
 * (BEST_PRACTICES §4.1); once the param is gone the effect is inert.
 */
export function useScopeDeepLink(setFilters: (patch: Partial<TraceAdvancedFilters>) => void) {
  const [searchParams, setSearchParams] = useSearchParams();
  const scopeId = searchParams.get('scope');

  useEffect(() => {
    if (!scopeId) return;
    setFilters({ scope: scopeId });
    setSearchParams(params => {
          params.delete('scope');
      return params;
    }, { replace: true });
  }, [scopeId, setFilters, setSearchParams]);
}
