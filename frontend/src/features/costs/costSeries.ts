import type {
  AgentCostPointDto,
  AgentCostTotalDto,
  ApiKeyCostPointDto,
  ApiKeyCostTotalDto,
} from '../../api/costs';
import type { StackedDatum } from '../../components/charts/chart-math';
import { agentColor } from '../../lib/colors';
import { bucketAxisLabel, type StatisticsBucket } from '../../lib/time-range';
import { resolveRange, type TimeRange } from '../../lib/timeRange';

/**
 * Pure series math for the Costs page. Framework-free and unit-tested (`costSeries.spec.ts`): the
 * sparse→dense bucketing, the StackedBar adapter, the month-end projection and the window
 * resolution the overview query needs.
 *
 * The API returns a **sparse** series (only cells that had spend). The chart needs a **dense**
 * bucket axis so a day with no traffic reads as a gap rather than compressing the timeline — so the
 * full UTC-aligned grid is generated from the window and the sparse rows folded into it.
 *
 * The bucketing is deliberately **dimension-agnostic**: spend is grouped by agent *or* by inbound
 * API key, and both feed the same chart. Callers convert their DTO rows to {@link CostSeriesPoint}
 * (via {@link agentPoints} / {@link apiKeyPoints}) and everything downstream is shared.
 */

/**
 * One sparse `(bucket, series)` cell. `seriesKey` is the agent id or the API key id; `null` is a
 * real, meaningful group — the unattributed remainder of the key dimension — not missing data.
 */
export interface CostSeriesPoint {
  bucketStart: string;
  seriesKey: string | null;
  costEur: number;
}

/** Adapts the per-agent API rows to the shared series shape. */
export function agentPoints(rows: readonly AgentCostPointDto[]): CostSeriesPoint[] {
  return rows.map(r => ({ bucketStart: r.bucketStart, seriesKey: r.agentId, costEur: r.costEur }));
}

/** Adapts the per-key API rows to the shared series shape, preserving the null (unattributed) key. */
export function apiKeyPoints(rows: readonly ApiKeyCostPointDto[]): CostSeriesPoint[] {
  return rows.map(r => ({ bucketStart: r.bucketStart, seriesKey: r.apiKeyId, costEur: r.costEur }));
}

const BUCKET_MS: Record<StatisticsBucket, number> = {
  fiveMinutes: 5 * 60_000,
  hourly: 60 * 60_000,
  daily: 24 * 60 * 60_000,
};

/** Hard cap on rendered bars — a wide window at a fine bucket would produce unreadable output. */
export const MAX_BUCKETS = 400;

export interface DenseCostBucket {
  startMs: number;
  iso: string;
  /** Per-series spend in this bucket, descending by amount. */
  cells: { seriesKey: string | null; costEur: number }[];
  totalEur: number;
}

export interface DenseCostSeries {
  buckets: DenseCostBucket[];
  /** True when older buckets were dropped to respect {@link MAX_BUCKETS}. */
  truncated: boolean;
}

/**
 * The concrete `from`/`to` the overview query needs (both required by the API). `to` is the range's
 * upper bound or *now*; `from` is the range's lower bound, falling back to the start of the current
 * UTC month for open-ended ranges so the page always opens on the period budgets are measured over.
 */
export function resolveCostWindow(range: TimeRange, nowMs: number = Date.now()): { from: string; to: string } {
  const resolved = resolveRange(range, nowMs);
  const to = range.kind === 'absolute' && range.to ? range.to : new Date(nowMs).toISOString();
  const from = resolved.from ?? monthStartIso(nowMs);
  return { from, to };
}

/**
 * {@link resolveCostWindow} with *now* quantized to the end of the current bucket, so a relative
 * range resolves to the **same** `from`/`to` strings across renders — a raw `Date.now()` would
 * change the query key (and refetch) on every render.
 */
export function quantizedCostWindow(
  range: TimeRange,
  bucket: StatisticsBucket,
  nowMs: number = Date.now(),
): { from: string; to: string } {
  const step = BUCKET_MS[bucket];
  return resolveCostWindow(range, Math.floor(nowMs / step) * step + step - 1);
}

/** Midnight UTC on the first of the month containing `ms` — the period budgets are measured over. */
export function monthStartIso(ms: number): string {
  const d = new Date(ms);
  return new Date(Date.UTC(d.getUTCFullYear(), d.getUTCMonth(), 1)).toISOString();
}

/**
 * Folds the sparse API rows onto the dense UTC-aligned bucket grid spanned by [from, to].
 * Buckets with no spend are kept (with an empty cell list) so gaps read as gaps.
 */
