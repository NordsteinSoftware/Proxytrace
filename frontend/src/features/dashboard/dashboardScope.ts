import type { ScopeListItemDto } from '../../api/models';

/**
 * Resolves the dashboard's remembered scope against the project's scopes. Nothing remembered →
 * all scopes, at once. Remembered but the list still loading → unresolved: whether the scope still
 * exists is not known yet, and guessing "all scopes" would fetch (and show) the unscoped dashboard
 * first. Once the list is in, a scope that is gone falls back to all scopes — as does a list that
 * failed to load, which `isLoading` no longer reports.
 */
export function resolveDashboardScope(
  stored: string | undefined,
  scopes: readonly ScopeListItemDto[],
  scopesLoading: boolean,
): { scopeId: string | undefined; isResolved: boolean } {
  if (!stored) return { scopeId: undefined, isResolved: true };
  if (scopesLoading) return { scopeId: undefined, isResolved: false };
  return { scopeId: scopes.some(s => s.id === stored) ? stored : undefined, isResolved: true };
}
