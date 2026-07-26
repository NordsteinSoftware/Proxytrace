import { describe, expect, it } from 'vitest';
import { findFocusIndex, nextFocusStep, MAX_FOCUS_CHUNKS } from './traceFocus';
import type { TraceListRow } from './traceDayDividers';
import type { TraceRow } from './tracesMeta';
import type { AgentCallListItemDto } from '../../api/models';

/** Only the id matters here — the rest of the DTO is irrelevant to resolving a deep link. */
function trace(id: string): AgentCallListItemDto {
  return { id } as AgentCallListItemDto;
}

function flat(id: string): TraceListRow {
  return { kind: 'row', row: { type: 'flat', trace: trace(id) } satisfies TraceRow };
}

function group(conversationId: string, ...ids: string[]): TraceListRow {
  return {
    kind: 'row',
    row: { type: 'conversation', conversationId, turns: ids.map(trace) } satisfies TraceRow,
  };
}

function divider(dayKey: string): TraceListRow {
  return { kind: 'divider', dayKey, timestamp: new Date(2026, 6, 26).toISOString() };
}

describe('findFocusIndex', () => {
  it('finds a flat row by its trace id', () => {
    expect(findFocusIndex([flat('a'), flat('b')], 'b')).toBe(1);
  });

  it('finds a trace that is a turn inside a conversation group', () => {
    // The link points at one call; the list may render its whole conversation as a single row.
    expect(findFocusIndex([flat('a'), group('c1', 'x', 'y')], 'y')).toBe(1);
  });

  it('counts dividers as positions, since the virtualizer indexes every list row', () => {
    expect(findFocusIndex([divider('d1'), flat('a')], 'a')).toBe(1);
  });

  it('returns -1 when no loaded row carries the id', () => {
    expect(findFocusIndex([flat('a'), group('c1', 'x')], 'missing')).toBe(-1);
  });
});

describe('nextFocusStep', () => {
  const base = { hasNextPage: true, isFetchingNextPage: false, chunksFetched: 0 };

  it('scrolls as soon as the row is loaded', () => {
    expect(nextFocusStep({ ...base, index: 3 })).toBe('scroll');
  });

  it('scrolls even with no budget left — being loaded is all that matters', () => {
    expect(nextFocusStep({ ...base, index: 0, chunksFetched: MAX_FOCUS_CHUNKS, hasNextPage: false }))
      .toBe('scroll');
  });

  it('pulls the next chunk when the row is not loaded yet', () => {
    // The bug in #456: this used to be a silent no-op, so an older trace was never brought into view.
    expect(nextFocusStep({ ...base, index: -1 })).toBe('fetch-more');
  });

  it('waits rather than stacking requests while a chunk is in flight', () => {
    expect(nextFocusStep({ ...base, index: -1, isFetchingNextPage: true })).toBe('wait');
  });

  it('gives up at the end of the list', () => {
    expect(nextFocusStep({ ...base, index: -1, hasNextPage: false })).toBe('give-up');
  });

  it('gives up once the per-link chunk budget is spent', () => {
    expect(nextFocusStep({ ...base, index: -1, chunksFetched: MAX_FOCUS_CHUNKS })).toBe('give-up');
    expect(nextFocusStep({ ...base, index: -1, chunksFetched: MAX_FOCUS_CHUNKS - 1 })).toBe('fetch-more');
  });

  it('honours a caller-supplied budget', () => {
    expect(nextFocusStep({ ...base, index: -1, chunksFetched: 2, maxChunks: 2 })).toBe('give-up');
  });
});
