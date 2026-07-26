import { useCallback, useEffect, useMemo, useRef } from 'react';
import { Trans } from '@lingui/react/macro';
import { SkeletonList } from '../../../components/ui/Skeleton';
import type { AgentCallListItemDto } from '../../../api/models';
import { GRID_TEMPLATE, GRID_TEMPLATE_NARROW, traceListView } from '../tracesMeta';
import type { TraceRow, TraceSort, TraceSortField } from '../tracesMeta';
import type { TraceListRow } from '../traceDayDividers';
import { FlatTraceRow } from './FlatTraceRow';
import { ConversationGroupRow } from './ConversationGroupRow';
import { TracesEmptyState } from './TracesEmptyState';
import { TraceTableHeader } from './TraceTableHeader';
import { TraceDayDivider } from './TraceDayDivider';
import { TraceListFooter } from './TraceListFooter';
import { useTraceVirtualizer } from '../hooks/useTraceVirtualizer';

/** Scroll offset under which the list counts as "at the top" for live-arrival purposes. */
const AT_TOP_THRESHOLD_PX = 4;

export interface TracePagingProps {
  /** Total traces matching the filters — the denominator, not the loaded count. */
  total: number;
  isFetching: boolean;
  isFetchingNextPage: boolean;
  hasNextPage: boolean;
  onLoadMore: () => void;
}

/** The live-arrival seam: what just landed, what is being withheld, and where the reader is. */
export interface TraceLiveProps {
  /** Traces folded into the list moments ago; they flash the arrival wash once. */
  freshIds: ReadonlySet<string>;
  /** Live traces arrived while scrolled; surfaced in the header until the reader returns to the top. */
  pendingRefresh: boolean;
  /** Reports whether the list is scrolled to the top, so the page can decide when to flush. */
  onAtTopChange: (isAtTop: boolean) => void;
}

export interface TraceSelectionProps {
  selectedId: string | null;
  expandedConvs: Set<string>;
  onSelectTrace: (trace: AgentCallListItemDto) => void;
  onToggleConv: (id: string) => void;
}

/** Stand-in for a list nothing streams into: no fresh rows, nothing withheld, no scroll reporting. */
const NO_LIVE_ARRIVALS: TraceLiveProps = {
  freshIds: new Set(),
  pendingRefresh: false,
  onAtTopChange: () => {},
};

interface Props {
  /** Rows plus any interleaved day dividers, already ordered. */
  items: TraceListRow[];
  paging: TracePagingProps;
  /** Omitted by a list with no live-arrival owner — the session timeline appends its own traces. */
  live?: TraceLiveProps;
  selection: TraceSelectionProps;
  /** A narrowing filter (agent or search) is active — empty means "no match", not "no traces yet". */
  filtered: boolean;
  sort: TraceSort;
  /** Header click: a new column sorts descending; the active column toggles direction. */
  onSortChange: (field: TraceSortField) => void;
  /** Trace to bring into view (deep link); cleared via {@link onScrolledToTrace}. */
  scrollToTraceId?: string | null;
  onScrolledToTrace?: () => void;
}

/* eslint-disable lingui/no-unlocalized-strings -- React reconciliation keys, not UI copy */
function rowKey(item: TraceListRow): string {
  if (item.kind === 'divider') return `divider-${item.dayKey}`;
  return item.row.type === 'flat' ? item.row.trace.id : `conv-${item.row.conversationId}`;
}
/* eslint-enable lingui/no-unlocalized-strings */

function renderRow(row: TraceRow, selection: TraceSelectionProps, freshIds: ReadonlySet<string>) {
  if (row.type === 'flat') {
    return (
      <FlatTraceRow
        trace={row.trace}
        selected={row.trace.id === selection.selectedId}
        fresh={freshIds.has(row.trace.id)}
        onClick={() => selection.onSelectTrace(row.trace)}
      />
    );
  }
  return (
    <ConversationGroupRow
      group={row}
      expanded={selection.expandedConvs.has(row.conversationId)}
      onToggle={() => selection.onToggleConv(row.conversationId)}
      selectedId={selection.selectedId}
      freshIds={freshIds}
      onSelectTrace={selection.onSelectTrace}
    />
  );
}

/**
 * The trace list: a virtualized, continuously scrolling table. Only the visible window is in the
 * DOM, so thousands of loaded rows cost the same as a screenful, and the next chunk is fetched as
 * the reader approaches the end.
 */
