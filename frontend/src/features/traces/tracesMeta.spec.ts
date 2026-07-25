import { describe, it, expect } from 'vitest';
import type { AgentCallListItemDto } from '../../api/models';
import { buildRows, latencyBarPct, toolCount, autoTimeRange, hasActiveTraceFilters, traceListView, EMPTY_ADVANCED_FILTERS, advancedFilterParams, isValidAdvancedFilters, GRID_TEMPLATE, COL_WIDTHS, COL_HEADERS, DEFAULT_TRACE_SORT, SORT_FIELD_BY_COL, SORT_FIELD_TO_API, isValidTraceSort } from './tracesMeta';

// ── Minimal fixture factory ───────────────────────────────────────────────────

function trace(over: Partial<AgentCallListItemDto> & Pick<AgentCallListItemDto, 'id'>): AgentCallListItemDto {
  return {
    agentId: null,
    agentName: null,
    model: 'gpt-4o',
    provider: 'openai',
    messagePreview: null,
    toolCount: 0,
    inputTokens: 10,
    outputTokens: 5,
    cachedInputTokens: 0,
    durationMs: 200,
    httpStatus: 200,
    finishReason: 'stop',
    errorMessage: null,
    costEur: null,
    createdAt: new Date().toISOString(),
    updatedAt: new Date().toISOString(),
    conversationId: null,
    sessionId: null,
    outlierFlags: 0,
    ...over,
  };
}

// ── buildRows ─────────────────────────────────────────────────────────────────

describe('buildRows', () => {
  it('returns flat rows when no conversationId', () => {
    const traces = [trace({ id: 'a' }), trace({ id: 'b' })];
    const rows = buildRows(traces);
    expect(rows).toHaveLength(2);
    expect(rows[0].type).toBe('flat');
    expect(rows[1].type).toBe('flat');
  });

  it('returns flat row when conversationId appears only once', () => {
    const traces = [trace({ id: 'a', conversationId: 'conv-1' })];
    const rows = buildRows(traces);
    expect(rows).toHaveLength(1);
    expect(rows[0].type).toBe('flat');
  });

  it('groups traces with the same conversationId appearing more than once', () => {
    const traces = [
      trace({ id: 'a', conversationId: 'conv-1' }),
      trace({ id: 'b', conversationId: 'conv-1' }),
    ];
    const rows = buildRows(traces);
    expect(rows).toHaveLength(1);
    expect(rows[0].type).toBe('conversation');
    if (rows[0].type === 'conversation') {
      expect(rows[0].conversationId).toBe('conv-1');
      expect(rows[0].turns).toHaveLength(2);
    }
  });

  it('emits the conversation group only once per conversationId', () => {
    const traces = [
      trace({ id: 'a', conversationId: 'conv-1' }),
      trace({ id: 'b', conversationId: 'conv-1' }),
      trace({ id: 'c', conversationId: 'conv-1' }),
    ];
    const rows = buildRows(traces);
    expect(rows).toHaveLength(1);
  });

  it('preserves order: flat traces before and after a group', () => {
    const traces = [
      trace({ id: 'x' }),
      trace({ id: 'a', conversationId: 'conv-1' }),
      trace({ id: 'b', conversationId: 'conv-1' }),
      trace({ id: 'y' }),
    ];
    const rows = buildRows(traces);
    expect(rows).toHaveLength(3);
    expect(rows[0].type).toBe('flat');
    expect(rows[1].type).toBe('conversation');
    expect(rows[2].type).toBe('flat');
  });

  it('handles multiple distinct conversation groups', () => {
    const traces = [
      trace({ id: 'a', conversationId: 'conv-1' }),
      trace({ id: 'b', conversationId: 'conv-1' }),
      trace({ id: 'c', conversationId: 'conv-2' }),
      trace({ id: 'd', conversationId: 'conv-2' }),
    ];
    const rows = buildRows(traces);
    expect(rows).toHaveLength(2);
    expect(rows[0].type).toBe('conversation');
    expect(rows[1].type).toBe('conversation');
    if (rows[0].type === 'conversation') expect(rows[0].conversationId).toBe('conv-1');
    if (rows[1].type === 'conversation') expect(rows[1].conversationId).toBe('conv-2');
  });

  it('returns empty array for empty input', () => {
    expect(buildRows([])).toHaveLength(0);
  });
});

// ── toolCount ─────────────────────────────────────────────────────────────────

describe('toolCount', () => {
  it('returns the precomputed response tool-request count', () => {
    expect(toolCount(trace({ id: 'a', toolCount: 2 }))).toBe(2);
  });

  it('returns 0 when the call produced no tool requests (error / empty completion)', () => {
    expect(toolCount(trace({ id: 'b', toolCount: 0 }))).toBe(0);
  });
});


// ── hasActiveTraceFilters (empty-state regression guard) ──────────────────────

