// Rolling per-minute call-rate window for the pulse band. Seeds from the server's
// pulse series (refetched every 30s) and rolls forward client-side between refetches:
// each SSE trace arrival bumps the current minute, a 60s timer slides the window.
// External-sync effects live here per BEST_PRACTICES §4.1.

import { useEffect, useState } from 'react';
import { useTraceStream } from '../../../api/event-stream';
import { bumpPulse, normalizePulse, shiftPulse } from '../dashboardMeta';

interface PulseState {
  /** Dense per-minute counts, oldest → newest (PULSE_MINUTES entries). */
  pulse: number[];
  /** Timestamp of the last SSE arrival — key a sweep element on it to restart its animation. */
  lastBeat: number;
}

export function usePulse(
  serverPulse: number[] | undefined,
  projectId: string | undefined,
  scopeId?: string,
): PulseState {
  const [pulse, setPulse] = useState<number[]>(() => normalizePulse(serverPulse));
  const [lastBeat, setLastBeat] = useState(0);

  // Re-seed whenever the server delivers a fresh series — it is the source of truth
  // and already includes any calls the SSE bumps counted in the meantime. Compared
  // during render rather than in an effect (BEST_PRACTICES §4.1's "reset state on a
  // prop change" pattern — see SearchIndexingSection.tsx for the same shape).
  // Benign race: a concurrent refetch can return a series that doesn't yet include a trace an
  // SSE bump just counted, transiently overwriting it here — self-corrects on the next poll.
  const serverKey = serverPulse?.join(',');
  const [syncedKey, setSyncedKey] = useState(serverKey);
  if (serverKey !== syncedKey) {
    setSyncedKey(serverKey);
    if (serverPulse) setPulse(normalizePulse(serverPulse));
  }

  // Minute rollover.
  useEffect(() => {
    const id = setInterval(() => setPulse(shiftPulse), 60_000);
    return () => clearInterval(id);
  }, []);

  useTraceStream(e => {
    // The stream is server-filtered to member projects; narrow to the selected project here.
    if (projectId && e.projectId !== projectId) return;
    // …and to the selected scope, so the band counts what the scoped aggregates count.
    if (scopeId && e.scopeId !== scopeId) return;
    setPulse(bumpPulse);
    setLastBeat(Date.now());
  });

  return { pulse, lastBeat };
}
