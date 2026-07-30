import { describe, expect, it } from 'vitest';
import type { AgentCostTotalDto, ApiKeyCostTotalDto } from '../../api/costs';
import {
  MAX_BUCKETS,
  type CostSeriesPoint,
  agentPoints,
  apiKeyPoints,
  densifyCostSeries,
  monthDelta,
  monthStartIso,
  projectMonthEnd,
  quantizedCostWindow,
  resolveCostWindow,
  toStackedCostData,
  topAgents,
  topApiKeys,
  totalOf,
} from './costSeries';

const DAY = 24 * 60 * 60 * 1000;
const AGENT_A = 'aaaaaaaa-0000-0000-0000-000000000001';
const AGENT_B = 'bbbbbbbb-0000-0000-0000-000000000002';

function point(iso: string, seriesKey: string | null, costEur: number): CostSeriesPoint {
  return { bucketStart: iso, seriesKey, costEur };
}

describe('resolveCostWindow', () => {
  const now = Date.parse('2026-07-20T12:34:56.000Z');

  it('falls back to the start of the current UTC month for an open-ended range', () => {
    const { from, to } = resolveCostWindow({ kind: 'all' }, now);

    // Budgets are measured over the calendar month, so that is what the page opens on.
    expect(from).toBe('2026-07-01T00:00:00.000Z');
    expect(to).toBe('2026-07-20T12:34:56.000Z');
  });

  it('resolves a relative preset against now', () => {
    const { from, to } = resolveCostWindow({ kind: 'preset', preset: '7d' }, now);

    expect(Date.parse(to) - Date.parse(from)).toBe(7 * DAY);
  });

  it('keeps both explicit bounds of an absolute range', () => {
    const range = { kind: 'absolute', from: '2026-06-01T00:00:00.000Z', to: '2026-06-30T00:00:00.000Z' } as const;

    expect(resolveCostWindow(range, now)).toEqual({ from: range.from, to: range.to });
  });
});

describe('quantizedCostWindow', () => {
  it('produces the same window across renders inside one bucket', () => {
    const base = Date.parse('2026-07-20T12:00:00.000Z');

    const first = quantizedCostWindow({ kind: 'preset', preset: '7d' }, 'daily', base);
    const second = quantizedCostWindow({ kind: 'preset', preset: '7d' }, 'daily', base + 60_000);

    // A raw Date.now() here would change the query key — and refetch — on every render.
    expect(second).toEqual(first);
  });
});

describe('monthStartIso', () => {
  it('returns midnight UTC on the first of the month', () => {
    expect(monthStartIso(Date.parse('2026-07-20T12:34:56.000Z'))).toBe('2026-07-01T00:00:00.000Z');
  });
});

describe('densifyCostSeries', () => {
  const from = '2026-07-01T00:00:00.000Z';
  const to = '2026-07-04T23:59:59.000Z';

  it('emits every bucket in the window, including empty ones', () => {
    const rows = [point('2026-07-01T09:00:00.000Z', AGENT_A, 2)];

    const dense = densifyCostSeries(rows, from, to, 'daily');

    // Empty days must read as gaps, not compress the timeline.
    expect(dense.buckets).toHaveLength(4);
    expect(dense.buckets.map(b => b.totalEur)).toEqual([2, 0, 0, 0]);
    expect(dense.truncated).toBe(false);
  });

  it('sums several rows landing in the same bucket for the same agent', () => {
    const rows = [
      point('2026-07-02T01:00:00.000Z', AGENT_A, 1.5),
      point('2026-07-02T18:00:00.000Z', AGENT_A, 2.5),
    ];

    const dense = densifyCostSeries(rows, from, to, 'daily');

    expect(dense.buckets[1].cells).toEqual([{ seriesKey: AGENT_A, costEur: 4 }]);
  });

  it('orders a bucket cells by spend, descending', () => {
    const rows = [
      point('2026-07-01T01:00:00.000Z', AGENT_A, 1),
      point('2026-07-01T02:00:00.000Z', AGENT_B, 9),
    ];

    const dense = densifyCostSeries(rows, from, to, 'daily');

    expect(dense.buckets[0].cells.map(c => c.seriesKey)).toEqual([AGENT_B, AGENT_A]);
  });

  it('ignores rows outside the window', () => {
    const rows = [point('2026-06-15T00:00:00.000Z', AGENT_A, 100)];

    const dense = densifyCostSeries(rows, from, to, 'daily');

    expect(totalOf(dense)).toBe(0);
  });

  it('keeps the most recent buckets when the window exceeds the cap', () => {
    const wideTo = new Date(Date.parse(from) + (MAX_BUCKETS + 50) * DAY).toISOString();

    const dense = densifyCostSeries([], from, wideTo, 'daily');

    expect(dense.buckets).toHaveLength(MAX_BUCKETS);
    expect(dense.truncated).toBe(true);
    // Truncation drops the past, not the present.
    expect(Date.parse(dense.buckets[dense.buckets.length - 1].iso)).toBeGreaterThan(Date.parse(from) + MAX_BUCKETS * DAY);
  });

  it('returns nothing for an inverted window', () => {
    expect(densifyCostSeries([], to, from, 'daily').buckets).toEqual([]);
  });
});