export function densifyCostSeries(
  rows: readonly CostSeriesPoint[],
  from: string,
  to: string,
  bucket: StatisticsBucket,
): DenseCostSeries {
  const step = BUCKET_MS[bucket];
  const fromMs = Date.parse(from);
  const toMs = Date.parse(to);
  if (!Number.isFinite(fromMs) || !Number.isFinite(toMs) || toMs < fromMs) {
    return { buckets: [], truncated: false };
  }

  const firstStart = Math.floor(fromMs / step) * step;
  const lastStart = Math.floor(toMs / step) * step;
  const totalCount = Math.floor((lastStart - firstStart) / step) + 1;
  const truncated = totalCount > MAX_BUCKETS;
  // Keep the most recent MAX_BUCKETS — a truncated view should show the present, not the past.
  const startMs = truncated ? lastStart - (MAX_BUCKETS - 1) * step : firstStart;
  const count = truncated ? MAX_BUCKETS : totalCount;

  // Map keys are `string | null`: the null group is the unattributed series and must survive the
  // fold like any other, rather than being silently dropped or merged into a neighbour.
  const byBucket = new Map<number, Map<string | null, number>>();
  for (const row of rows) {
    const ms = Date.parse(row.bucketStart);
    if (!Number.isFinite(ms)) continue;
    const slot = Math.floor(ms / step) * step;
    if (slot < startMs || slot > lastStart) continue;
    const cell = byBucket.get(slot) ?? new Map<string | null, number>();
    cell.set(row.seriesKey, (cell.get(row.seriesKey) ?? 0) + row.costEur);
    byBucket.set(slot, cell);
  }

  const buckets: DenseCostBucket[] = [];
  for (let i = 0; i < count; i++) {
    const slot = startMs + i * step;
    const cellMap = byBucket.get(slot);
    const cells = cellMap
      ? [...cellMap.entries()]
          .map(([seriesKey, costEur]) => ({ seriesKey, costEur }))
          .sort((a, b) => b.costEur - a.costEur)
      : [];
    buckets.push({
      startMs: slot,
      iso: new Date(slot).toISOString(),
      cells,
      totalEur: cells.reduce((sum, c) => sum + c.costEur, 0),
    });
  }

  return { buckets, truncated };
}

/**
 * Adapts the dense series to the `StackedBar` input, one segment per series key. `nameOf` resolves
 * the display name — including the label for the null (unattributed) key — so the chart never has
 * to know which dimension it is rendering.
 */
export function toStackedCostData(
  series: DenseCostSeries,
  bucket: StatisticsBucket,
  nameOf: (seriesKey: string | null) => string,
): StackedDatum[] {
  return series.buckets.map(b => ({
    label: bucketAxisLabel(b.iso, bucket),
    segments: b.cells.map(cell => ({
      label: nameOf(cell.seriesKey),
      value: cell.costEur,
      // The unattributed slice is deliberately muted rather than given a palette colour: it is a
      // remainder, not a peer of the named series.
      color: cell.seriesKey === null ? 'var(--text-muted)' : agentColor(cell.seriesKey),
    })),
  }));
}

/** Total spend of the dense series — what the window's headline figure shows. */
export function totalOf(series: DenseCostSeries): number {
  return series.buckets.reduce((sum, b) => sum + b.totalEur, 0);
}

/**
 * Straight-line projection of where month-to-date spend lands by month end, from the fraction of
 * the month already elapsed. Returns null before enough of the month has passed for the
 * extrapolation to mean anything — on the 1st, a single expensive hour would project a wild figure.
 */
export function projectMonthEnd(monthToDateEur: number, nowMs: number = Date.now()): number | null {
  const now = new Date(nowMs);
  const monthStart = Date.UTC(now.getUTCFullYear(), now.getUTCMonth(), 1);
  const monthEnd = Date.UTC(now.getUTCFullYear(), now.getUTCMonth() + 1, 1);
  const elapsed = nowMs - monthStart;
  const length = monthEnd - monthStart;
  if (elapsed <= 0 || length <= 0) return null;

  const fraction = elapsed / length;
  // Below ~5% of the month the extrapolation multiplies noise by 20 or more; say nothing instead.
  if (fraction < 0.05) return null;
  return monthToDateEur / fraction;
}

/**
 * Month-over-month change as a signed fraction (0.25 = 25% more than last month), or null when
 * last month had no spend at all — "up from zero" has no meaningful percentage.
 */
export function monthDelta(monthToDateEur: number, previousMonthEur: number): number | null {
  if (previousMonthEur <= 0) return null;
  return (monthToDateEur - previousMonthEur) / previousMonthEur;
}

/** The window's totals, largest first, capped to `limit` with the rest folded into one remainder. */
export function topBySpend<T extends { costEur: number }>(
  totals: readonly T[],
  limit: number,
): { rows: T[]; otherEur: number } {
  const sorted = [...totals].sort((a, b) => b.costEur - a.costEur);
  if (sorted.length <= limit) return { rows: sorted, otherEur: 0 };
  return {
    rows: sorted.slice(0, limit),
    otherEur: sorted.slice(limit).reduce((sum, t) => sum + t.costEur, 0),
  };
}

/** The window's per-agent totals, largest first, capped to `limit`. */
export function topAgents(
  totals: readonly AgentCostTotalDto[],
  limit: number,
): { rows: AgentCostTotalDto[]; otherEur: number } {
  return topBySpend(totals, limit);
}

/**
 * The window's per-key totals, largest first, capped to `limit`. The unattributed row is pinned
 * last regardless of size — it is a remainder, and letting it head the list would read as if one
 * key were the biggest spender.
 */
export function topApiKeys(
  totals: readonly ApiKeyCostTotalDto[],
  limit: number,
): { rows: ApiKeyCostTotalDto[]; otherEur: number; unattributedEur: number } {
  const unattributedEur = totals
    .filter(t => t.apiKeyId === null)
    .reduce((sum, t) => sum + t.costEur, 0);
  const { rows, otherEur } = topBySpend(totals.filter(t => t.apiKeyId !== null), limit);
  return { rows, otherEur, unattributedEur };
}