export function TraceTable({
  items,
  paging,
  live,
  selection,
  filtered,
  sort,
  onSortChange,
  scrollToTraceId,
  onScrolledToTrace,
}: Props) {
  const scrollRef = useRef<HTMLDivElement>(null);
  const { total, isFetching, isFetchingNextPage, hasNextPage, onLoadMore } = paging;
  const { freshIds, pendingRefresh, onAtTopChange } = live ?? NO_LIVE_ARRIVALS;

  const { virtualizer, virtualItems } = useTraceVirtualizer(scrollRef, items, {
    hasNextPage,
    isFetchingNextPage,
    onLoadMore,
  });

  // Trace rows only — dividers are list entries but not traces, so they must not shift the readout.
  const traceIndices = useMemo(
    () => items.reduce<number[]>((acc, item, i) => {
      if (item.kind === 'row') acc.push(i);
      return acc;
    }, []),
    [items],
  );

  const visible = virtualItems.filter(v => items[v.index]?.kind === 'row');
  const first = visible.length > 0 ? traceIndices.indexOf(visible[0].index) + 1 : 0;
  const last = visible.length > 0 ? traceIndices.indexOf(visible[visible.length - 1].index) + 1 : 0;

  // Deep link: resolve the trace id to its position and scroll the virtualizer there. A plain
  // querySelector cannot find an unrendered row, which is exactly what virtualization guarantees.
  useEffect(() => {
    if (!scrollToTraceId) return;
    const index = items.findIndex(item =>
      item.kind === 'row' && (
        item.row.type === 'flat'
          ? item.row.trace.id === scrollToTraceId
          : item.row.turns.some(t => t.id === scrollToTraceId)
      ));
    // Not in a loaded chunk — leave it; the detail drawer still opens via ?trace=.
    if (index < 0) return;
    virtualizer.scrollToIndex(index, { align: 'center' });
    onScrolledToTrace?.();
  }, [scrollToTraceId, items, virtualizer, onScrolledToTrace]);

  const handleScroll = useCallback(() => {
    onAtTopChange((scrollRef.current?.scrollTop ?? 0) <= AT_TOP_THRESHOLD_PX);
  }, [onAtTopChange]);

  const view = traceListView(traceIndices.length, isFetching, filtered);

  return (
    <div
      data-testid="trace-table"
      // Virtualization needs a BOUNDED scroll container. Below md the page scrolls naturally, so
      // `flex-1` would let this grow to its content height — every row would then count as visible,
      // which permanently satisfies the load-more trigger and walks the whole result set. A fixed
      // viewport-relative height keeps it a real scroller there. (Amends DESIGN.md §4.)
      className="fade-up bg-card rounded-lg overflow-hidden flex-1 min-h-0 max-md:flex-none max-md:h-[60svh] flex flex-col shadow-[var(--shadow-card)] [animation-delay:120ms] @container"
      style={{ '--trace-grid': GRID_TEMPLATE, '--trace-grid-narrow': GRID_TEMPLATE_NARROW } as React.CSSProperties}
    >
      <div
        ref={scrollRef}
        data-testid="trace-scroll"
        onScroll={handleScroll}
        aria-rowcount={total}
        className="flex-1 min-h-0 overflow-y-auto [scrollbar-gutter:stable]"
      >
        <TraceTableHeader
          sort={sort}
          onSortChange={onSortChange}
          position={{ first, last, total, pendingRefresh }}
        />

        {/* Test id is load-bearing: a live arrival must patch the list in place, and the e2e spec
            proves that by watching for this skeleton and failing if it ever reappears. */}
        {view === 'loading' && (
          <div data-testid="trace-list-loading" className="p-3">
            <SkeletonList rows={10} height={36} gap={4} />
          </div>
        )}

        {view === 'empty-filtered' && (
          <div data-testid="traces-empty-state" className="py-12 flex flex-col items-center gap-1 text-center">
            <span className="text-secondary text-body"><Trans>No traces match your filters.</Trans></span>
            <span className="text-muted text-body-sm"><Trans>Try widening the time range, agent, or search.</Trans></span>
          </div>
        )}

        {view === 'empty-setup' && <TracesEmptyState />}

        {view === 'rows' && (
          <div className="relative w-full" style={{ height: virtualizer.getTotalSize() }}>
            {virtualItems.map(virtualItem => {
              const item = items[virtualItem.index];
              if (!item) return null;
              return (
                <div
                  key={rowKey(item)}
                  data-index={virtualItem.index}
                  ref={virtualizer.measureElement}
                  className="absolute top-0 left-0 w-full"
                  style={{ transform: `translateY(${virtualItem.start}px)` }}
                >
                  {item.kind === 'divider'
                    ? <TraceDayDivider timestamp={item.timestamp} />
                    : renderRow(item.row, selection, freshIds)}
                </div>
              );
            })}
          </div>
        )}

        {view === 'rows' && (
          <TraceListFooter
            isFetchingNextPage={isFetchingNextPage}
            hasNextPage={hasNextPage}
            hasRows={traceIndices.length > 0}
          />
        )}
      </div>
    </div>
  );
}
