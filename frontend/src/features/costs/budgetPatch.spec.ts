import { describe, expect, it } from 'vitest';
import type { CostLimitDto } from '../../api/costs';
import { dropBudget, isMonthToDateWindow, upsertBudget, type CostOverviewCache } from './budgetPatch';

// 2026-07-29T10:00:00Z — a Wednesday well into the month.
const NOW = Date.parse('2026-07-29T10:00:00.000Z');
const MONTH_START = '2026-07-01T00:00:00.000Z';
const MTD_RANGE = `${MONTH_START}|2026-07-29T10:59:59.999Z|hourly`;

function overview(overrides: Partial<CostOverviewCache> = {}): CostOverviewCache {
  return {
    monthToDateSpendEur: 42,
    previousMonthSpendEur: 100,
    series: [],
    agentTotals: [{ agentId: 'a1', agentName: 'Data Analyst', costEur: 12 }],
    apiKeySeries: [],
    apiKeyTotals: [{ apiKeyId: 'k1', apiKeyName: 'CI', keyPrefix: 'pt_ci', costEur: 7 }],
    budgets: [],
    hasUnpricedEndpoints: false,
    bucket: 'hourly',
    ...overrides,
  };
}

function limit(overrides: Partial<CostLimitDto> = {}): CostLimitDto {
  return {
    id: 'l1',
    projectId: 'p1',
    agentId: null,
    agentName: null,
    apiKeyId: null,
    apiKeyName: null,
    softLimitEur: null,
    hardLimitEur: 100,
    enabled: true,
    createdAt: '2026-07-29T10:00:00.000Z',
    updatedAt: '2026-07-29T10:00:00.000Z',
    ...overrides,
  };
}

describe('isMonthToDateWindow', () => {
  it('accepts a live window starting at the month boundary', () => {
    expect(isMonthToDateWindow(MTD_RANGE, NOW)).toBe(true);
  });

  it('rejects a window that starts before this month', () => {
    expect(isMonthToDateWindow(`2026-06-01T00:00:00.000Z|2026-07-29T11:00:00.000Z|daily`, NOW)).toBe(false);
  });

  it('rejects a window that stops short of now', () => {
    // Its totals cover only part of the month, so they are not month-to-date spend.
    expect(isMonthToDateWindow(`${MONTH_START}|2026-07-10T00:00:00.000Z|daily`, NOW)).toBe(false);
  });

  it('rejects a malformed key rather than guessing', () => {
    expect(isMonthToDateWindow('', NOW)).toBe(false);
    expect(isMonthToDateWindow(`${MONTH_START}|not-a-date|daily`, NOW)).toBe(false);
  });
});

describe('upsertBudget — create', () => {
  it('gives a project budget the exact month-to-date total', () => {
    // The API derives that figure from the month, not from the charted window, so it is always right.
    const next = upsertBudget(overview(), limit(), 'irrelevant|window|daily', NOW);

    expect(next.budgets).toHaveLength(1);
    expect(next.budgets[0]).toMatchObject({
      costLimitId: 'l1',
      monthToDateSpendEur: 42,
      hardLimitEur: 100,
      softBreached: false,
      hardBreached: false,
    });
  });

  it('reads an agent budget’s spend off the window totals when the window is the month', () => {
    const next = upsertBudget(overview(), limit({ agentId: 'a1', agentName: 'Data Analyst' }), MTD_RANGE, NOW);

    expect(next.budgets[0]).toMatchObject({ agentId: 'a1', agentName: 'Data Analyst', monthToDateSpendEur: 12 });
  });

  it('reads a key budget’s spend off the per-key totals', () => {
    const next = upsertBudget(overview(), limit({ apiKeyId: 'k1', apiKeyName: 'CI' }), MTD_RANGE, NOW);

    expect(next.budgets[0]).toMatchObject({ apiKeyId: 'k1', monthToDateSpendEur: 7 });
  });

  it('reports zero for a scope the month has no spend for', () => {
    const next = upsertBudget(overview(), limit({ agentId: 'brand-new' }), MTD_RANGE, NOW);

    expect(next.budgets[0].monthToDateSpendEur).toBe(0);
  });

  it('leaves an agent budget’s spend unknown when the window is not the month', () => {
    // Faking 0 here would claim the full limit is still available for a scope that may be over it.
    const next = upsertBudget(overview(), limit({ agentId: 'a1' }), '2026-05-01T00:00:00.000Z|2026-06-01T00:00:00.000Z|daily', NOW);

    expect(next.budgets[0].monthToDateSpendEur).toBeNull();
  });

  it('keeps the rest of the payload untouched', () => {
    const prev = overview();
    const next = upsertBudget(prev, limit(), MTD_RANGE, NOW);

    expect(next.agentTotals).toBe(prev.agentTotals);
    expect(next.monthToDateSpendEur).toBe(42);
    expect(prev.budgets).toHaveLength(0);
  });
});

describe('upsertBudget — update', () => {
  const existing = overview({
    budgets: [{
      costLimitId: 'l1',
      agentId: null,
      agentName: null,
      apiKeyId: null,
      apiKeyName: null,
      softLimitEur: 10,
      hardLimitEur: 20,
      enabled: true,
      monthToDateSpendEur: 33,
      softBreached: true,
      hardBreached: true,
    }],
  });

  it('replaces the row in place and keeps its measured spend', () => {
    const next = upsertBudget(existing, limit({ softLimitEur: 50, hardLimitEur: 500 }), MTD_RANGE, NOW);

    expect(next.budgets).toHaveLength(1);
    expect(next.budgets[0]).toMatchObject({ softLimitEur: 50, hardLimitEur: 500, monthToDateSpendEur: 33 });
  });

  it('clears the breach flags, matching what the API does on save', () => {
    // PUT deletes the limit's breach rows; without this the meter would keep reading "Blocking
    // calls" after the very edit that raised the limit.
    const next = upsertBudget(existing, limit({ hardLimitEur: 500 }), MTD_RANGE, NOW);

    expect(next.budgets[0]).toMatchObject({ softBreached: false, hardBreached: false });
  });
});

describe('dropBudget', () => {
  it('removes only the deleted row', () => {
    const prev = upsertBudget(
      upsertBudget(overview(), limit(), MTD_RANGE, NOW),
      limit({ id: 'l2', agentId: 'a1' }),
      MTD_RANGE,
      NOW,
    );

    expect(dropBudget(prev, 'l1').budgets.map(b => b.costLimitId)).toEqual(['l2']);
  });

  it('is a no-op for an unknown id', () => {
    const prev = upsertBudget(overview(), limit(), MTD_RANGE, NOW);
    expect(dropBudget(prev, 'nope').budgets).toHaveLength(1);
  });
});
