export function fmtLatency(ms: number): string {
  if (ms < 1000) return `${Math.round(ms)}ms`;
  return `${(ms / 1000).toFixed(1)}s`;
}

export function fmtTokens(n: number): string {
  if (n >= 1_000_000) return `${(n / 1_000_000).toFixed(1)}M`;
  if (n >= 1000) return `${(n / 1000).toFixed(1)}k`;
  return String(n);
}

/**
 * Share of the input tokens served from the provider cache, as a whole percentage (0–100), or
 * `null` when there is no input or no cached portion to report. Drives the "(N% cached)" hint shown
 * beside input-token figures. Clamps a cached count larger than the input to 100%.
 */
export function cachedPct(cachedInput: number, input: number): number | null {
  if (input <= 0 || cachedInput <= 0) return null;
  const pct = Math.round((Math.min(cachedInput, input) / input) * 100);
  return pct > 0 ? pct : null;
}

const pad2 = (n: number) => String(n).padStart(2, '0');

/**
 * Browser-local date, "dd.MM.yyyy". App-wide canonical date format — 24-hour, day-first,
 * dot-separated. Do not re-implement date formatting in features; reuse these helpers.
 */
export function fmtDate(iso: string): string {
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return '—';
  return `${pad2(d.getDate())}.${pad2(d.getMonth() + 1)}.${d.getFullYear()}`;
}

/** UTC sibling of {@link fmtDate}, "dd.MM.yyyy" in UTC — for UTC instants (e.g. schedule run-times). */
export function fmtDateUtc(iso: string): string {
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return '—';
  return `${pad2(d.getUTCDate())}.${pad2(d.getUTCMonth() + 1)}.${d.getUTCFullYear()}`;
}

/** Date + 24h time to the minute, "dd.MM.yyyy HH:mm". For compact displays (chips, headers, pickers). */
export function fmtDateTimeShort(iso: string): string {
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return '—';
  return `${fmtDate(iso)} ${pad2(d.getHours())}:${pad2(d.getMinutes())}`;
}

/**
 * Date + 24h time to the minute in **UTC**, "dd.MM.yyyy HH:mm". For schedule run-times, which are
 * UTC (the scheduler fires in UTC) — pair with a " UTC" label at the call site.
 */
export function fmtDateTimeShortUtc(iso: string): string {
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return '—';
  return `${pad2(d.getUTCDate())}.${pad2(d.getUTCMonth() + 1)}.${d.getUTCFullYear()} ${pad2(d.getUTCHours())}:${pad2(d.getUTCMinutes())}`;
}

/** Date + 24h time with seconds, "dd.MM.yyyy HH:mm:ss". For log rows where exact time matters. */
export function fmtDateTime(iso: string): string {
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return '—';
  return `${fmtDateTimeShort(iso)}:${pad2(d.getSeconds())}`;
}

/**
 * Formats how long ago `iso` was, relative to `nowMs` (defaults to `Date.now()`).
 * Pass an explicit `nowMs` from a re-rendering tick (e.g. {@link useNowTick}) so the
 * label recomputes on a timer instead of only when the component re-renders for
 * another reason.
 */
export function fmtRelative(iso: string, nowMs: number = Date.now()): string {
  const diff = nowMs - new Date(iso).getTime();
  const s = Math.floor(diff / 1000);
  if (s < 60) return `${s}s ago`;
  const m = Math.floor(s / 60);
  if (m < 60) return `${m}m ago`;
  const h = Math.floor(m / 60);
  if (h < 24) return `${h}h ago`;
  const day = Math.floor(h / 24);
  if (day < 7) return `${day}d ago`;
  return fmtDate(iso);
}

/**
 * Future-facing counterpart to {@link fmtRelative}: formats how long *until* a timestamp,
 * e.g. "in 30m" / "in 2h" / "in 3d". Returns "due" when the time is already past (or now).
 * Mirrors `fmtRelative`'s unit thresholds; falls back beyond a week out to an absolute **UTC** date
 * (it is used only for UTC schedule run-times, shown beside a UTC clock time — keep them consistent).
 */
export function fmtUntil(iso: string): string {
  const diff = new Date(iso).getTime() - Date.now();
  const s = Math.floor(diff / 1000);
  if (s <= 0) return 'due';
  if (s < 60) return `in ${s}s`;
  const m = Math.floor(s / 60);
  if (m < 60) return `in ${m}m`;
  const h = Math.floor(m / 60);
  if (h < 24) return `in ${h}h`;
  const day = Math.floor(h / 24);
  if (day < 7) return `in ${day}d`;
  return fmtDateUtc(iso);
}

export function fmtDuration(ms: number | null | undefined): string {
  if (ms == null) return '—';
  if (ms < 1000) return `${Math.round(ms)}ms`;
  const s = ms / 1000;
  if (s < 60) return `${s.toFixed(1)}s`;
  const m = Math.floor(s / 60);
  const rem = Math.round(s % 60);
  return `${m}m ${rem}s`;
}

/** Formats elapsed whole seconds as a compact m:ss stopwatch readout (`0:07`, `1:23`). */
export function fmtElapsed(seconds: number): string {
  const s = Math.max(0, Math.floor(seconds));
  const minutes = Math.floor(s / 60);
  const rest = s % 60;
  return `${minutes}:${String(rest).padStart(2, '0')}`;
}

export function fmtPct(v: number): string {
  return `${Math.round(v * 100)}%`;
}

/** Formats a value that is already a percentage (0–100, e.g. run/suite `passRate` DTOs). */
export function fmtPct100(v: number): string {
  return `${Math.round(v)}%`;
}

/**
 * Formats a cost in EUR (all costs in Proxytrace are EUR, converted from the USD price feed).
 *
 * **Always exactly two decimals**, whatever the magnitude. The old rule switched to four below €1,
 * so the same column mixed `€0.0004` and `€12,345.68` and a zero total read as the distinctly
 * unmoneylike `€0.0000` — money is read by comparing shapes, and one that changes shape per row
 * cannot be scanned.
 *
 * The one exception is the sub-cent floor: a real but tiny amount would round to `€0.00`, which is
 * indistinguishable from free. Those render `<€0.01` — still two decimals, and honest about being
 * something rather than nothing. Per-call precision lives in the trace detail, which formats the
 * raw `costEur` itself.
 */
export function fmtCost(eur: number | null | undefined): string {
  if (eur == null) return '—';
  if (eur > 0 && eur < 0.005) return '<€0.01';
  return `€${eur.toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`;
}
