import { describe, expect, it } from 'vitest';
import type { CostLimitDto } from '../../api/costs';
import { dropBudget, upsertBudget, type BudgetRow } from './budgetPatch';

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

function row(overrides: Partial<BudgetRow> = {}): BudgetRow {
  return {
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
    ...overrides,
  };
}

describe('upsertBudget — create', () => {
  it('appends the saved budget with its thresholds', () => {
    const next = upsertBudget([], limit());

    expect(next).toHaveLength(1);
    expect(next[0]).toMatchObject({
      costLimitId: 'l1',
      hardLimitEur: 100,
      softBreached: false,
      hardBreached: false,
    });
  });

  it('leaves spend unknown so the meter measures rather than claims zero', () => {
    // A fabricated 0 would read as "the full limit is still available" for a scope that may
    // already be over it; the status refetch resolves this within one cheap round trip.
    expect(upsertBudget([], limit({ agentId: 'a1' }))[0].monthToDateSpendEur).toBeNull();
  });

  it('carries the scope names through from the saved limit', () => {
    const next = upsertBudget([], limit({ agentId: 'a1', agentName: 'Data Analyst' }));

    expect(next[0]).toMatchObject({ agentId: 'a1', agentName: 'Data Analyst' });
  });

  it('does not mutate the previous list', () => {
    const prev: BudgetRow[] = [];
    upsertBudget(prev, limit());

    expect(prev).toHaveLength(0);
  });
});

describe('upsertBudget — update', () => {
  it('replaces the row in place and keeps its measured spend', () => {
    const next = upsertBudget([row()], limit({ softLimitEur: 50, hardLimitEur: 500 }));

    expect(next).toHaveLength(1);
    expect(next[0]).toMatchObject({ softLimitEur: 50, hardLimitEur: 500, monthToDateSpendEur: 33 });
  });

  it('clears the breach flags, matching what the API does on save', () => {
    // PUT deletes the limit's breach rows; without this the meter would keep reading "Blocking
    // calls" after the very edit that raised the limit.
    const next = upsertBudget([row()], limit({ hardLimitEur: 500 }));

    expect(next[0]).toMatchObject({ softBreached: false, hardBreached: false });
  });

  it('leaves other budgets alone', () => {
    const other = row({ costLimitId: 'l2', agentId: 'a1' });
    const next = upsertBudget([row(), other], limit({ hardLimitEur: 500 }));

    expect(next.map(b => b.costLimitId)).toEqual(['l1', 'l2']);
    expect(next[1]).toBe(other);
  });
});

describe('dropBudget', () => {
  it('removes only the deleted row', () => {
    const prev = [row(), row({ costLimitId: 'l2' })];

    expect(dropBudget(prev, 'l1').map(b => b.costLimitId)).toEqual(['l2']);
  });

  it('is a no-op for an unknown id', () => {
    expect(dropBudget([row()], 'nope')).toHaveLength(1);
  });
});
