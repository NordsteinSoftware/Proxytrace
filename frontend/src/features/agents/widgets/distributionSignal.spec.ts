import { describe, expect, it } from 'vitest';
import type { MetricDistributionDto } from '../../../api/models';
import { fmtCost, fmtPct, fmtTokens } from '../../../lib/format';
import { hasDistributionSignal } from './distributionSignal';

const fmtCount = (v: number) => v.toFixed(1);
const fmtTokenStat = (v: number) => fmtTokens(Math.round(v));

/** Minimal distribution with a given mean / sample count (other fields don't affect the predicate). */
const dist = (mean: number, sampleCount = 100): MetricDistributionDto => ({
  mean,
  stdDev: 0,
  sampleCount,
  min: 0,
  max: Math.max(mean, 0),
  histogram: [],
});

describe('hasDistributionSignal', () => {
  it('drops a metric with no samples', () => {
    expect(hasDistributionSignal(dist(326, 0), fmtTokenStat)).toBe(false);
  });

  it('drops tool calls that round to "0.0" (an agent that barely calls tools)', () => {
    // 1 tool call across ~850 conversations → mean ≈ 0.0012 → fmtCount "0.0".
    expect(hasDistributionSignal(dist(0.0012), fmtCount)).toBe(false);
  });

  it('keeps tool calls with a real average', () => {
    expect(hasDistributionSignal(dist(0.3), fmtCount)).toBe(true);
  });

  it('drops a cache hit rate that renders as "0%"', () => {
    expect(hasDistributionSignal(dist(0), fmtPct)).toBe(false);
    expect(hasDistributionSignal(dist(0.003), fmtPct)).toBe(false); // 0.3% → rounds to 0%
  });

  it('keeps a cache hit rate that renders non-zero', () => {
    expect(hasDistributionSignal(dist(0.05), fmtPct)).toBe(true); // 5%
  });

  it('keeps a tiny-but-real cost shown as "<€0.01"', () => {
    expect(hasDistributionSignal(dist(0.0003), fmtCost)).toBe(true);
  });

  it('drops an exactly-zero cost', () => {
    expect(hasDistributionSignal(dist(0), fmtCost)).toBe(false);
  });

  it('keeps real token / latency magnitudes', () => {
    expect(hasDistributionSignal(dist(326), fmtTokenStat)).toBe(true);
  });
});
