import { useCallback, useMemo, useState } from 'react';
import { useParams } from 'react-router-dom';
import { Trans, useLingui } from '@lingui/react/macro';
import { Pagination } from '../../../components/ui/Pagination';
import { Skeleton, SkeletonList } from '../../../components/ui/Skeleton';
import { EmptyState } from '../../../components/ui/EmptyState';
import { TraceDetailPanel as TraceDetail } from '../../../components/trace-detail/TraceDetailPanel';
import { useSelectedTrace } from '../../../hooks/useSelectedTrace';
import { useNowTick } from '../../../hooks/useNowTick';
import { flatRows, DEFAULT_TRACE_SORT } from '../tracesMeta';
import type { TraceRow, TraceSort, TraceSortField } from '../tracesMeta';
import { TraceTable } from '../components/TraceTable';
import { SessionHeader } from './components/SessionHeader';
import { useSessionDetail } from './hooks/useSessionDetail';
import { useSessionTraces } from './hooks/useSessionTraces';
import { useSessionLiveStream } from './hooks/useSessionLiveStream';
import { isSessionLive, SESSION_TRACES_PAGE_SIZE } from './sessionMeta';

// Grouping is disabled on the session timeline (traces render flat and chronological), so there is
// no conversation expand/collapse state — this stays empty and is never mutated.
const NO_EXPANDED: Set<string> = new Set();
// The session timeline is oldest-first so live arrivals append at the bottom (DESIGN §8).
// Known limit: past SESSION_TRACES_PAGE_SIZE traces, a live arrival lands on the *last* page while
// the viewer sits on page 1 — the header counters climb but no row appears until they page forward.
// Accepted for now because sessions are bounded (one app run); revisit if long sessions become common.
const SESSION_DEFAULT_SORT = { field: DEFAULT_TRACE_SORT.field, desc: false } as const;

export default function SessionView() {
  const { t } = useLingui();
  const { sessionId = null } = useParams<{ sessionId: string }>();
  const [page, setPage] = useState(1);
  const [sort, setSort] = useState<TraceSort>(SESSION_DEFAULT_SORT);

  const { session, isLoading, isError } = useSessionDetail(sessionId);
  const { traces, total, isFetching } = useSessionTraces(
    sessionId,
    session?.projectId ?? null,
    page,
    SESSION_TRACES_PAGE_SIZE,
    sort,
  );
  useSessionLiveStream(sessionId);

  // Re-evaluate the "live" window on a timer, not only on an unrelated re-render.
  const now = useNowTick(30_000);
  const live = isSessionLive(session?.lastActivityAt, now);

  const rows = useMemo<TraceRow[]>(() => flatRows(traces), [traces]);
  const [selectedTrace, selectTrace] = useSelectedTrace();
  const selectedIdx = selectedTrace ? traces.findIndex(t => t.id === selectedTrace.id) : -1;

  const handleSortChange = useCallback((field: TraceSortField) => {
    setSort(prev => (prev.field === field ? { field, desc: !prev.desc } : { field, desc: true }));
    setPage(1);
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
        rows={rows}
        isFetching={isFetching}
        // The sessionId scope is always active — an empty list reads as "no matching traces",
        // never the first-run setup prompt.
        filtered
        selectedId={selectedTrace?.id ?? null}
        expandedConvs={NO_EXPANDED}
        sort={sort}
        onSortChange={handleSortChange}
        onSelectTrace={trace => selectTrace(trace.id)}
        onToggleConv={() => {}}
      />

      {total > SESSION_TRACES_PAGE_SIZE && (
        <div data-testid="session-pagination" className="flex items-center justify-between gap-3 shrink-0">
          <Pagination page={page} total={total} pageSize={SESSION_TRACES_PAGE_SIZE} onChange={setPage} />
          <span className="text-caption text-muted whitespace-nowrap">
            <Trans>{total.toLocaleString()} total</Trans>
          </span>
        </div>
      )}

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
