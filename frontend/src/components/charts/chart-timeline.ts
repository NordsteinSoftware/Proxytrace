export interface TimelinePoint {
  /** Centre of the bucket's slot — where the hover playhead and marker sit. */
  x: number;
  /** Left/right edge of the bucket's slot. */
  x0: number;
  x1: number;
  /** Where the volume curve passes over this bucket (=== baselineY for an empty bucket). */
  totalY: number;
  /** Where the error curve passes under this bucket (=== baselineY when nothing failed). */
  errorY: number;
}

export interface TimelineTrace {
  /** Smooth curve through the volume series, above the baseline. */
  totalLine: string;
  /** The same curve closed down to the baseline: the flat fill body. */
  totalArea: string;
  /**
   * Error humps mirrored below the baseline, one closed path per run of error-bearing buckets.
   * Split rather than drawn as one series so a zero stretch leaves the baseline rule clean
   * instead of painting a red line along it.
   */
  errorAreas: string[];
  points: TimelinePoint[];
  /** Shared zero line: volume grows up from it, errors hang below it. */
  baselineY: number;
  plotL: number;
  plotR: number;
  plotT: number;
  /** Bottom of the mirrored error lane — the plot's lower edge. */
  plotB: number;
  maxTotal: number;
  maxErrors: number;
}

const PAD_L = 2, PAD_R = 2, PAD_T = 6;
/** Reserved for the time-axis ticks + labels below the plot. */
const PAD_B = 16;
/**
 * Share of the plot given to the mirrored error lane, clamped to stay readable at any height.
 * Deliberately the smaller half: errors are the rarer series but scaled to their own peak, so
 * without a height cap a 2% error rate paints as much ink as the traffic it came from.
 */
const ERROR_LANE_SHARE = 0.22, ERROR_LANE_MIN = 10, ERROR_LANE_MAX = 20;
/** Headroom above the tallest plateau so a peak never touches the top edge. */
const TOTAL_HEADROOM = 1.08;
/** Errors get a headroom of their own — the lane is scaled independently (see below). */
const ERROR_HEADROOM = 1.15;

type Pt = [number, number];

const r = (n: number) => Math.round(n * 100) / 100;

/**
 * Monotone cubic Hermite tangents (Fritsch–Carlson). Buckets are evenly spaced, so the interior
 * slope reduces to the harmonic mean of the two adjacent secants — zeroed at a local extremum.
 * That is what stops the curve overshooting: a plain Catmull-Rom spline dips *below* the axis
 * between a spike and a quiet bucket, drawing trace counts that never happened.
 */
function monotoneTangents(pts: Pt[]): number[] {
  const n = pts.length;
  const secants = Array.from({ length: n - 1 }, (_, i) =>
    (pts[i + 1][1] - pts[i][1]) / (pts[i + 1][0] - pts[i][0] || 1));
  return pts.map((_, i) => {
    if (i === 0) return secants[0];
    if (i === n - 1) return secants[n - 2];
    const a = secants[i - 1], b = secants[i];
    return a * b <= 0 ? 0 : (2 * a * b) / (a + b);
  });
}

/** The `C` commands joining `pts` — everything after the opening move. */
function curveSegments(pts: Pt[]): string {
  if (pts.length < 2) return '';
  const m = monotoneTangents(pts);
  let d = '';
  for (let i = 0; i < pts.length - 1; i++) {
    const [x0, y0] = pts[i], [x1, y1] = pts[i + 1];
    const dx = (x1 - x0) / 3;
    d += `C${r(x0 + dx)},${r(y0 + m[i] * dx)},${r(x1 - dx)},${r(y1 - m[i + 1] * dx)},${r(x1)},${r(y1)}`;
  }
  return d;
}

/** A curve closed to `baselineY` at both ends, so it fills as a self-contained body. */
function curveArea(pts: Pt[], baselineY: number): string {
  if (pts.length === 0) return '';
  const [fx, fy] = pts[0], [lx] = pts[pts.length - 1];
  return `M${r(fx)},${r(baselineY)}L${r(fx)},${r(fy)}${curveSegments(pts)}L${r(lx)},${r(baselineY)}Z`;
}

/**
 * Index ranges (inclusive) covering every error-bearing bucket, each padded by one bucket on
 * either side so the hump can descend to zero rather than ending mid-air. Ranges that meet after
 * padding are merged — two bursts one quiet bucket apart are one shape, not two.
 */
function errorRuns(values: number[]): [number, number][] {
  const runs: [number, number][] = [];
  values.forEach((v, i) => {
    if (v <= 0) return;
    const lo = Math.max(0, i - 1), hi = Math.min(values.length - 1, i + 1);
    const prev = runs[runs.length - 1];
    if (prev && lo <= prev[1]) prev[1] = Math.max(prev[1], hi);
    else runs.push([lo, hi]);
  });
  return runs;
}

/**
 * Lays the trace histogram out as a mirrored scope trace filling the full width (no axis gutter —
 * this is a full-bleed timeline strip). Volume curves up from a shared baseline; errors hang below
 * it in a short lane of their own. Both series are sampled at bucket centres and smoothed.
 *
 * The two lanes are scaled **independently**: errors are rare by definition, so sharing the volume
 * scale would flatten every incident into an invisible sliver. The lane's smaller height and its
 * position below the zero line keep the asymmetry legible, and the hover readout carries the exact
 * counts.
 */
