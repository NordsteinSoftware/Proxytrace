import { useCallback, useState } from 'react';
import { EMPTY_ADVANCED_FILTERS, isValidAdvancedFilters, type TraceAdvancedFilters } from '../tracesMeta';

/**
 * Owns the composable trace filter bar's state (agent, anomaly, tool, model, status class, numeric
 * ranges). The whole object is **project-scoped** — agent ids and tool names belong to one project
 * and the Traces page stays mounted across project switches — so it is stored under a per-project
 * key and re-read when the project changes, via the same render-time prev-value pattern as the
 * time-range filter hook (no effect).
 *
 * Migration: earlier versions stored the agent filter under `traces.agentFilter.${projectId}` and
 * the outliers-only toggle under a global `traces.outlierOnly`. When no new-format value exists
 * yet, those legacy values seed the initial state (outliers-only becomes the anomaly filter's
 * "any" option) and the legacy keys are removed.
 */

export function advancedFiltersKey(projectId: string): string {
  return `traces.filters.${projectId}`;
}

/** The pre-filter-bar keys this hook migrates from (see module doc). */
function legacyAgentFilterKey(projectId: string): string {
  return `traces.agentFilter.${projectId}`;
}

const LEGACY_OUTLIER_KEY = 'traces.outlierOnly';

/**
 * Pure parse of the stored state: prefers the new-format JSON, falls back to the legacy keys,
 * defaults to all-empty. Exported for tests.
 */
export function parseStoredAdvancedFilters(
  raw: string | null,
  legacyAgent: string | null,
  legacyOutlierOnly: string | null,
): TraceAdvancedFilters {
  if (raw !== null) {
    try {
      const parsed: unknown = JSON.parse(raw);
      // Fill slots added since the value was stored (e.g. `scope`) with their empty default
      // before validating — otherwise every filter bar saved by an older version would fail the
      // shape check and silently reset to all-empty.
      const merged: unknown =
        typeof parsed === 'object' && parsed !== null ? { ...EMPTY_ADVANCED_FILTERS, ...parsed } : parsed;
      if (isValidAdvancedFilters(merged)) return merged;
    } catch {
      // fall through to the defaults — a corrupt value must not break the page
    }
    return EMPTY_ADVANCED_FILTERS;
  }
  return {
    ...EMPTY_ADVANCED_FILTERS,
    ...(legacyAgent ? { agent: legacyAgent } : {}),
    ...(legacyOutlierOnly === 'true' ? { anomaly: 'any' as const } : {}),
  };
}

function readFilters(projectId: string | null): TraceAdvancedFilters {
  if (projectId === null) return EMPTY_ADVANCED_FILTERS;
  try {
    const filters = parseStoredAdvancedFilters(
      localStorage.getItem(advancedFiltersKey(projectId)),
      localStorage.getItem(legacyAgentFilterKey(projectId)),
      localStorage.getItem(LEGACY_OUTLIER_KEY),
    );
    localStorage.removeItem(legacyAgentFilterKey(projectId));
    return filters;
  } catch {
    return EMPTY_ADVANCED_FILTERS;
  }
}

export interface TraceAdvancedFiltersState {
  filters: TraceAdvancedFilters;
  /** Merge a partial update into the active filters (empty string clears a slot). */
  setFilters: (patch: Partial<TraceAdvancedFilters>) => void;
  clearAll: () => void;
}

export function useTraceAdvancedFilters(projectId: string | null): TraceAdvancedFiltersState {
  const [filters, setFiltersState] = useState(() => readFilters(projectId));

  // Re-read on project switch during render (prev-value pattern; the page is not remounted).
  const [trackedProjectId, setTrackedProjectId] = useState(projectId);
  if (projectId !== trackedProjectId) {
    setTrackedProjectId(projectId);
    setFiltersState(readFilters(projectId));
  }

  const write = useCallback(
    (next: TraceAdvancedFilters) => {
      setFiltersState(next);
      if (projectId === null) return;
      try {
        localStorage.setItem(advancedFiltersKey(projectId), JSON.stringify(next));
      } catch {
        // storage unavailable or over quota — keep the in-memory value only
      }
    },
    [projectId],
  );

  const setFilters = useCallback(
    (patch: Partial<TraceAdvancedFilters>) => write({ ...filters, ...patch }),
    [filters, write],
  );
  const clearAll = useCallback(() => write(EMPTY_ADVANCED_FILTERS), [write]);

  return { filters, setFilters, clearAll };
}