describe('hasActiveTraceFilters', () => {
  const none = { search: '', timeRangeActive: false, advanced: EMPTY_ADVANCED_FILTERS };

  it('is false when no filter is applied (so a genuinely empty project shows setup instructions)', () => {
    expect(hasActiveTraceFilters(none)).toBe(false);
  });

  it('is true when a non-blank search term is entered', () => {
    expect(hasActiveTraceFilters({ ...none, search: '  hello ' })).toBe(true);
  });

  it('treats a whitespace-only search as no filter', () => {
    expect(hasActiveTraceFilters({ ...none, search: '   ' })).toBe(false);
  });

  it('is true when a time range is active', () => {
    expect(hasActiveTraceFilters({ ...none, timeRangeActive: true })).toBe(true);
  });

  // The historically regressed case: a filter that hides every row but is not counted here makes
  // a filtered-empty list wrongly show the first-time setup instructions. EVERY advanced field
  // must count.
  it('is true when any advanced filter is set', () => {
    for (const patch of [
      { agent: 'agent-1' },
      { session: 'session-1' },
      { anomaly: 'any' as const },
      { anomaly: 'highLatency' as const },
      { tool: 'web_search' },
      { model: 'gpt' },
      { statusClass: '5' as const },
      { minTokens: '100' },
      { maxTokens: '100' },
      { minLatencyMs: '50' },
      { maxLatencyMs: '50' },
    ]) {
      expect(hasActiveTraceFilters({ ...none, advanced: { ...EMPTY_ADVANCED_FILTERS, ...patch } })).toBe(true);
    }
  });
});

// ── Advanced filters → API params ──────────────────────────────────────────────

describe('advancedFilterParams', () => {
  it('maps nothing for the empty filter set', () => {
    expect(advancedFilterParams(EMPTY_ADVANCED_FILTERS)).toEqual({});
  });

  it('maps agent, tool, model and status class', () => {
    expect(advancedFilterParams({
      ...EMPTY_ADVANCED_FILTERS, agent: 'a1', tool: 'web_search', model: 'gpt', statusClass: '5',
    })).toEqual({ agentId: 'a1', toolName: 'web_search', model: 'gpt', httpStatusClass: 5 });
  });

  it('maps the session slot to the sessionId param', () => {
    expect(advancedFilterParams({ ...EMPTY_ADVANCED_FILTERS, session: 'sess-1' })).toEqual({ sessionId: 'sess-1' });
  });

  it("maps anomaly 'any' to outlierOnly and specific anomalies to their OutlierFlags bit", () => {
    expect(advancedFilterParams({ ...EMPTY_ADVANCED_FILTERS, anomaly: 'any' })).toEqual({ outlierOnly: true });
    expect(advancedFilterParams({ ...EMPTY_ADVANCED_FILTERS, anomaly: 'highTokens' })).toEqual({ anomalyFlags: 1 });
    expect(advancedFilterParams({ ...EMPTY_ADVANCED_FILTERS, anomaly: 'highLatency' })).toEqual({ anomalyFlags: 2 });
    expect(advancedFilterParams({ ...EMPTY_ADVANCED_FILTERS, anomaly: 'lowCacheHit' })).toEqual({ anomalyFlags: 4 });
    expect(advancedFilterParams({ ...EMPTY_ADVANCED_FILTERS, anomaly: 'manyToolCalls' })).toEqual({ anomalyFlags: 8 });
    expect(advancedFilterParams({ ...EMPTY_ADVANCED_FILTERS, anomaly: 'custom' })).toEqual({ anomalyFlags: 16 });
  });

  it('maps numeric ranges, ignoring blanks and non-numeric garbage', () => {
    expect(advancedFilterParams({
      ...EMPTY_ADVANCED_FILTERS, minTokens: '100', maxTokens: '5000', minLatencyMs: '250.5', maxLatencyMs: 'abc',
    })).toEqual({ minTokens: 100, maxTokens: 5000, minLatencyMs: 250.5 });
  });
});

describe('isValidAdvancedFilters', () => {
  it('accepts the empty shape and a populated shape', () => {
    expect(isValidAdvancedFilters(EMPTY_ADVANCED_FILTERS)).toBe(true);
    expect(isValidAdvancedFilters({ ...EMPTY_ADVANCED_FILTERS, anomaly: 'any', statusClass: '4' })).toBe(true);
  });

  it('rejects garbage from storage', () => {
    expect(isValidAdvancedFilters(null)).toBe(false);
    expect(isValidAdvancedFilters({})).toBe(false);
    expect(isValidAdvancedFilters({ ...EMPTY_ADVANCED_FILTERS, anomaly: 'bogus' })).toBe(false);
    expect(isValidAdvancedFilters({ ...EMPTY_ADVANCED_FILTERS, statusClass: '3' })).toBe(false);
    expect(isValidAdvancedFilters({ ...EMPTY_ADVANCED_FILTERS, minTokens: 100 })).toBe(false);
    expect(isValidAdvancedFilters({ ...EMPTY_ADVANCED_FILTERS, session: 5 })).toBe(false);
  });
});

// ── traceListView (empty-state branch regression guard) ───────────────────────

