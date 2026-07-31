import { useLingui } from '@lingui/react/macro';
import { fmtElapsed } from '../../lib/format';
import { useElapsedSeconds } from '../../hooks/useElapsedSeconds';

/**
 * A quiet m:ss stopwatch counting up from its own mount. A leaf on purpose: the once-a-second
 * state tick lives here, so the card hosting it (the await card's corner) doesn't re-render its
 * rows every second for the sake of a text node.
 */
export function ElapsedStopwatch() {
  const { t } = useLingui();
  const elapsed = useElapsedSeconds();
  return (
    <span className="font-mono text-caption tabular-nums text-muted" title={t`Time waited`}>
      {fmtElapsed(elapsed)}
    </span>
  );
}
