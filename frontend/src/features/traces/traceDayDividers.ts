import type { TraceRow } from './tracesMeta';

/**
 * One entry in the virtualized trace list: either a trace row or a day divider standing between
 * two of them. Dividers are list entries rather than decoration inside a row so the virtualizer
 * measures and positions them like anything else.
 */
export type TraceListRow =
  | { kind: 'divider'; dayKey: string; timestamp: string }
  | { kind: 'row'; row: TraceRow };

/**
 * Stable identity for a list entry, independent of where it currently sits.
 *
 * Load-bearing twice over: it is the React reconciliation key *and* the virtualizer's `getItemKey`,
 * which is what its measured-height cache is keyed by. Position cannot serve here — a live arrival
 * splices rows into the head, so every index below it shifts, and a position-keyed height cache
 * hands an expanded group's measurement to whatever row inherits its old index.
 */
export function listRowKey(item: TraceListRow): string {
  if (item.kind === 'divider') return `divider-${item.dayKey}`;
  return item.row.type === 'flat' ? item.row.trace.id : `conv-${item.row.conversationId}`;
}

/**
 * The instant a row sits at. A conversation group is dated by its first turn — the same turn whose
 * relative time the collapsed group row displays — so the divider lands where the reader sees the
 * day change, rather than inside a group.
 */
export function rowTimestamp(row: TraceRow): string {
  return row.type === 'flat' ? row.trace.createdAt : row.turns[0].createdAt;
}

/**
 * Local-time day bucket. Local rather than UTC because the reader's "yesterday" is their own, not
 * the server's — a divider that flips at 01:00 local time would be wrong for the person reading it.
 */
function dayKey(iso: string): string {
  const d = new Date(iso);
  return `${d.getFullYear()}-${d.getMonth()}-${d.getDate()}`;
}

/**
 * Interleaves day dividers into a time-ordered row list. A divider precedes the first row of each
 * new day, but never the very first row: the column header already sits directly above it, and a
 * divider wedged between the two reads as a stray rule rather than a boundary.
 *
 * `enabled` is false under a non-time sort, where consecutive rows have no temporal relationship —
 * a divider there would assert an ordering the list does not actually have. Callers also disable it
 * when the loaded rows span a single day, since every divider would say the same thing.
 */
export function withDayDividers(rows: TraceRow[], enabled: boolean): TraceListRow[] {
  if (!enabled) {
    return rows.map(row => ({ kind: 'row', row }));
  }

  const out: TraceListRow[] = [];
  let previousDay: string | null = null;

  for (const row of rows) {
    const timestamp = rowTimestamp(row);
    const key = dayKey(timestamp);

    if (previousDay !== null && key !== previousDay) {
      out.push({ kind: 'divider', dayKey: key, timestamp });
    }

    previousDay = key;
    out.push({ kind: 'row', row });
  }

  return out;
}

/** True when the loaded rows straddle more than one local day — the precondition for dividers. */
export function spansMultipleDays(rows: TraceRow[]): boolean {
  if (rows.length < 2) {
    return false;
  }

  const first = dayKey(rowTimestamp(rows[0]));
  return rows.some(row => dayKey(rowTimestamp(row)) !== first);
}
