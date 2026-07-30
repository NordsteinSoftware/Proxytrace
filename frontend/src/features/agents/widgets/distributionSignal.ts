import type { MetricDistributionDto } from '../../../api/models';

/**
 * Whether a distribution metric is worth its own card. True only when it has samples *and* its mean
 * doesn't render as the metric's own zero — so an agent that never caches ("0%") or never calls a
 * tool ("0.0 ± 0.0") drops out, while a tiny-but-real value like cost's "<€0.01" stays. Using the
 * metric's own `fmt` keeps the test aligned with what the card would actually display.
 */
export function hasDistributionSignal(
  dist: MetricDistributionDto,
  fmt: (v: number) => string,
): boolean {
  return dist.sampleCount > 0 && fmt(dist.mean) !== fmt(0);
}
