// Shared cell renderers used by FlatTraceRow and ConversationGroupRow.
// Each is a tiny presentational component (< 20 lines) with no state.

import { useLingui } from '@lingui/react/macro';
import { cn } from '../../../lib/cn';
import { agentColor, modelColor } from '../../../lib/colors';
import { fmtLatency, fmtTokens, cachedPct } from '../../../lib/format';
import { tracePreview } from '../../../lib/trace';
import { OUTLIER_FLAG_LABEL, isOutlier, outlierFlagKeys } from '../../../lib/outliers';
import type { AgentCallListItemDto } from '../../../api/models';
import { Tooltip } from '../../../components/ui/Tooltip';
import { AlertTriangleIcon } from '../../../components/icons';
import { latencyBarPct } from '../tracesMeta';

// ── Latency bar ───────────────────────────────────────────────────────────────

export function LatencyBar({ ms }: { ms: number }) {
  const pct = latencyBarPct(ms);
  const barColor = ms > 3000 ? 'var(--warn)' : 'var(--accent-primary)';
  return (
    <span className="flex-1 max-w-[60px] h-[3px] overflow-hidden inline-block align-middle bg-white/[0.05]">
      <span className="block h-full" style={{ width: `${pct}%`, background: barColor }} />
    </span>
  );
}

// ── Individual cells ──────────────────────────────────────────────────────────

export function MessagePreviewCell({ trace }: { trace: AgentCallListItemDto }) {
  const c = trace.agentId ? agentColor(trace.agentId) : modelColor(trace.model);
  const preview = tracePreview(trace);
  return (
    // `pr-3`: the row grid has no column gap, so each cell carries its own gutter — without it a
    // truncated preview runs straight into the agent name in the next column.
    <span className="flex items-center gap-2 min-w-0 pr-3">
      <span className="w-[3px] h-[18px] shrink-0" style={{ background: c }} />
      <span className="text-body-sm text-secondary overflow-hidden text-ellipsis whitespace-nowrap">
        {preview ?? <span className="text-muted">—</span>}
      </span>
    </span>
  );
}

/**
 * Anomaly-column chip: an amber warning triangle on a flagged trace, nothing on a normal one.
 * Hovering it lists which characteristics tripped (e.g. *High latency, Many tool calls*). Lives in
 * the dedicated first column so flagged calls are scannable down the left edge of the list.
 */
export function OutlierCell({ flags }: { flags: number }) {
  const { t, i18n } = useLingui();
  if (!isOutlier(flags)) return null;
  const reasons = outlierFlagKeys(flags).map(key => i18n._(OUTLIER_FLAG_LABEL[key])).join(', ');
  const label = t`Outlier: ${reasons}`;
  return (
    <Tooltip content={label}>
      <span
        data-testid={`trace-outlier-marker-${flags}`}
        aria-label={label}
        className="inline-flex items-center justify-center w-[18px] h-[18px] rounded-none bg-warn-subtle text-warn"
      >
        <AlertTriangleIcon size={11} />
      </span>
    </Tooltip>
  );
}

export function TokenCell({ trace }: { trace: AgentCallListItemDto }) {
  return (
    <span className="mono text-body-sm">
      <span className="text-primary">{fmtTokens(trace.inputTokens + trace.outputTokens)}</span>
      <span className="text-muted ml-1.5 text-caption">{fmtTokens(trace.inputTokens)}/{fmtTokens(trace.outputTokens)}</span>
    </span>
  );
}

/** Share of the input tokens served from the provider cache, as a percent. Muted dash when none. */
export function CachedCell({ cachedInput, input }: { cachedInput: number; input: number }) {
  const pct = cachedPct(cachedInput, input);
  return pct !== null
    ? <span className="mono text-body-sm text-secondary">{pct}%</span>
    : <span className="text-muted text-body-sm">—</span>;
}

export function ToolsCell({ count }: { count: number }) {
  return count > 0
    ? <span className="mono text-body-sm text-primary">{count}</span>
    : <span className="text-muted text-body-sm">—</span>;
}

export function LatencyCell({ ms }: { ms: number }) {
  return (
    <span className="flex items-center gap-1.5">
      <span className={cn('mono text-body-sm min-w-[40px] shrink-0', ms > 3000 ? 'text-warn' : 'text-secondary')}>
        {fmtLatency(ms)}
      </span>
      <LatencyBar ms={ms} />
    </span>
  );
}
