// Full-width live pulse band — the dashboard's signature element. An EKG-style
// call-rate line over the trailing hour, beating in real time via SSE, flanked
// by the live telemetry counters.

import { Trans, useLingui } from '@lingui/react/macro';
import type { LiveTelemetryDto } from '../../../api/models';
import { sparklinePath } from '../../../lib/charts';
import { useElementWidth } from '../../../hooks/useElementWidth';
import { teleFmt, EYEBROW_CLS } from '../dashboardMeta';
import { cn } from '../../../lib/cn';

const BAND_HEIGHT = 72;

interface PulseBandProps {
  pulse: number[];
  lastBeat: number;
  telemetry: LiveTelemetryDto | undefined;
}

export function PulseBand({ pulse, lastBeat, telemetry }: PulseBandProps) {
  const { t } = useLingui();
  const [ref, width] = useElementWidth<HTMLDivElement>(600);
  const idle = pulse.every(v => v === 0);
  const line = idle
    ? // eslint-disable-next-line lingui/no-unlocalized-strings -- SVG path commands
      `M 0 ${BAND_HEIGHT - 4} L ${width} ${BAND_HEIGHT - 4}`
    : sparklinePath(pulse, width, BAND_HEIGHT);
  const errorPctDisplay = telemetry ? `${(telemetry.errorRate * 100).toFixed(1)}%` : '—';

  return (
    <div
      data-testid="pulse-band"
      className={cn(
        'relative overflow-hidden rounded-lg bg-card shadow-[var(--shadow-card)] px-4 py-3 flex flex-col sm:flex-row items-stretch gap-5',
        idle && 'pulse-idle-sweep',
      )}
    >
      {/* EKG line */}
      <div ref={ref} className="relative flex-1 min-w-0">
        <div className={cn(EYEBROW_CLS, 'mb-1')}>
          <Trans>Activity · last 60 min</Trans>
        </div>
        <svg width="100%" height={BAND_HEIGHT} className="block overflow-visible" aria-hidden="true">
          <path d={line} fill="none" stroke="var(--accent-primary)" strokeWidth={2} />
        </svg>
        {lastBeat > 0 && <span key={lastBeat} className="pulse-sweep" />}
      </div>

      {/* Live counters have a different fixed window from the activity line. */}
      <div className="relative flex flex-col justify-center gap-2 pt-3 border-t sm:pt-0 sm:border-t-0 sm:pl-5 sm:border-l border-hairline shrink-0">
        <span className={EYEBROW_CLS}><Trans>Live rates · last 5 min</Trans></span>
        <div className="flex items-center justify-between gap-6">
          <PulseCounter label={t`traces/min`} value={teleFmt(telemetry?.tracesPerMinute)} accent />
          <PulseCounter label={t`tokens/s`} value={teleFmt(telemetry?.tokensPerSecond, v => String(Math.round(v)))} />
          <PulseCounter label={t`errors`} value={errorPctDisplay} danger={(telemetry?.errorRate ?? 0) > 0} />
        </div>
      </div>
    </div>
  );
}

interface PulseCounterProps {
  label: string;
  value: string;
  accent?: boolean;
  danger?: boolean;
}

function PulseCounter({ label, value, accent, danger }: PulseCounterProps) {
  return (
    <div className="flex flex-col items-end gap-0.5">
      {/* Keying on the value restarts the tick animation on every change. */}
      <span
        key={value}
        className={cn(
          'digit-tick font-mono text-display font-semibold tabular-nums leading-none',
          danger ? 'text-danger' : accent ? 'text-accent-hover' : 'text-primary',
        )}
      >
        {value}
      </span>
      <span className={EYEBROW_CLS}>{label}</span>
    </div>
  );
}
