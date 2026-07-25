// Pure derive/format helpers for the traces UI. No JSX, no I/O — unit-tested
// in tracesMeta.spec.ts.

import { msg } from '@lingui/core/macro';
import { type MessageDescriptor } from '@lingui/core';
import type { AgentCallFilter, AgentCallListItemDto } from '../../api/models';
import { ALL_TIME, type TimeRange, type TimeRangePreset } from '../../lib/timeRange';

// ── Time range helpers ────────────────────────────────────────────────────────

/** Auto-default thresholds: smallest preset whose window still contains the newest trace. */
const AUTO_THRESHOLDS: readonly { maxAgeMs: number; preset: TimeRangePreset }[] = [
  { maxAgeMs: 15 * 60_000, preset: '15m' },
  { maxAgeMs: 60 * 60_000, preset: '1h' },
  { maxAgeMs: 6 * 60 * 60_000, preset: '6h' },
  { maxAgeMs: 24 * 60 * 60_000, preset: '24h' },
  { maxAgeMs: 7 * 24 * 60 * 60_000, preset: '7d' },
  { maxAgeMs: 30 * 24 * 60 * 60_000, preset: '30d' },
];

/** Smallest preset window that still contains the newest trace; "all time" when none/too old. */
export function autoTimeRange(newestTraceIso: string | null, now: number = Date.now()): TimeRange {
  if (!newestTraceIso) return ALL_TIME;
  const age = now - new Date(newestTraceIso).getTime();
  const match = AUTO_THRESHOLDS.find(t => age <= t.maxAgeMs);
  return match ? { kind: 'preset', preset: match.preset } : ALL_TIME;
}

// ── Row types / grouping (shared with the dashboard live stream) ───────────────

export { buildRows } from '../../lib/trace';
export type { ConversationGroup, FlatTrace, TraceRow } from '../../lib/trace';

import type { AgentCallListItemDto as _AgentCallListItemDto } from '../../api/models';
import type { TraceRow as _TraceRow } from '../../lib/trace';

/**
 * Ungrouped rows: every trace becomes its own flat row, order preserved. Used wherever conversation
 * grouping is intentionally disabled (a metric sort on the Traces tab, the session timeline) so the
 * "flat row" shape isn't re-inlined per call site.
 */
export function flatRows(traces: _AgentCallListItemDto[]): _TraceRow[] {
  return traces.map(trace => ({ type: 'flat', trace }));
}

/**
 * Tool-request count for a trace — the backend precomputes it into the light row
 * ({@link AgentCallListItemDto.toolCount}; 0 when the call produced no response).
 */
export function toolCount(trace: AgentCallListItemDto): number {
  return trace.toolCount;
}

// ── Advanced (composable) filters ───────────────────────────────────────────────

export type TraceAnomalyFilter = '' | 'any' | 'highTokens' | 'highLatency' | 'lowCacheHit' | 'manyToolCalls' | 'custom';
export type TraceStatusClassFilter = '' | '2' | '4' | '5';

/**
 * The filter-bar state: one slot per composable filter, empty string = not active. Numeric bounds
 * stay strings so the inputs remain controlled; {@link advancedFilterParams} parses them.
 */
export interface TraceAdvancedFilters {
  agent: string;
  session: string;
  anomaly: TraceAnomalyFilter;
  tool: string;
  model: string;
  statusClass: TraceStatusClassFilter;
  minTokens: string;
  maxTokens: string;
  minLatencyMs: string;
  maxLatencyMs: string;
}

export const EMPTY_ADVANCED_FILTERS: TraceAdvancedFilters = {
  agent: '',
  session: '',
  anomaly: '',
  tool: '',
  model: '',
  statusClass: '',
  minTokens: '',
  maxTokens: '',
  minLatencyMs: '',
  maxLatencyMs: '',
};

/** Backend OutlierFlags bit per specific anomaly option (mirrors OutlierFlags.cs). */
export const ANOMALY_FLAG_BITS: Record<Exclude<TraceAnomalyFilter, '' | 'any'>, number> = {
  highTokens: 1,
  highLatency: 2,
  lowCacheHit: 4,
  manyToolCalls: 8,
  custom: 16,
};

