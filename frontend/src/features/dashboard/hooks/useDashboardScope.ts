import { useCallback } from 'react';
import { useLocalStorageState } from '../../../hooks/useLocalStorageState';
import { useProjectScopes } from '../../../hooks/useProjectScopes';
import { resolveDashboardScope } from '../dashboardScope';

/**
 * The dashboard's scope filter — remembered per project (one stored map, since the dashboard stays
 * mounted across project switches) and validated against the project's scopes, so a scope that no
 * longer exists falls back to "all scopes" instead of an empty dashboard. `isResolved` stays false
 * while a remembered scope waits for the scope list: the dashboard holds its request until then
 * rather than firing an unscoped one and flashing project-wide numbers.
 */
export function useDashboardScope(projectId: string | undefined) {
  const { scopes, isLoading } = useProjectScopes();
  const [byProject, setByProject] = useLocalStorageState<Record<string, string>>('dashboard.scope', {});
  const stored = projectId ? byProject[projectId] : undefined;
  const { scopeId, isResolved } = resolveDashboardScope(stored, scopes, isLoading);

  const setScopeId = useCallback(
    (next: string | undefined) => {
      if (!projectId) return;
      const rest = Object.fromEntries(Object.entries(byProject).filter(([id]) => id !== projectId));
      setByProject(next ? { ...rest, [projectId]: next } : rest);
    },
    [projectId, byProject, setByProject],
  );

  return { scopes, scopeId, isResolved, setScopeId };
}
