import { describe, expect, it } from 'vitest';
import type { AgentCallListItemDto, PagedResult } from '../../api/models';
import { dedupeById, mergeHeadChunk, type TraceListCache } from './traceHeadMerge';

function trace(id: string): AgentCallListItemDto {
  return {
    id,
    agentId: 'a1',
    agentName: 'Agent',
    model: 'gpt-4o',
    provider: 'openai',
    messagePreview: null,
    toolCount: 0,
    inputTokens: 10,
    outputTokens: 20,
    cachedInputTokens: 0,
    durationMs: 120,
    httpStatus: 200,
    finishReason: 'stop',
    errorMessage: null,
    costEur: null,
    createdAt: '2026-07-26T10:00:00Z',
    updatedAt: '2026-07-26T10:00:00Z',
    conversationId: null,
    sessionId: null,
    outlierFlags: 0,
  };
}

function page(ids: string[], total = ids.length, pageNo = 1): PagedResult<AgentCallListItemDto> {
  return { items: ids.map(trace), total, page: pageNo, pageSize: 50 };
}

function cache(pages: PagedResult<AgentCallListItemDto>[]): TraceListCache {
  return { pages, pageParams: pages.map((_, i) => i + 1) };
}

const ids = (c: TraceListCache) => c.pages.map(p => p.items.map(i => i.id));

describe('mergeHeadChunk', () => {
  it('prepends a newly arrived trace without touching the rows already loaded', () => {
    const current = cache([page(['b', 'c'], 2)]);

    const result = mergeHeadChunk(current, page(['new', 'b', 'c'], 3));

    expect(result).not.toBeNull();
    expect(ids(result?.cache ?? cache([]))).toEqual([['new', 'b', 'c']]);
    expect(result?.freshIds).toEqual(['new']);
  });

  it('reports the fresh ids so the list can animate exactly the arrivals', () => {
    const current = cache([page(['b'], 1)]);

    const result = mergeHeadChunk(current, page(['n2', 'n1', 'b'], 3));

    expect(result?.freshIds).toEqual(['n2', 'n1']);
  });

  it('keeps the arrival order the server returned when several land at once', () => {
    const current = cache([page(['b', 'c'], 2)]);

    const result = mergeHeadChunk(current, page(['n1', 'n2', 'n3', 'b', 'c'], 5));

    expect(ids(result?.cache ?? cache([]))).toEqual([['n1', 'n2', 'n3', 'b', 'c']]);
  });

  it('inserts at the position the fresh chunk gives it, so a metric sort is not lied about', () => {
    // Sorted by latency: an arrival ranks third, not first. Inserting it at the top would claim an
    // order the server did not return.
    const current = cache([page(['slow', 'mid', 'fast'], 3)]);

    const result = mergeHeadChunk(current, page(['slow', 'mid', 'new', 'fast'], 4));

    expect(ids(result?.cache ?? cache([]))).toEqual([['slow', 'mid', 'new', 'fast']]);
  });

  it('leaves every already-loaded chunk beyond the first untouched', () => {
    // The rows a reader has scrolled past must not move: only the head grows.
    const current = cache([page(['b', 'c'], 5), page(['d', 'e'], 5, 2)]);

    const result = mergeHeadChunk(current, page(['new', 'b', 'c'], 6));

    expect(ids(result?.cache ?? cache([]))).toEqual([['new', 'b', 'c'], ['d', 'e']]);
  });

  it('never re-inserts a trace already loaded in a later chunk', () => {
    // Offset paging shifts rows between chunks, so the fresh head can legitimately contain a row we
    // already hold further down. Re-inserting it would duplicate a React key.
    const current = cache([page(['b', 'c'], 4), page(['d', 'e'], 4, 2)]);

    const result = mergeHeadChunk(current, page(['new', 'b', 'c', 'd'], 5));

    expect(result?.freshIds).toEqual(['new']);
    expect(ids(result?.cache ?? cache([]))).toEqual([['new', 'b', 'c'], ['d', 'e']]);
  });

  it('refreshes the total so the position readout counts the arrival', () => {
    const current = cache([page(['b'], 1)]);

    const result = mergeHeadChunk(current, page(['new', 'b'], 2));

    expect(result?.cache.pages[0].total).toBe(2);
  });

  it('patches a changed total even when nothing new arrived', () => {
    // A trace deleted elsewhere shrinks the set without adding a row; the readout must still move.
    const current = cache([page(['b', 'c'], 9)]);

    const result = mergeHeadChunk(current, page(['b', 'c'], 7));

    expect(result?.freshIds).toEqual([]);
    expect(result?.cache.pages[0].total).toBe(7);
  });

  it('returns null when the head chunk holds nothing new, so no render is spent', () => {
    const current = cache([page(['b', 'c'], 2)]);

    expect(mergeHeadChunk(current, page(['b', 'c'], 2))).toBeNull();
  });

  it('returns null when no chunk is loaded yet — the query itself will fetch', () => {
    expect(mergeHeadChunk(undefined, page(['new']))).toBeNull();
    expect(mergeHeadChunk(cache([]), page(['new']))).toBeNull();
  });

  it('does not mutate the cache it was handed', () => {
    const current = cache([page(['b', 'c'], 2)]);

    mergeHeadChunk(current, page(['new', 'b', 'c'], 3));

    expect(ids(current)).toEqual([['b', 'c']]);
  });
});

describe('dedupeById', () => {
  it('keeps the first occurrence of a repeated trace', () => {
    const rows = [trace('a'), trace('b'), trace('a'), trace('c')];

    expect(dedupeById(rows).map(r => r.id)).toEqual(['a', 'b', 'c']);
  });

  it('returns the same array reference when there is nothing to drop', () => {
    // The flatten runs on every render of a list holding thousands of rows; an untouched list must
    // not churn a new array and re-run every downstream memo.
    const rows = [trace('a'), trace('b')];

    expect(dedupeById(rows)).toBe(rows);
  });
});
