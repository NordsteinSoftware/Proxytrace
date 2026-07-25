import { useCallback } from 'react';
import { useSearchParams } from 'react-router';

/**
 * Persists a master/detail selection in the URL query string so it survives
 * refresh, back/forward, and link sharing. This is the app-wide convention for
 * master-detail views (agents, evaluators, proposals, runs, suites): the
 * selected entity id lives in `?<param>=` (default `id`), written with
 * `{ replace: true }` so browsing the list doesn't pile up history entries.
 *
 * The raw param value is returned as-is; callers validate it against loaded
 * data and fall back to a default (usually the first item) — that default is
 * *derived*, not written to the URL, keeping the address bar clean until the
 * user makes an explicit choice.
 *
 * `select(id, clear)` optionally drops sibling params in the same history
 * replace (e.g. a stale deep-link param) so two updates don't race.
 */
export function useSelectedId(
  param = 'id',
): readonly [string | null, (id: string | null, clear?: string[]) => void] {
  const [searchParams, setSearchParams] = useSearchParams();
  const selectedId = searchParams.get(param);

  const select = useCallback(
    (id: string | null, clear?: string[]) => {
      setSearchParams(
        prev => {
          const next = new URLSearchParams(prev);
          if (id) next.set(param, id);
          else next.delete(param);
          clear?.forEach(p => next.delete(p));
          return next;
        },
        { replace: true },
      );
    },
    [setSearchParams, param],
  );

  return [selectedId, select] as const;
}
