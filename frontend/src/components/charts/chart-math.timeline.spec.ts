import { describe, it, expect } from 'vitest';
import { computeTimelineTrace, timeToX, xToTime, timelineAxisTicks, formatAxisTime, formatBucketInstant, zoomTowardPivot } from './chart-math';

describe('computeTimelineTrace', () => {
  const buckets = [
    { total: 0, errors: 0 },
    { total: 10, errors: 0 },
    { total: 10, errors: 5 },
    { total: 4, errors: 4 },
  ];

  it('samples one point per bucket centre and runs the curve edge to edge', () => {
    const t = computeTimelineTrace(buckets, 400, 92);
    expect(t.points).toHaveLength(4);
    expect(t.points[0].x0).toBeCloseTo(t.plotL, 5);
    expect(t.points[3].x1).toBeCloseTo(t.plotR, 5);
    // Each sample sits at the centre of its own slot.
    t.points.forEach(p => expect(p.x).toBeCloseTo((p.x0 + p.x1) / 2, 5));
    expect(t.totalLine.startsWith(`M${t.plotL},`)).toBe(true);
    expect(t.totalLine).toContain('C');                     // smoothed, not a polyline
    expect(t.totalLine).toContain(`L${t.plotR},`);          // flat lead-out to the right edge
  });

  it('grows volume upward from the baseline, scaled to the tallest bucket', () => {
    const t = computeTimelineTrace(buckets, 400, 92);
    expect(t.maxTotal).toBe(10);
    expect(t.points[0].totalY).toBeCloseTo(t.baselineY, 5); // empty bucket rests on zero
    expect(t.points[1].totalY).toBeLessThan(t.baselineY);
    expect(t.points[1].totalY).toBeGreaterThan(t.plotT);    // headroom keeps the peak off the top edge
    expect(t.points[3].totalY).toBeGreaterThan(t.points[1].totalY); // 4 < 10, so it sits lower
  });

  it('never lets the smoothed curve overshoot past zero or the peak', () => {
    // A lone spike between empty buckets is where a naive spline undershoots below the axis.
    const spike = [
      { total: 0, errors: 0 },
      { total: 0, errors: 0 },
      { total: 50, errors: 0 },
      { total: 0, errors: 0 },
      { total: 0, errors: 0 },
    ];
    const t = computeTimelineTrace(spike, 600, 92);
    const peakY = t.points[2].totalY;
    // The path is nothing but coordinate pairs, so every odd number is a y — anchors and Bézier
    // control points alike. A curve is bounded by its control polygon, so checking those bounds
    // the drawn curve too.
    const nums = [...t.totalLine.matchAll(/-?\d+(?:\.\d+)?/g)].map(m => Number(m[0]));
    const ys = nums.filter((_, i) => i % 2 === 1);
    expect(ys.length).toBeGreaterThan(0);
    ys.forEach(y => {
      expect(y).toBeLessThanOrEqual(t.baselineY + 0.01); // no dip below zero traces
      expect(y).toBeGreaterThanOrEqual(peakY - 0.01);    // no bulge above the actual peak
    });
  });

  it('mirrors errors below the baseline within the error lane', () => {
    const t = computeTimelineTrace(buckets, 400, 92);
    expect(t.maxErrors).toBe(5);
    expect(t.points[0].errorY).toBeCloseTo(t.baselineY, 5);
    expect(t.points[2].errorY).toBeGreaterThan(t.baselineY);
    expect(t.points[2].errorY).toBeLessThanOrEqual(t.plotB);
  });

  it('cuts the error series into one hump per run of error-bearing buckets', () => {
    // Two bursts, far enough apart that padding them by a bucket each side keeps them separate.
    const gapped = [0, 2, 0, 0, 0, 1, 3].map(errors => ({ total: 5, errors }));
    const t = computeTimelineTrace(gapped, 400, 92);
    expect(t.errorAreas).toHaveLength(2);
    t.errorAreas.forEach(d => expect(d.endsWith(`,${t.baselineY}Z`)).toBe(true));
  });

  it('merges bursts that are only a bucket apart into one hump', () => {
    const adjacent = [0, 2, 0, 1, 0].map(errors => ({ total: 5, errors }));
    expect(computeTimelineTrace(adjacent, 400, 92).errorAreas).toHaveLength(1);
  });

  it('draws no error geometry when nothing failed', () => {
    const clean = [{ total: 3, errors: 0 }, { total: 7, errors: 0 }];
    const t = computeTimelineTrace(clean, 400, 92);
    expect(t.errorAreas).toEqual([]);
    expect(t.maxErrors).toBe(0);
  });

  it('survives an empty bucket list', () => {
    const t = computeTimelineTrace([], 400, 92);
    expect(t.points).toEqual([]);
    expect(t.totalLine).toBe('');
    expect(t.totalArea).toBe('');
    expect(t.errorAreas).toEqual([]);
  });
});

