import { describe, expect, it } from 'vitest';
import type { CostBudgetStatusDto } from '../../api/costs';
import { budgetMeter, parseAmount, remainingEur, sortBudgets, validateBudget } from './budgetMeter';

function budget(overrides: Partial<CostBudgetStatusDto> = {}): CostBudgetStatusDto {
  return {
    costLimitId: 'limit-1',
    agentId: null,
    agentName: null,
    softLimitEur: 80,
    hardLimitEur: 100,
    enabled: true,
    monthToDateSpendEur: 0,
    softBreached: false,
    hardBreached: false,
    ...overrides,
  };
}

describe('budgetMeter', () => {
  it('scales the fill against the hard limit', () => {
    const meter = budgetMeter(budget({ monthToDateSpendEur: 25 }));

    expect(meter.scaleEur).toBe(100);
    expect(meter.fill).toBeCloseTo(0.25);
    expect(meter.consumed).toBeCloseTo(0.25);
  });

  it('scales against the soft limit when no hard limit is set', () => {
    const meter = budgetMeter(budget({ hardLimitEur: null, softLimitEur: 50, monthToDateSpendEur: 10 }));

    expect(meter.scaleEur).toBe(50);
    expect(meter.fill).toBeCloseTo(0.2);
  });

  it('clamps the fill at full while still reporting the real overshoot', () => {
    const meter = budgetMeter(budget({ monthToDateSpendEur: 250, hardBreached: true }));

    // The bar must not overflow its track, but the number behind it stays honest.
    expect(meter.fill).toBe(1);
    expect(meter.consumed).toBeCloseTo(2.5);
  });

  it('places the soft marker as a fraction of the hard limit', () => {
    expect(budgetMeter(budget()).softMarker).toBeCloseTo(0.8);
  });

  it('has no soft marker when no soft limit is set', () => {
    expect(budgetMeter(budget({ softLimitEur: null })).softMarker).toBeNull();
  });

  it('reports the hard state from the persisted breach flag', () => {
    // Between the crossing and the next guard tick, spend and flags disagree — the flag is what
    // the proxy actually enforces, so it wins.
    const meter = budgetMeter(budget({ monthToDateSpendEur: 10, hardBreached: true }));

    expect(meter.state).toBe('hard');
  });

  it('reports the soft state from the persisted breach flag', () => {
    expect(budgetMeter(budget({ monthToDateSpendEur: 85, softBreached: true })).state).toBe('soft');
  });

  it('warns while approaching the threshold before anything has fired', () => {
    expect(budgetMeter(budget({ monthToDateSpendEur: 90 })).state).toBe('approaching');
  });

  it('is ok well below the threshold', () => {
    expect(budgetMeter(budget({ monthToDateSpendEur: 5 })).state).toBe('ok');
  });

  it('reports a disabled budget as disabled even when breached', () => {
    // A disabled limit stops blocking immediately; the meter must not imply it is still enforcing.
    expect(budgetMeter(budget({ enabled: false, hardBreached: true })).state).toBe('disabled');
  });
});

describe('remainingEur', () => {
  it('returns the headroom before calls stop', () => {
    expect(remainingEur(budget({ monthToDateSpendEur: 60 }))).toBe(40);
  });

  it('never goes negative', () => {
    expect(remainingEur(budget({ monthToDateSpendEur: 250 }))).toBe(0);
  });

  it('returns null without a hard limit', () => {
    expect(remainingEur(budget({ hardLimitEur: null }))).toBeNull();
  });
});

describe('sortBudgets', () => {
  it('puts the most urgent first, project-wide ahead of agent overrides', () => {
    const rows = [
      budget({ costLimitId: 'ok-agent', agentId: 'a1', agentName: 'Zulu' }),
      budget({ costLimitId: 'hard', hardBreached: true }),
      budget({ costLimitId: 'ok-project' }),
      budget({ costLimitId: 'soft-agent', agentId: 'a2', agentName: 'Alpha', softBreached: true }),
    ];

    expect(sortBudgets(rows).map(b => b.costLimitId)).toEqual(['hard', 'soft-agent', 'ok-project', 'ok-agent']);
  });
});

describe('validateBudget', () => {
  it('rejects a budget with no threshold at all', () => {
    expect(validateBudget(null, null)).toBe('no-threshold');
  });

  it('rejects non-positive amounts', () => {
    expect(validateBudget(0, 100)).toBe('not-positive');
    expect(validateBudget(null, -1)).toBe('not-positive');
  });

  it('rejects a soft limit above the hard limit', () => {
    // It could never fire — the hard limit blocks first.
    expect(validateBudget(200, 100)).toBe('soft-above-hard');
  });

  it('accepts equal thresholds', () => {
    expect(validateBudget(100, 100)).toBeNull();
  });

  it('accepts a single threshold', () => {
    expect(validateBudget(50, null)).toBeNull();
    expect(validateBudget(null, 50)).toBeNull();
  });
});

describe('parseAmount', () => {
  it('treats a blank field as unset', () => {
    expect(parseAmount('   ')).toEqual({ value: null, valid: true });
  });

  it('accepts a comma decimal separator', () => {
    expect(parseAmount('12,50')).toEqual({ value: 12.5, valid: true });
  });

  it('rejects a non-numeric field', () => {
    expect(parseAmount('lots')).toEqual({ value: null, valid: false });
  });
});
