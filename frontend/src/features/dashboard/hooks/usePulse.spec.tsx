// @vitest-environment jsdom
/**
 * Unit spec for {@link usePulse} — the composition of a render-time server reseed
 * (`syncedKey` compare), SSE-driven {@link bumpPulse} updates, and the 60s {@link shiftPulse}
 * interval. The pure helpers are covered in `dashboardMeta.spec.ts`; this spec pins the
 * interplay that is easy to break in refactors: a fresh server series replaces client state,
 * an SSE bump increments the newest bucket, the minute rollover slides the window, and a
 * refetch whose serialized series is unchanged does NOT clobber a pending bump.
 */
import { describe, it, vi, beforeEach, afterEach, expect } from 'vitest';
import { act, useEffect } from 'react';
import { createRoot, type Root } from 'react-dom/client';

// Capture the latest useTraceStream callback so the spec can fire synthetic SSE events.
let onTrace: ((e: TraceCreatedEvent) => void) | null = null;
vi.mock('../../../api/event-stream', () => ({
  useTraceStream: (cb: (e: TraceCreatedEvent) => void) => { onTrace = cb; },
}));

import { usePulse } from './usePulse';
import { PULSE_MINUTES } from '../dashboardMeta';
import type { TraceCreatedEvent } from '../../../api/models';

(globalThis as Record<string, unknown>).IS_REACT_ACT_ENVIRONMENT = true;

const traceEvent = (projectId: string): TraceCreatedEvent => ({
  id: 't1',
  agentId: 'a1',
  projectId,
  agentName: 'Agent',
  model: 'm',
  provider: 'p',
  createdAt: '2024-01-01T00:00:00Z',
  conversationId: null,
  sessionId: null,
});

// Test-harness escape hatch: the spec drives the hook imperatively, so the latest hook result is
// captured into a module-level ref after each render (a spec-only pattern).
const pulseRef: { current: ReturnType<typeof usePulse> | null } = { current: null };
function Host({ serverPulse, projectId }: { serverPulse: number[] | undefined; projectId: string | undefined }) {
  const state = usePulse(serverPulse, projectId);
  useEffect(() => { pulseRef.current = state; });
  return null;
}

let root: Root;
let container: HTMLDivElement;

function render(serverPulse: number[] | undefined, projectId: string | undefined) {
  act(() => { root.render(<Host serverPulse={serverPulse} projectId={projectId} />); });
}

const pulse = () => (pulseRef.current as ReturnType<typeof usePulse>).pulse;
const lastBeat = () => (pulseRef.current as ReturnType<typeof usePulse>).lastBeat;
const fireTrace = (projectId: string) => act(() => { (onTrace as (e: TraceCreatedEvent) => void)(traceEvent(projectId)); });

beforeEach(() => {
  vi.useFakeTimers();
  vi.setSystemTime(new Date('2024-01-01T12:00:00Z'));
  onTrace = null;
  pulseRef.current = null;
  container = document.createElement('div');
  document.body.appendChild(container);
  root = createRoot(container);
});

afterEach(() => {
  act(() => { root.unmount(); });
  container.remove();
  vi.useRealTimers();
});

describe('usePulse', () => {
  it('seeds from the server series on mount, normalized to PULSE_MINUTES entries', () => {
    render([1, 2, 3], 'proj1');
    expect(pulse()).toHaveLength(PULSE_MINUTES);
    expect(pulse().slice(-3)).toEqual([1, 2, 3]);
    expect(pulse()[0]).toBe(0);
    expect(lastBeat()).toBe(0);
  });

  it('starts all-zero while the server series is still loading, then seeds when it arrives', () => {
    render(undefined, 'proj1');
    expect(pulse()).toEqual(Array(PULSE_MINUTES).fill(0));

    render([5, 6], 'proj1');
    expect(pulse().slice(-2)).toEqual([5, 6]);
  });

  it('bumps the newest bucket and stamps lastBeat on a matching SSE trace', () => {
    render([1, 2, 3], 'proj1');
    fireTrace('proj1');
    expect(pulse()[PULSE_MINUTES - 1]).toBe(4);
    expect(lastBeat()).toBe(Date.now());
  });

  it('ignores SSE traces from other projects when a project is selected', () => {
    render([1, 2, 3], 'proj1');
    fireTrace('other-project');
    expect(pulse()[PULSE_MINUTES - 1]).toBe(3);
    expect(lastBeat()).toBe(0);
  });

  it('counts traces from any project when no project is selected', () => {
    render([1, 2, 3], undefined);
    fireTrace('whatever');
    expect(pulse()[PULSE_MINUTES - 1]).toBe(4);
  });

  it('shifts the window on the minute rollover', () => {
    render([1, 2, 3], 'proj1');
    act(() => { vi.advanceTimersByTime(60_000); });
    // [.., 1, 2, 3] slides left and opens an empty current bucket.
    expect(pulse().slice(-3)).toEqual([2, 3, 0]);
  });

  it('reseeds from a fresh server series, replacing client-side bumps', () => {
    render([1, 2, 3], 'proj1');
    fireTrace('proj1');
    expect(pulse()[PULSE_MINUTES - 1]).toBe(4);

    render([1, 2, 4], 'proj1'); // refetch now includes the bumped trace
    expect(pulse().slice(-3)).toEqual([1, 2, 4]);
  });

  it('does not clobber a pending bump when a refetch returns an unchanged series', () => {
    render([1, 2, 3], 'proj1');
    fireTrace('proj1');
    expect(pulse()[PULSE_MINUTES - 1]).toBe(4);

    // New array identity, same serialized content — the syncedKey compare must skip the reseed.
    render([1, 2, 3], 'proj1');
    expect(pulse()[PULSE_MINUTES - 1]).toBe(4);
  });
});