describe('timeToX / xToTime', () => {
  it('round-trips a time within the window', () => {
    const from = 1000, to = 5000, plotL = 10, plotR = 410;
    const x = timeToX(3000, from, to, plotL, plotR);
    expect(x).toBeCloseTo(210, 1);
    expect(xToTime(x, from, to, plotL, plotR)).toBeCloseTo(3000, 1);
  });

  it('clamps outside the plot range', () => {
    expect(xToTime(-50, 1000, 5000, 10, 410)).toBe(1000);
    expect(xToTime(9999, 1000, 5000, 10, 410)).toBe(5000);
  });
});

describe('timelineAxisTicks', () => {
  it('spreads ticks evenly across the plot with inward-anchored edges', () => {
    const ticks = timelineAxisTicks(0, 4000, 10, 410, 5);
    expect(ticks).toHaveLength(5);
    expect(ticks[0].x).toBeCloseTo(10, 5);
    expect(ticks[2].x).toBeCloseTo(210, 5);
    expect(ticks[4].x).toBeCloseTo(410, 5);
    expect(ticks[0].anchor).toBe('start');
    expect(ticks[2].anchor).toBe('middle');
    expect(ticks[4].anchor).toBe('end');
  });

  it('returns nothing for a degenerate window or count', () => {
    expect(timelineAxisTicks(5, 5, 0, 100, 5)).toEqual([]);
    expect(timelineAxisTicks(0, 10, 50, 50, 5)).toEqual([]);
    expect(timelineAxisTicks(0, 10, 0, 100, 1)).toEqual([]);
  });
});

describe('zoomTowardPivot', () => {
  it('shrinks the window by the factor while keeping the pivot fixed in place', () => {
    const { from, to } = zoomTowardPivot(2000, 0, 4000, 0.8);
    expect(to - from).toBeCloseTo(3200, 5);          // 4000 * 0.8
    // pivot keeps its relative position (centered here → stays centered)
    expect((2000 - from) / (to - from)).toBeCloseTo(0.5, 5);
  });

  it('keeps an off-center pivot at the same fraction', () => {
    const before = (3000 - 0) / (4000 - 0);          // 0.75
    const { from, to } = zoomTowardPivot(3000, 0, 4000, 0.5);
    expect((3000 - from) / (to - from)).toBeCloseTo(before, 5);
  });
});

describe('formatBucketInstant', () => {
  const t = Date.UTC(2026, 5, 9, 14, 30);
  // Asserted against the locale's own output rather than a literal, so this holds in any locale.
  const clock = new Date(t).toLocaleTimeString();

  it('keeps the clock alone inside a single day', () => {
    expect(formatBucketInstant(t, 3_600_000)).toBe(clock);
  });
  it('prefixes the date once the window spans more than a day', () => {
    // A bare "14:30" cannot say which of seven afternoons it is.
    const out = formatBucketInstant(t, 7 * 86_400_000);
    expect(out.endsWith(clock)).toBe(true);
    expect(out.length).toBeGreaterThan(clock.length);
  });
});

describe('formatAxisTime', () => {
  const t = Date.UTC(2026, 5, 9, 14, 30);
  it('shows a clock time for intraday spans', () => {
    expect(formatAxisTime(t, 3_600_000)).toMatch(/:/);
  });
  it('drops the clock and shows a date for multi-week spans', () => {
    expect(formatAxisTime(t, 30 * 86_400_000)).not.toMatch(/:/);
  });
});
