import { useEffect, useState } from 'react';

/**
 * Whole seconds since the component mounted, ticking once a second (a timer is an external
 * system, so this is one of the sanctioned effect shapes — BEST_PRACTICES §4.1). Render it from a
 * leaf component (see `ElapsedStopwatch`) so the per-second state tick re-renders only the
 * readout, not the whole card around it.
 */
export function useElapsedSeconds(): number {
  const [start] = useState(() => Date.now());
  const [now, setNow] = useState(start);
  useEffect(() => {
    const timer = setInterval(() => setNow(Date.now()), 1_000);
    return () => clearInterval(timer);
  }, []);
  return Math.max(0, Math.floor((now - start) / 1_000));
}
