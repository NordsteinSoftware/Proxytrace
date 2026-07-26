import { useCallback, useMemo, useState } from 'react';
import { useParams } from 'react-router';
import { useLingui } from '@lingui/react/macro';
import { Skeleton, SkeletonList } from '../../../components/ui/Skeleton';
import { EmptyState } from '../../../components/ui/EmptyState';
import { TraceDetailPanel as TraceDetail } from '../../../components/trace-detail/TraceDetailPanel';
import { useSelectedTrace } from '../../../hooks/useSelectedTrace';
import { useNowTick } from '../../../hooks/useNowTick';
import { flatRows, DEFAULT_TRACE_SORT } from '../tracesMeta';
import type { TraceSort, TraceSortField } from '../tracesMeta';
import { withDayDividers } from '../traceDayDividers';
import { TraceTable } from '../components/TraceTable';
import { SessionHeader } from './components/SessionHeader';
import { useSessionDetail } from './hooks/useSessionDetail';
import { useSessionTraces } from './hooks/useSessionTraces';
import { useSessionLiveStream } from './hooks/useSessionLiveStream';
import { isSessionLive } from './sessionMeta';

// Grouping is disabled on the session timeline (traces render flat and chronological), so there is
// no conversation expand/collapse state — this stays empty and is never mutated.
const NO_EXPANDED: Set<string> = new Set();
// The session timeline is oldest-first so live arrivals append at the bottom (DESIGN §8).
const SESSION_DEFAULT_SORT = { field: DEFAULT_TRACE_SORT.field, desc: false } as const;

export default function SessionView() {
  const { t } = useLingui();
  const { sessionId = null } = useParams<{ sessionId: string }>();
  const [sort, setSort] = useState<TraceSort>(SESSION_DEFAULT_SORT);

  const { session, isLoading, isError } = useSessionDetail(sessionId);
  const { traces, total, isFetching, isFetchingNextPage, hasNextPage, fetchNextPage } = useSessionTraces(
    sessionId,
    session?.projectId ?? null,
    sort,
  );
  useSessionLiveStream(sessionId);

  // Re-evaluate the "live" window on a timer, not only on an unrelated re-render.
  const now = useNowTick(30_000);
  const live = isSessionLive(session?.lastActivityAt, now);

  // Grouping and day markers are both off here: a session is one app run, rendered flat and
  // chronological, so neither would tell the reader anything they don't already know.
  const items = useMemo(() => withDayDividers(flatRows(traces), false), [traces]);
  const [selectedTrace, selectTrace] = useSelectedTrace();
  const selectedIdx = selectedTrace ? traces.findIndex(t => t.id === selectedTrace.id) : -1;

  const handleSortChange = useCallback((field: TraceSortField) => {
    setSort(prev => (prev.field === field ? { field, desc: !prev.desc } : { field, desc: true }));
  }, []);

  if (isLoading) {
    return (
      <div data-testid="session-view" className="w-full min-w-0 flex flex-col gap-3.5">
        <Skeleton height={92} className="rounded-lg" />
        <div className="bg-card rounded-lg p-3"><SkeletonList rows={8} height={36} gap={4} /></div>
      </div>
    );
  }

  if (isError || !session) {
    return (
      <div data-testid="session-view" className="w-full min-w-0">
        <div data-testid="session-empty-state">
        <EmptyState
          title={t`Session not found`}
          description={t`This session no longer exists, or the link is no longer valid.`}
        />
        </div>
      </div>
    );
  }

  return (
    <div data-testid="session-view" className="w-full min-w-0 md:h-full md:min-h-0 flex flex-col gap-3.5">
      <SessionHeader session={session} live={live} />

      <TraceTable
        items={items}
        paging={{ total, isFetching, isFetchingNextPage, hasNextPage, onLoadMore: fetchNextPage }}
        selection={{
          selectedId: selectedTrace?.id ?? null,
          expandedConvs: NO_EXPANDED,
          onSelectTrace: trace => selectTrace(trace.id),
          onToggleConv: () => {},
        }}
        // The sessionId scope is always active — an empty list reads as "no matching traces",
        // never the first-run setup prompt.
        filtered
        sort={sort}
        onSortChange={handleSortChange}
      />

      {selectedTrace && (
        <TraceDetail
          trace={selectedTrace}
          onClose={() => selectTrace(null)}
          onPrev={selectedIdx > 0 ? () => selectTrace(traces[selectedIdx - 1].id) : undefined}
          onNext={selectedIdx >= 0 && selectedIdx < traces.length - 1 ? () => selectTrace(traces[selectedIdx + 1].id) : undefined}
        />
      )}
    </div>
  );
}
