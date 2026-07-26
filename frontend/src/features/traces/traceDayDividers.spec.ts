import { describe, expect, it } from 'vitest';
import { rowTimestamp, spansMultipleDays, withDayDividers } from './traceDayDividers';
import type { TraceRow } from './tracesMeta';
import type { AgentCallListItemDto } from '../../api/models';

/**
 * Builds an instant on a given **local** day and hour of July 2026, then serialises it. Dividers
 * bucket by local day, so constructing the fixtures locally keeps these tests independent of the
 * timezone the suite happens to run in — a UTC literal like `2026-07-26T23:30:00Z` lands on a
 * different local day depending on the offset.
 */
function at(day: number, hour: number): string {
  return new Date(2026, 6, day, hour).toISOString();
}

/** Only `createdAt` matters to this module — the rest of the DTO is irrelevant here. */
function trace(id: string, createdAt: string): AgentCallListItemDto {
  return { id, createdAt } as AgentCallListItemDto;
}

function flat(id: string, createdAt: string): TraceRow {
  return { type: 'flat', trace: trace(id, createdAt) };
}

function group(conversationId: string, ...createdAt: string[]): TraceRow {
  return {
    type: 'conversation',
    conversationId,
    turns: createdAt.map((ts, i) => trace(`${conversationId}-${i}`, ts)),
  };
}

describe('rowTimestamp', () => {
  it('reads a flat row from its trace', () => {
    expect(rowTimestamp(flat('a', at(26, 10)))).toBe(at(26, 10));
  });

  it('dates a conversation group by its first turn, matching how the group row renders', () => {
    const row = group('c1', at(26, 10), at(25, 9));
    expect(rowTimestamp(row)).toBe(at(26, 10));
  });
});

describe('withDayDividers', () => {
  it('returns rows untouched when disabled', () => {
    const rows = [flat('a', at(26, 10)), flat('b', at(25, 10))];

    expect(withDayDividers(rows, false)).toEqual([
      { kind: 'row', row: rows[0] },
      { kind: 'row', row: rows[1] },
    ]);
  });

  it('returns an empty list for no rows', () => {
    expect(withDayDividers([], true)).toEqual([]);
  });

  it('emits no divider when every row falls on one day', () => {
    const rows = [
      flat('a', at(26, 10)),
      flat('b', at(26, 14)),
      flat('c', at(26, 16)),
    ];

    expect(withDayDividers(rows, true).filter(r => r.kind === 'divider')).toHaveLength(0);
  });

  it('emits a divider before the first row of each new day, but never before the first row', () => {
    // Descending time order, as the default sort produces.
    const rows = [
      flat('a', at(26, 10)),
      flat('b', at(26, 1)),
      flat('c', at(25, 22)),
      flat('d', at(24, 22)),
    ];

    expect(withDayDividers(rows, true).map(r => r.kind)).toEqual([
      'row', 'row', 'divider', 'row', 'divider', 'row',
    ]);
  });

  it('carries the crossing row timestamp so the label can be locale-formatted', () => {
    const rows = [flat('a', at(26, 10)), flat('b', at(25, 22))];

    const dividers = withDayDividers(rows, true).filter(r => r.kind === 'divider');

    expect(dividers).toHaveLength(1);
    expect(new Date(dividers[0].timestamp).getTime())
      .toBe(new Date(at(25, 22)).getTime());
  });

  it('dates a conversation group by its first turn when deciding day boundaries', () => {
    // The group's later turns fall on the previous day, but the row is dated by its first turn,
    // so the boundary sits before the group, not inside it.
    const rows = [
      flat('a', at(26, 10)),
      group('c1', at(25, 23), at(25, 22)),
      flat('b', at(25, 21)),
    ];

    expect(withDayDividers(rows, true).map(r => r.kind)).toEqual([
      'row', 'divider', 'row', 'row',
    ]);
  });

  it('keeps every original row when interleaving', () => {
    const rows = [
      flat('a', at(26, 10)),
      flat('b', at(25, 10)),
      flat('c', at(24, 10)),
    ];

    const kept = withDayDividers(rows, true).filter(r => r.kind === 'row').map(r => r.row);

    expect(kept).toEqual(rows);
  });
});

describe('spansMultipleDays', () => {
  it('is false for an empty list', () => {
    expect(spansMultipleDays([])).toBe(false);
  });

  it('is false for a single row', () => {
    expect(spansMultipleDays([flat('a', at(26, 10))])).toBe(false);
  });

  it('is false when every row shares a day', () => {
    expect(spansMultipleDays([
      flat('a', at(26, 1)),
      flat('b', at(26, 23)),
    ])).toBe(false);
  });

  it('is true as soon as one row falls on a different day', () => {
    expect(spansMultipleDays([
      flat('a', at(26, 10)),
      flat('b', at(26, 9)),
      flat('c', at(25, 23)),
    ])).toBe(true);
  });
});