const ANOMALY_VALUES: readonly TraceAnomalyFilter[] = ['', 'any', 'highTokens', 'highLatency', 'lowCacheHit', 'manyToolCalls', 'custom'];
const STATUS_CLASS_VALUES: readonly TraceStatusClassFilter[] = ['', '2', '4', '5'];

/** Guard a value parsed from storage against the {@link TraceAdvancedFilters} shape. */
export function isValidAdvancedFilters(v: unknown): v is TraceAdvancedFilters {
  if (typeof v !== 'object' || v === null) return false;
  const f = v as Record<keyof TraceAdvancedFilters, unknown>;
  return (
    typeof f.agent === 'string' &&
    typeof f.session === 'string' &&
    typeof f.tool === 'string' &&
    typeof f.model === 'string' &&
    typeof f.minTokens === 'string' &&
    typeof f.maxTokens === 'string' &&
    typeof f.minLatencyMs === 'string' &&
    typeof f.maxLatencyMs === 'string' &&
    ANOMALY_VALUES.includes(f.anomaly as TraceAnomalyFilter) &&
    STATUS_CLASS_VALUES.includes(f.statusClass as TraceStatusClassFilter)
  );
}

function numericParam(raw: string): number | null {
  const n = Number(raw);
  return raw.trim() !== '' && Number.isFinite(n) ? n : null;
}

/** API query params for the active advanced filters (blank / unparsable slots map to nothing). */
export function advancedFilterParams(f: TraceAdvancedFilters): Partial<AgentCallFilter> {
  const minTokens = numericParam(f.minTokens);
  const maxTokens = numericParam(f.maxTokens);
  const minLatencyMs = numericParam(f.minLatencyMs);
  const maxLatencyMs = numericParam(f.maxLatencyMs);
  return {
    ...(f.agent ? { agentId: f.agent } : {}),
    ...(f.session ? { sessionId: f.session } : {}),
    ...(f.anomaly === 'any' ? { outlierOnly: true } : {}),
    ...(f.anomaly && f.anomaly !== 'any' ? { anomalyFlags: ANOMALY_FLAG_BITS[f.anomaly] } : {}),
    ...(f.tool ? { toolName: f.tool } : {}),
    ...(f.model ? { model: f.model } : {}),
    ...(f.statusClass ? { httpStatusClass: Number(f.statusClass) } : {}),
    ...(minTokens !== null ? { minTokens } : {}),
    ...(maxTokens !== null ? { maxTokens } : {}),
    ...(minLatencyMs !== null ? { minLatencyMs } : {}),
    ...(maxLatencyMs !== null ? { maxLatencyMs } : {}),
  };
}

/** Number of active filter-bar slots (tokens/latency each count once per bound). */
export function countActiveAdvancedFilters(f: TraceAdvancedFilters): number {
  return (Object.keys(EMPTY_ADVANCED_FILTERS) as (keyof TraceAdvancedFilters)[])
    .filter(k => f[k] !== '').length;
}

/**
 * Whether any user-applied filter is narrowing the traces list. Drives the empty state: an empty
 * list with a filter active shows "no traces match your filters"; with none, the first-time setup
 * instructions. Every filter that can hide rows MUST be reflected here — omitting one (historically
 * the outliers-only toggle) makes a filtered-empty list look like an empty project and wrongly shows
 * the setup instructions. `showSystem` is deliberately excluded: its default (system traces hidden)
 * is the baseline view, so counting it would mark a genuinely empty project as filtered.
 */
export function hasActiveTraceFilters(input: {
  search: string;
  timeRangeActive: boolean;
  advanced: TraceAdvancedFilters;
}): boolean {
  return (
    input.search.trim().length > 0 ||
    input.timeRangeActive ||
    countActiveAdvancedFilters(input.advanced) > 0
  );
}

export type TraceListView = 'rows' | 'loading' | 'empty-filtered' | 'empty-setup';

/**
 * Which view the traces list should render. Extracted from TraceTable's JSX so the bug-prone
 * "filtered-empty vs genuinely-empty" decision is unit-testable in isolation: a filtered-empty list
 * MUST be `empty-filtered` (the "no traces match your filters" message), never `empty-setup` (the
 * first-time setup instructions). `filtered` comes from {@link hasActiveTraceFilters}.
 */
export function traceListView(rowCount: number, isFetching: boolean, filtered: boolean): TraceListView {
  if (rowCount > 0) return 'rows';
  if (isFetching) return 'loading';
  return filtered ? 'empty-filtered' : 'empty-setup';
}

