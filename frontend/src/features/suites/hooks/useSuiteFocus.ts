import { useEffect, useState } from 'react';
import { useSearchParams } from 'react-router';

/**
 * Reads ?suiteId=<id> from the URL once the grid is rendered, scrolls that
 * suite card into view, and returns its id for a transient highlight ring.
 * Clears the param afterwards (replaces history entry). Genuine external
 * side-effect (URL param driving DOM scroll) per BEST_PRACTICES §4.1.
 */
export function useSuiteFocus(ready: boolean): string | null {
  const [searchParams, setSearchParams] = useSearchParams();
  const suiteId = searchParams.get('suiteId');
  const [highlight, setHighlight] = useState<string | null>(null);

  useEffect(() => {
    if (!suiteId || !ready) return;
    const el = document.querySelector(`[data-testid="suite-card-${suiteId}"]`);
    if (!el) return;

    el.scrollIntoView({ behavior: 'smooth', block: 'center' });
    // Defer state out of the effect body so the highlight doesn't cascade-render.
    const raf = requestAnimationFrame(() => setHighlight(suiteId));
    const timer = setTimeout(() => setHighlight(null), 2200);

    setSearchParams(prev => {
      const next = new URLSearchParams(prev);
      next.delete('suiteId');
      return next;
    }, { replace: true });

    return () => {
      cancelAnimationFrame(raf);
      clearTimeout(timer);
    };
  }, [suiteId, ready, setSearchParams]);

  return highlight;
}