describe('toStackedCostData', () => {
  it('maps each agent cell to a named, colored segment', () => {
    const dense = densifyCostSeries(
      [point('2026-07-01T00:00:00.000Z', AGENT_A, 3)],
      '2026-07-01T00:00:00.000Z',
      '2026-07-01T23:00:00.000Z',
      'daily',
    );

    const data = toStackedCostData(dense, 'daily', id => (id === AGENT_A ? 'Support bot' : String(id)));

    expect(data).toHaveLength(1);
    expect(data[0].segments).toHaveLength(1);
    expect(data[0].segments[0].label).toBe('Support bot');
    expect(data[0].segments[0].value).toBe(3);
    expect(data[0].segments[0].color).toMatch(/^#/);
  });

  it('renders the unattributed series muted rather than as a palette colour', () => {
    const dense = densifyCostSeries(
      [point('2026-07-01T00:00:00.000Z', null, 3)],
      '2026-07-01T00:00:00.000Z',
      '2026-07-01T23:00:00.000Z',
      'daily',
    );

    const data = toStackedCostData(dense, 'daily', () => 'Unattributed');

    // A remainder is not a peer of the named series, and must not look like one.
    expect(data[0].segments[0].color).toBe('var(--text-muted)');
  });
});

describe('point adapters', () => {
  it('maps agent rows onto the shared series shape', () => {
    const mapped = agentPoints([{ bucketStart: '2026-07-01T00:00:00.000Z', agentId: AGENT_A, costEur: 2 }]);

    expect(mapped).toEqual<CostSeriesPoint[]>([
      { bucketStart: '2026-07-01T00:00:00.000Z', seriesKey: AGENT_A, costEur: 2 },
    ]);
  });

  it('preserves a null API key as a real series key', () => {
    const mapped = apiKeyPoints([{ bucketStart: '2026-07-01T00:00:00.000Z', apiKeyId: null, costEur: 2 }]);

    // Null is the unattributed group, not missing data — dropping it would break reconciliation
    // with the project total.
    expect(mapped[0].seriesKey).toBeNull();
  });
});

describe('projectMonthEnd', () => {
  it('extrapolates linearly from the elapsed fraction of the month', () => {
    // Half of a 31-day July has elapsed at midday on the 16th.
    const now = Date.parse('2026-07-16T12:00:00.000Z');

    const projected = projectMonthEnd(100, now);

    expect(projected).not.toBeNull();
    expect(projected).toBeCloseTo(200, 0);
  });

  it('says nothing in the first days of a month', () => {
    // At 1% elapsed the extrapolation would multiply a single expensive hour by 100.
    expect(projectMonthEnd(10, Date.parse('2026-07-01T07:00:00.000Z'))).toBeNull();
  });
});

describe('monthDelta', () => {
  it('returns the signed fraction of change', () => {
    expect(monthDelta(150, 100)).toBeCloseTo(0.5);
    expect(monthDelta(50, 100)).toBeCloseTo(-0.5);
  });

  it('returns null when the previous month had no spend', () => {
    // "Up from zero" has no meaningful percentage.
    expect(monthDelta(50, 0)).toBeNull();
  });
});

describe('topAgents', () => {
  const totals: AgentCostTotalDto[] = [
    { agentId: AGENT_A, agentName: 'A', costEur: 1 },
    { agentId: AGENT_B, agentName: 'B', costEur: 5 },
    { agentId: 'c', agentName: 'C', costEur: 3 },
  ];

  it('sorts descending and keeps everything under the cap', () => {
    const { rows, otherEur } = topAgents(totals, 5);

    expect(rows.map(r => r.agentName)).toEqual(['B', 'C', 'A']);
    expect(otherEur).toBe(0);
  });

  it('folds the tail into a single remainder', () => {
    const { rows, otherEur } = topAgents(totals, 1);

    expect(rows.map(r => r.agentName)).toEqual(['B']);
    expect(otherEur).toBe(4);
  });
});

describe('topApiKeys', () => {
  const totals: ApiKeyCostTotalDto[] = [
    { apiKeyId: 'k1', apiKeyName: 'CI', keyPrefix: 'proxytrace-aa', costEur: 3 },
    { apiKeyId: 'k2', apiKeyName: 'Prod', keyPrefix: 'proxytrace-bb', costEur: 7 },
    { apiKeyId: null, apiKeyName: null, keyPrefix: null, costEur: 99 },
  ];

  it('separates the unattributed remainder from the ranked keys', () => {
    const { rows, unattributedEur } = topApiKeys(totals, 5);

    // Unattributed is by far the largest here; it must not head the ranking as if it were a key.
    expect(rows.map(r => r.apiKeyName)).toEqual(['Prod', 'CI']);
    expect(unattributedEur).toBe(99);
  });

  it('folds the ranked tail while keeping unattributed separate', () => {
    const { rows, otherEur, unattributedEur } = topApiKeys(totals, 1);

    expect(rows.map(r => r.apiKeyName)).toEqual(['Prod']);
    expect(otherEur).toBe(3);
    expect(unattributedEur).toBe(99);
  });
});