describe('traceListView', () => {
  it('renders rows whenever there are rows, regardless of fetching/filtered', () => {
    expect(traceListView(3, false, false)).toBe('rows');
    expect(traceListView(3, true, true)).toBe('rows');
  });

  it('renders the loading skeleton while fetching an empty list', () => {
    expect(traceListView(0, true, false)).toBe('loading');
    // Rows present mid-fetch still render rows, not the skeleton.
    expect(traceListView(2, true, false)).toBe('rows');
  });

  it('renders the first-time setup only for a genuinely empty, unfiltered project', () => {
    expect(traceListView(0, false, false)).toBe('empty-setup');
  });

  // The reported bug: filters (e.g. outliers-only) excluded every row and the page showed setup
  // instructions. An empty-but-filtered list must be 'empty-filtered', never 'empty-setup'.
  it('renders "no matches" (never setup) when filters exclude every row', () => {
    expect(traceListView(0, false, true)).toBe('empty-filtered');
  });
});

// ── latencyBarPct ─────────────────────────────────────────────────────────────

describe('latencyBarPct', () => {
  it('scales 0ms to 0%', () => {
    expect(latencyBarPct(0)).toBe(0);
  });

  it('caps at 100% for very high ms', () => {
    expect(latencyBarPct(100_000)).toBe(100);
  });

  it('returns 50 for 3000ms', () => {
    expect(latencyBarPct(3000)).toBe(50);
  });
});

// ── GRID_TEMPLATE / COL_WIDTHS ────────────────────────────────────────────────

describe('GRID_TEMPLATE', () => {
  it('is a string joining all COL_WIDTHS with spaces', () => {
    expect(GRID_TEMPLATE).toBe(COL_WIDTHS.join(' '));
  });
});

// ── autoTimeRange ──────────────────────────────────────────────────────────────

describe('autoTimeRange', () => {
  const now = new Date('2026-06-08T12:00:00Z').getTime();

  it('returns all-time when there is no trace', () => {
    expect(autoTimeRange(null, now)).toEqual({ kind: 'all' });
  });

  it('picks the smallest preset containing the newest trace', () => {
    expect(autoTimeRange(new Date(now - 5 * 60_000).toISOString(), now)).toEqual({ kind: 'preset', preset: '15m' });
    expect(autoTimeRange(new Date(now - 30 * 60_000).toISOString(), now)).toEqual({ kind: 'preset', preset: '1h' });
    expect(autoTimeRange(new Date(now - 3 * 3_600_000).toISOString(), now)).toEqual({ kind: 'preset', preset: '6h' });
    expect(autoTimeRange(new Date(now - 12 * 3_600_000).toISOString(), now)).toEqual({ kind: 'preset', preset: '24h' });
    expect(autoTimeRange(new Date(now - 3 * 86_400_000).toISOString(), now)).toEqual({ kind: 'preset', preset: '7d' });
    expect(autoTimeRange(new Date(now - 20 * 86_400_000).toISOString(), now)).toEqual({ kind: 'preset', preset: '30d' });
    expect(autoTimeRange(new Date(now - 90 * 86_400_000).toISOString(), now)).toEqual({ kind: 'all' });
  });
});

// ── Column sorting meta ─────────────────────────────────────────────────────────

describe('trace sort meta', () => {
  it('DEFAULT_TRACE_SORT is newest-first time order', () => {
    expect(DEFAULT_TRACE_SORT).toEqual({ field: 'time', desc: true });
  });

  it('SORT_FIELD_BY_COL is index-aligned with COL_HEADERS and marks exactly the sortable columns', () => {
    expect(SORT_FIELD_BY_COL).toHaveLength(COL_HEADERS.length);
    const byHeader = Object.fromEntries(COL_HEADERS.map((h, i) => [h, SORT_FIELD_BY_COL[i]]));
    expect(byHeader['Tools']).toBe('toolCount');
    expect(byHeader['Tokens']).toBe('tokens');
    expect(byHeader['Cached']).toBe('cacheHit');
    expect(byHeader['Latency']).toBe('latency');
    expect(byHeader['Time']).toBe('time');
    expect(byHeader['Message']).toBeNull();
    expect(byHeader['Agent']).toBeNull();
    expect(byHeader['Model']).toBeNull();
    expect(byHeader['Status']).toBeNull();
  });

  it('SORT_FIELD_TO_API covers every sort field with the backend enum name', () => {
    expect(SORT_FIELD_TO_API).toEqual({
      time: 'createdAt',
      latency: 'latency',
      tokens: 'totalTokens',
      toolCount: 'toolCount',
      cacheHit: 'cacheHitRate',
    });
  });

  it('isValidTraceSort accepts valid shapes and rejects garbage from storage', () => {
    expect(isValidTraceSort({ field: 'time', desc: true })).toBe(true);
    expect(isValidTraceSort({ field: 'latency', desc: false })).toBe(true);
    expect(isValidTraceSort({ field: 'bogus', desc: true })).toBe(false);
    expect(isValidTraceSort({ field: 'time' })).toBe(false);
    expect(isValidTraceSort(null)).toBe(false);
    expect(isValidTraceSort('time')).toBe(false);
  });
});
