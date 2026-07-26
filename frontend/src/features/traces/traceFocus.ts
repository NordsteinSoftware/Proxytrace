// Pure resolution of a `?focus=` deep link against the loaded trace chunks. No React, no I/O —
// unit-tested in traceFocus.spec.ts.
//
// The list is virtualized over an infinite query, so "scroll to this trace" can only be answered for
// rows that have actually been fetched. A link handed out by anomalies or Tracey usually points at
// an older trace, which sits past the newest chunk — the drawer opened over a list still showing
// something else entirely, losing the surrounding context that was the reason to follow the link.
// Chunks arrive in list order, so the trace is reachable by simply pulling more of them; the only
// question is when to stop.

import type { TraceListRow } from './traceDayDividers';

/**
 * How many extra chunks a focus deep link may pull in before it gives up. At 50 traces per chunk
 * this reaches ~1000 rows back, which covers the links the product hands out (a recent anomaly, a
 * trace Tracey just discussed) without letting a link to a months-old trace walk the whole table.
 */
export const MAX_FOCUS_CHUNKS = 20;

/** What the focus effect should do next. */
export type FocusStep =
  /** The row is loaded — bring it into view and consume the link. */
  | 'scroll'
  /** Not loaded yet, and there is more to load — pull the next chunk. */
  | 'fetch-more'
  /** A chunk is already in flight; the next render decides. */
  | 'wait'
  /** Out of chunks, or out of budget — stop trying. */
  | 'give-up';

export interface FocusState {
  /** Index of the trace in the loaded rows, or -1 when it is not among them. */
  index: number;
  hasNextPage: boolean;
  isFetchingNextPage: boolean;
  /** Chunks this deep link has already requested. */
  chunksFetched: number;
  maxChunks?: number;
}

/**
 * Index of the list row carrying <paramref name="traceId" /> — the flat row for it, or the
 * conversation group holding it as a turn. -1 when no loaded row does.
 */
export function findFocusIndex(items: readonly TraceListRow[], traceId: string): number {
  return items.findIndex(item =>
    item.kind === 'row' && (
      item.row.type === 'flat'
        ? item.row.trace.id === traceId
        : item.row.turns.some(turn => turn.id === traceId)
    ));
}

export function nextFocusStep({
  index,
  hasNextPage,
  isFetchingNextPage,
  chunksFetched,
  maxChunks = MAX_FOCUS_CHUNKS,
}: FocusState): FocusStep {
  if (index >= 0) return 'scroll';
  if (isFetchingNextPage) return 'wait';
  if (!hasNextPage || chunksFetched >= maxChunks) return 'give-up';
  return 'fetch-more';
}