// ── Column layout (shared between header row and all trace rows) ───────────────

// Minimums are sized so the row still fits a ~900px list (1024px viewport with collapsed
// sidebar); fixed-ish columns get a minmax upper bound so wide screens keep today's layout.
// The anomaly indicator is a super-narrow, fixed 36px track for the outlier chip (empty on normal
// rows). It sits just before the timestamp, between Latency and Time.
export const COL_WIDTHS = ['minmax(170px,2fr)', 'minmax(96px,1fr)', 'minmax(104px,140px)', '64px', '56px', 'minmax(96px,130px)', 'minmax(64px,84px)', 'minmax(88px,120px)', '36px', '72px'] as const;
export const GRID_TEMPLATE = COL_WIDTHS.join(' ');

// The '' entry is the anomaly column — its header is an icon, not text (see TraceTable), so the
// string is blank; the accessible name comes from COL_HEADER_LABELS instead.
export const COL_HEADERS = ['Message', 'Agent', 'Model', 'Status', 'Tools', 'Tokens', 'Cached', 'Latency', '', 'Time'] as const;

/** Translatable header labels, index-aligned with {@link COL_HEADERS}; resolve at render with i18n._(). */
export const COL_HEADER_LABELS: readonly MessageDescriptor[] = [
  msg`Message`, msg`Agent`, msg`Model`, msg`Status`, msg`Tools`, msg`Tokens`, msg`Cached`, msg`Latency`, msg`Anomalies`, msg`Time`,
];

// ── Narrow-list (mobile) column collapse ───────────────────────────────────────
// On a list narrower than the @2xl container breakpoint (phones), only Message /
// Status / Time survive — the rest are drill-in detail. The list container
// (TraceTable) declares `@container` and exposes both templates as CSS vars; every
// row applies TRACE_GRID_CLS so header and rows stay column-aligned in both modes.
export const COL_MOBILE_VISIBLE = [true, false, false, true, false, false, false, false, true, true] as const;
export const GRID_TEMPLATE_NARROW = COL_WIDTHS.filter((_, i) => COL_MOBILE_VISIBLE[i]).join(' ');

/** Grid-template classes shared by the header row and every trace/turn row. */
export const TRACE_GRID_CLS =
  '[grid-template-columns:var(--trace-grid)] @max-2xl:[grid-template-columns:var(--trace-grid-narrow)]';

/** Per-column visibility class, index-aligned with COL_WIDTHS/COL_HEADERS. */
export const COL_VIS_CLS = COL_MOBILE_VISIBLE.map(v => (v ? '' : '@max-2xl:hidden'));

// ── Column sorting ──────────────────────────────────────────────────────────────

export type TraceSortField = 'time' | 'latency' | 'tokens' | 'toolCount' | 'cacheHit';

export interface TraceSort {
  field: TraceSortField;
  desc: boolean;
}

export const DEFAULT_TRACE_SORT: TraceSort = { field: 'time', desc: true };

/** Backend `AgentCallSortField` enum member per sort field (query-string `sortBy` value). */
export const SORT_FIELD_TO_API: Record<TraceSortField, string> = {
  time: 'createdAt',
  latency: 'latency',
  tokens: 'totalTokens',
  toolCount: 'toolCount',
  cacheHit: 'cacheHitRate',
};

/** Sort field per column, index-aligned with {@link COL_HEADERS}; null = not sortable. */
export const SORT_FIELD_BY_COL: readonly (TraceSortField | null)[] = [
  null, null, null, null, 'toolCount', 'tokens', 'cacheHit', 'latency', null, 'time',
];

/** Guard a value parsed from storage against the {@link TraceSort} shape (user-editable JSON). */
export function isValidTraceSort(v: unknown): v is TraceSort {
  if (typeof v !== 'object' || v === null) return false;
  const s = v as { field?: unknown; desc?: unknown };
  return typeof s.desc === 'boolean' && typeof s.field === 'string' && s.field in SORT_FIELD_TO_API;
}

// ── Latency bar math ──────────────────────────────────────────────────────────

/** Returns percentage (0–100) for the latency mini-bar (scale: 6 000 ms = 100%). */
export function latencyBarPct(ms: number): number {
  return Math.min(100, ms / 60);
}
