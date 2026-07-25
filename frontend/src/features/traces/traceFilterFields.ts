// Field registry for the composable trace filter bar. Pure — no JSX, no I/O.

import { msg } from '@lingui/core/macro';
import type { MessageDescriptor } from '@lingui/core';
import type { TraceAdvancedFilters, TraceAnomalyFilter } from './tracesMeta';

/** One chip slot in the filter bar; `tokens`/`latency` each own a min+max pair. */
export type TraceFilterFieldKey = 'agent' | 'session' | 'anomaly' | 'tool' | 'model' | 'status' | 'tokens' | 'latency';

export const TRACE_FILTER_FIELDS: readonly { key: TraceFilterFieldKey; label: MessageDescriptor }[] = [
  { key: 'agent', label: msg`Agent` },
  { key: 'session', label: msg`Session` },
  { key: 'anomaly', label: msg`Anomaly` },
  { key: 'tool', label: msg`Tool` },
  { key: 'model', label: msg`Model` },
  { key: 'status', label: msg`Status` },
  { key: 'tokens', label: msg`Tokens` },
  { key: 'latency', label: msg`Latency` },
];

export const ANOMALY_OPTION_LABELS: Record<Exclude<TraceAnomalyFilter, ''>, MessageDescriptor> = {
  any: msg`Any anomaly`,
  highTokens: msg`High tokens`,
  highLatency: msg`High latency`,
  lowCacheHit: msg`Low cache hit`,
  manyToolCalls: msg`Many tool calls`,
  custom: msg`Custom detector`,
};

export function isFieldActive(field: TraceFilterFieldKey, f: TraceAdvancedFilters): boolean {
  switch (field) {
    case 'agent': return f.agent !== '';
    case 'session': return f.session !== '';
    case 'anomaly': return f.anomaly !== '';
    case 'tool': return f.tool !== '';
    case 'model': return f.model !== '';
    case 'status': return f.statusClass !== '';
    case 'tokens': return f.minTokens !== '' || f.maxTokens !== '';
    case 'latency': return f.minLatencyMs !== '' || f.maxLatencyMs !== '';
  }
}

/** The patch that clears one chip slot. */
export function clearFieldPatch(field: TraceFilterFieldKey): Partial<TraceAdvancedFilters> {
  switch (field) {
    case 'agent': return { agent: '' };
    case 'session': return { session: '' };
    case 'anomaly': return { anomaly: '' };
    case 'tool': return { tool: '' };
    case 'model': return { model: '' };
    case 'status': return { statusClass: '' };
    case 'tokens': return { minTokens: '', maxTokens: '' };
    case 'latency': return { minLatencyMs: '', maxLatencyMs: '' };
  }
}

/** Chip value text for a min/max pair, e.g. "≥ 100 · ≤ 5000" (unit suffix optional). */
export function rangeChipValue(min: string, max: string, unit = ''): string {
  const parts: string[] = [];
  if (min !== '') parts.push(`≥ ${min}${unit}`);
  if (max !== '') parts.push(`≤ ${max}${unit}`);
  return parts.join(' · ');
}