export function computeTimelineTrace(
  buckets: { total: number; errors: number }[],
  width: number,
  height: number,
): TimelineTrace {
  const plotW = Math.max(width - PAD_L - PAD_R, 0);
  const plotH = Math.max(height - PAD_T - PAD_B, 0);
  const errorLaneH = Math.min(ERROR_LANE_MAX, Math.max(ERROR_LANE_MIN, Math.round(plotH * ERROR_LANE_SHARE)));
  const volumeH = Math.max(plotH - errorLaneH, 0);
  const baselineY = PAD_T + volumeH;
  const plotL = PAD_L, plotR = PAD_L + plotW, plotT = PAD_T, plotB = PAD_T + plotH;

  const totals = buckets.map(b => b.total);
  const errors = buckets.map(b => b.errors);
  const maxTotal = Math.max(0, ...totals);
  const maxErrors = Math.max(0, ...errors);
  const totalDenom = (maxTotal || 1) * TOTAL_HEADROOM;
  const errorDenom = (maxErrors || 1) * ERROR_HEADROOM;

  const slot = buckets.length > 0 ? plotW / buckets.length : plotW;
  const centreX = (i: number) => plotL + (i + 0.5) * slot;
  const totalYs = totals.map(v => baselineY - (v / totalDenom) * volumeH);
  const errorYs = errors.map(v => baselineY + (v / errorDenom) * errorLaneH);

  // The curve is sampled at bucket centres, then run flat out to both edges so the strip is
  // filled end to end without inventing data past the first and last bucket.
  const centres: Pt[] = totalYs.map((y, i) => [centreX(i), y]);
  const firstY = centres[0]?.[1] ?? baselineY;
  const lastY = centres[centres.length - 1]?.[1] ?? baselineY;
  const body = centres.length > 0
    ? `L${r(centres[0][0])},${r(firstY)}${curveSegments(centres)}L${r(plotR)},${r(lastY)}`
    : '';

  return {
    totalLine: body === '' ? '' : `M${r(plotL)},${r(firstY)}${body}`,
    totalArea: body === ''
      ? ''
      : `M${r(plotL)},${r(baselineY)}L${r(plotL)},${r(firstY)}${body}L${r(plotR)},${r(baselineY)}Z`,
    errorAreas: errorRuns(errors).map(([lo, hi]) =>
      curveArea(errorYs.slice(lo, hi + 1).map((y, i): Pt => [centreX(lo + i), y]), baselineY),
    ),
    points: buckets.map((_, i) => ({
      x: centreX(i),
      x0: plotL + i * slot,
      x1: plotL + (i + 1) * slot,
      totalY: totalYs[i],
      errorY: errorYs[i],
    })),
    baselineY,
    plotL,
    plotR,
    plotT,
    plotB,
    maxTotal,
    maxErrors,
  };
}

export function timeToX(t: number, from: number, to: number, plotL: number, plotR: number): number {
  if (to <= from) return plotL;
  const frac = Math.min(1, Math.max(0, (t - from) / (to - from)));
  return plotL + frac * (plotR - plotL);
}

export function xToTime(x: number, from: number, to: number, plotL: number, plotR: number): number {
  if (plotR <= plotL) return from;
  const frac = Math.min(1, Math.max(0, (x - plotL) / (plotR - plotL)));
  return from + frac * (to - from);
}

/** Shrink (or grow) the window [from, to] by `factor`, keeping `pivot` at the same relative spot. */
export function zoomTowardPivot(
  pivot: number, from: number, to: number, factor: number,
): { from: number; to: number } {
  return { from: pivot - (pivot - from) * factor, to: pivot + (to - pivot) * factor };
}

export interface TimeAxisTick { x: number; label: string; anchor: 'start' | 'middle' | 'end'; }

const DAY_MS = 86_400_000;

/**
 * Format a hovered bucket's instant for the readout. Always keeps the clock — the reader is
 * pinpointing a slice — and adds the date once the window spans more than a day, where a bare
 * `14:40` could mean any of seven afternoons.
 */
export function formatBucketInstant(ms: number, spanMs: number): string {
  const d = new Date(ms);
  const time = d.toLocaleTimeString();
  if (spanMs <= DAY_MS) return time;
  return `${d.toLocaleDateString([], { month: 'short', day: 'numeric' })} ${time}`;
}

/** Format an epoch-ms instant for a timeline axis tick, picking granularity from the window span. */
export function formatAxisTime(ms: number, spanMs: number): string {
  const d = new Date(ms);
  const time = d.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
  if (spanMs <= 2 * DAY_MS) return time;
  const date = d.toLocaleDateString([], { month: 'short', day: 'numeric' });
  if (spanMs <= 14 * DAY_MS) return `${date} ${time}`;
  return date;
}

/** Evenly-spaced time ticks across [from, to]; edge ticks anchor inward so labels never clip. */
export function timelineAxisTicks(
  from: number, to: number, plotL: number, plotR: number, count: number,
): TimeAxisTick[] {
  if (to <= from || plotR <= plotL || count < 2) return [];
  const span = to - from;
  return Array.from({ length: count }, (_, i) => {
    const frac = i / (count - 1);
    const anchor: TimeAxisTick['anchor'] = i === 0 ? 'start' : i === count - 1 ? 'end' : 'middle';
    return { x: plotL + frac * (plotR - plotL), label: formatAxisTime(from + frac * span, span), anchor };
  });
}
