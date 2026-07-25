// Shared re-render pulse for time-derived UI (relative timestamps, "live" windows)
// that must recompute on a timer rather than only on an unrelated re-render.
// External-sync effect isolated here per BEST_PRACTICES §4.1.

import { useEffect, useState } from 'react';

export function useNowTick(intervalMs: number): number {
  const [now, setNow] = useState(() => Date.now());
  useEffect(() => {
    const id = setInterval(() => setNow(Date.now()), intervalMs);
    return () => clearInterval(id);
  }, [intervalMs]);
  return now;
}
