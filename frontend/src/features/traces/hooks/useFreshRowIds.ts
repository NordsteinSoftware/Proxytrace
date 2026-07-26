import { useCallback, useEffect, useRef, useState } from 'react';

/**
 * How long a row counts as freshly arrived. Matches the `arrival-flash` keyframe in `index.css`, so
 * the class is dropped the moment the animation ends rather than mid-wash.
 */
export const FRESH_ROW_MS = 1600;

const NO_IDS: ReadonlySet<string> = new Set();

/**
 * Marks ids as freshly arrived for {@link FRESH_ROW_MS}, so a list can animate exactly the rows that
 * just appeared.
 *
 * Ids are *told* to this hook rather than diffed out of the rendered list: the cache fold already
 * knows precisely what it inserted (see `traceHeadMerge.ts`), and diffing a virtualized list of
 * thousands of rows on every render to rediscover it would be both slower and less accurate.
 *
 * Timers are an external system, so they live in a hook per BEST_PRACTICES §4.1.
 */
export function useFreshRowIds() {
  const [freshIds, setFreshIds] = useState<ReadonlySet<string>>(NO_IDS);
  const timers = useRef(new Set<ReturnType<typeof setTimeout>>());

  // Dropping the pending timers on unmount is also what makes the expiry callbacks safe: none of them
  // can fire against a torn-down component. The Set identity never changes, so capturing it is sound.
  useEffect(() => {
    const pending = timers.current;
    return () => {
      pending.forEach(clearTimeout);
      pending.clear();
    };
  }, []);

  const markFresh = useCallback((ids: readonly string[]) => {
    if (ids.length === 0) return;
    setFreshIds(prev => new Set([...prev, ...ids]));

    // Each batch expires on its own clock, so a later arrival never cuts an earlier one's animation
    // short — and the set cannot grow without bound on a busy stream.
    const timer = setTimeout(() => {
      timers.current.delete(timer);
      setFreshIds(prev => {
        const next = new Set(prev);
        ids.forEach(id => next.delete(id));
        return next.size === prev.size ? prev : next;
      });
    }, FRESH_ROW_MS);
    timers.current.add(timer);
  }, []);

  return { freshIds, markFresh };
}
