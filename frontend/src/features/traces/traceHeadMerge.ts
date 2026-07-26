/**
 * Folds a freshly fetched *head chunk* of the trace list into the chunks already cached, so a live
 * arrival becomes an in-place insert rather than a reload.
 *
 * Why a fold and not a refetch: the list is an infinite query. `resetQueries` drops every loaded
 * chunk, which leaves the query with no data at all until the refetch lands — and a list with no rows
 * renders its loading skeleton, so every arrival flashed the whole table away and redrew it. Patching
 * the cache keeps the rows mounted; only the arrivals mount, which is what lets them animate in.
 *
 * Pure, so the policy is testable without a query client, an SSE stream, or a DOM.
 */
import type { AgentCallListItemDto, PagedResult } from '../../api/models';

/** The shape TanStack Query stores for an infinite query — the trace list's cache entry. */
export interface TraceListCache {
  pages: PagedResult<AgentCallListItemDto>[];
  pageParams: unknown[];
}

export interface HeadMerge {
  cache: TraceListCache;
  /** Traces the fold added, in the order the server returned them — the rows to animate. */
  freshIds: string[];
}

/**
 * Merges `head` (a just-fetched page 1, under the list's current filter *and* sort) into `current`.
 * Returns null when there is nothing to apply, so a no-op arrival costs no render.
 *
 * Each unseen trace is inserted at the index it occupies in the head chunk rather than blindly at the
 * top. Under the default time-descending sort that *is* the top; under a metric sort it is wherever
 * the server ranked it — inserting at the top there would claim an order the server never returned.
 * Rows already loaded never move relative to each other (DESIGN.md §8).
 *
 * A trace present anywhere in `current` is never re-inserted: offset paging shifts rows across chunk
 * boundaries as the table grows, so the fresh head legitimately overlaps chunks further down, and a
 * second copy would duplicate a React key.
 */
export function mergeHeadChunk(
  current: TraceListCache | undefined,
  head: PagedResult<AgentCallListItemDto>,
): HeadMerge | null {
  const first = current?.pages[0];
  // Nothing loaded yet — there is no head to graft onto, and the query is fetching its first chunk
  // anyway. Patching here would race that fetch.
  if (!current || !first) return null;

  const known = new Set(current.pages.flatMap(page => page.items.map(item => item.id)));
  const arrivals = head.items
    .map((item, index) => ({ item, index }))
    .filter(({ item }) => !known.has(item.id));

  const totalChanged = head.total !== first.total;
  if (arrivals.length === 0 && !totalChanged) return null;

  const items = [...first.items];
  // Ascending index order, so each insert shifts the ones after it and the next index still lands
  // where the head chunk put it.
  for (const { item, index } of arrivals) {
    items.splice(Math.min(index, items.length), 0, item);
  }

  return {
    cache: {
      ...current,
      // Only the head is rewritten; the chunks the reader has scrolled past keep their identity.
      pages: [{ ...first, items, total: head.total }, ...current.pages.slice(1)],
    },
    freshIds: arrivals.map(({ item }) => item.id),
  };
}

/**
 * Drops repeated traces, keeping the first occurrence.
 *
 * Required because the list pages by offset while the table grows underneath it: once arrivals have
 * been folded into the head, the next chunk the reader scrolls into starts at a shifted offset and
 * repeats rows already on screen. Without this, those render twice under a duplicate React key.
 *
 * Returns the input array untouched when there is nothing to drop, so the common case does not churn
 * a new array through every downstream memo.
 */
export function dedupeById(traces: AgentCallListItemDto[]): AgentCallListItemDto[] {
  const seen = new Set<string>();
  let duplicates = false;
  for (const trace of traces) {
    if (seen.has(trace.id)) {
      duplicates = true;
      break;
    }
    seen.add(trace.id);
  }
  if (!duplicates) return traces;

  const kept = new Set<string>();
  return traces.filter(trace => {
    if (kept.has(trace.id)) return false;
    kept.add(trace.id);
    return true;
  });
}
