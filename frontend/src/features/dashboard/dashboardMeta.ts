// Pure constants, label maps, and derivations for the Dashboard.
// No JSX, no I/O — unit-tested in dashboardMeta.spec.ts.
//
// This module keeps the small cross-cutting bits (range list, shared class recipes, telemetry
// + token-number formatters) and re-exports the per-concern derivation modules so consumers can
// keep importing everything from `./dashboardMeta`:
//   - tokenSeries.ts  — token-volume time-series gap-fill
//   - latency.ts      — weighted latency percentiles + endpoint spectrum
//   - agentFleet.ts   — the agent-fleet roster + sparklines
//   - modelSplit.ts   — top-model token split
//   - pulse.ts        — live-pulse window transforms

import { fmtTokens } from '../../lib/format';
import type { RangeKey } from '../../lib/time-range';

export * from './tokenSeries';
export * from './latency';
export * from './agentFleet';
export * from './modelSplit';
export * from './pulse';
export * from './trendDeltas';

export const RANGES: RangeKey[] = ['1h', '24h', '7d', '30d', 'all'];

/** Scope-select sentinel for "every scope" (Radix Select items cannot carry an empty value). */
export const ALL_SCOPES = 'all';

// ── Shared dashboard class recipes ───────────────────────────────────────────

/** Section eyebrow label — the dashboard cards' shared mono-uppercase header treatment. */
export const EYEBROW_CLS = 'text-caption text-secondary font-mono tracking-[0.16em] uppercase font-bold';

/** Column-header label for the dashboard's aligned data grids and stat strips. */
export const COL_HEADER_CLS = 'text-caption font-bold text-secondary tracking-[0.12em] uppercase font-mono';

// ── Telemetry formatter ──────────────────────────────────────────────────────

/** Returns '—' for missing values, otherwise formats numbers or returns strings. */
export function teleFmt(
  v: string | number | undefined | null,
  fmt?: (n: number) => string,
): string {
  if (v === undefined || v === null) return '—';
  if (typeof v === 'number' && fmt) return fmt(v);
  return String(v);
}

// ── Token number splitting ───────────────────────────────────────────────────

export interface TokenDisplay {
  num: string;
  suffix: string;
}

/** Split a formatted token string like "1.2M" into number and suffix parts. */
export function splitTokenStr(totalTokens: number): TokenDisplay {
  const s = fmtTokens(totalTokens);
  const match = s.match(/^([\d.,]+)(\D*)$/);
  return { num: match?.[1] ?? s, suffix: match?.[2] ?? '' };
}
