import { describe, expect, it } from 'vitest';
import { initialGateState, onReturnedToTop, onTraceArrived, SUMMARY_COALESCE_MS } from './traceStreamGate';

describe('onTraceArrived', () => {
  it('folds the arrival into the head immediately when the reader is at the top', () => {
    const result = onTraceArrived(initialGateState(), true, 1000);

    expect(result.mergeHead).toBe(true);
    expect(result.state.pending).toBe(false);
  });

  it('withholds the merge and marks pending when scrolled away from the top', () => {
    const result = onTraceArrived(initialGateState(), false, 1000);

    expect(result.mergeHead).toBe(false);
    expect(result.state.pending).toBe(true);
  });

  it('keeps pending set across further arrivals while scrolled', () => {
    const first = onTraceArrived(initialGateState(), false, 1000);
    const second = onTraceArrived(first.state, false, 2000);

    expect(second.state.pending).toBe(true);
    expect(second.mergeHead).toBe(false);
  });

  it('clears a pending flag if an arrival lands while back at the top', () => {
    const pending = onTraceArrived(initialGateState(), false, 1000).state;

    const result = onTraceArrived(pending, true, 2000);

    expect(result.mergeHead).toBe(true);
    expect(result.state.pending).toBe(false);
  });

  it('refreshes the aggregates on the first arrival', () => {
    expect(onTraceArrived(initialGateState(), false, 1000).refreshAggregates).toBe(true);
  });

  it('coalesces aggregate refreshes inside the window', () => {
    const first = onTraceArrived(initialGateState(), false, 1000);
    const second = onTraceArrived(first.state, false, 1000 + SUMMARY_COALESCE_MS - 1);

    expect(second.refreshAggregates).toBe(false);
  });

  it('refreshes the aggregates again once the window has elapsed', () => {
    const first = onTraceArrived(initialGateState(), false, 1000);
    const later = onTraceArrived(first.state, false, 1000 + SUMMARY_COALESCE_MS);

    expect(later.refreshAggregates).toBe(true);
  });

  it('coalesces a burst of arrivals down to a single aggregate refresh', () => {
    // A busy proxy can emit many events a second; the aggregates are whole-table queries, so a
    // burst must not translate into a burst of them.
    let state = initialGateState();
    let refreshes = 0;
    for (let i = 0; i < 50; i++) {
      const result = onTraceArrived(state, false, 1000 + i * 100);
      state = result.state;
      if (result.refreshAggregates) refreshes += 1;
    }

    // 50 arrivals spanning 4.9s → the first one, and nothing else inside the 5s window.
    expect(refreshes).toBe(1);
  });
});

describe('onReturnedToTop', () => {
  it('flushes a pending merge', () => {
    const pending = onTraceArrived(initialGateState(), false, 1000).state;

    const result = onReturnedToTop(pending, 5000);

    expect(result.mergeHead).toBe(true);
    expect(result.state.pending).toBe(false);
  });

  it('does nothing when nothing is pending', () => {
    const result = onReturnedToTop(initialGateState(), 5000);

    expect(result.mergeHead).toBe(false);
    expect(result.state.pending).toBe(false);
  });

  it('does not flush twice for one pending arrival', () => {
    const pending = onTraceArrived(initialGateState(), false, 1000).state;

    const first = onReturnedToTop(pending, 5000);
    const second = onReturnedToTop(first.state, 6000);

    expect(first.mergeHead).toBe(true);
    expect(second.mergeHead).toBe(false);
  });
});
