import { useState, useCallback, useEffect, useMemo } from 'react';
import { Pagination } from '../../components/ui/Pagination';
import { FilterDropdown } from '../../components/ui/FilterDropdown';
import { TraceDetailPanel as TraceDetail } from '../../components/trace-detail/TraceDetailPanel';
import type { AgentCallDto } from '../../api/models';
import { buildRows, flatRows, hasActiveTraceFilters } from './tracesMeta';
import type { TraceAdvancedFilters, TraceRow, TraceSortField } from './tracesMeta';
import { useTraceAdvancedFilters } from './hooks/useTraceAdvancedFilters';
import { TraceFilterBar } from './components/TraceFilterBar';
import { TraceFilterPicker } from './components/TraceFilterPicker';
import { ALL_TIME, resolveRange, nowMs, type TimeRange } from '../../lib/timeRange';
import { PAGE_SIZE, PAGE_SIZE_OPTIONS } from './hooks/useTraceQueries';
import { useTraceQueries } from './hooks/useTraceQueries';
import { useLocalStorageState } from '../../hooks/useLocalStorageState';
import { useTraceFilters } from './hooks/useTraceFilters';
import { useFocusTrace } from './hooks/useFocusTrace';
import { useSelectedTrace } from '../../hooks/useSelectedTrace';
import { useScrollToTrace } from './hooks/useScrollToTrace';
import { useTraceSseStream } from './hooks/useTraceSseStream';
import { TraceToolbar } from './components/TraceToolbar';
import { TraceSummary } from './components/TraceSummary';
import { summarizeTraces } from './traceSummary';
import { TraceTable } from './components/TraceTable';
import { TraceTimeline } from '../../components/charts/TraceTimeline';
import { useTraceHistogram } from './hooks/useTraceHistogram';
import { useAutoDefaultRange } from './hooks/useAutoDefaultRange';
import useCurrentProject from '../../hooks/useCurrentProject';
import { useDebounce } from '../../hooks/useDebounce';
import { Trans, useLingui } from '@lingui/react/macro';

export default function Traces() {
  const { t } = useLingui();
  const { currentProjectId } = useCurrentProject();
  const [page, setPage] = useState(1);
  const [storedPageSize, setStoredPageSize] = useLocalStorageState<number>('traces.pageSize', PAGE_SIZE);
  // Guard against a stale/garbage stored value — only accept a known option.
  const pageSize = (PAGE_SIZE_OPTIONS as readonly number[]).includes(storedPageSize) ? storedPageSize : PAGE_SIZE;
  // Toolbar state persists across refresh / navigation; the composable filter-bar state is
  // project-scoped and owned by its own hook.
  const { timeRange, setTimeRange, search, setSearch, showSystem, setShowSystem, sort, setSort, rangeWasRestored } =
    useTraceFilters();
  const { filters: advanced, setFilters: setAdvanced, clearAll: clearAdvanced } = useTraceAdvancedFilters(currentProjectId);
  // Previous windows pushed by each zoom-in; double-clicking the timeline pops one.
  const [zoomStack, setZoomStack] = useState<TimeRange[]>([]);
  const [expandedConvs, setExpandedConvs] = useState<Set<string>>(new Set());
  const [pendingScrollId, setPendingScrollId] = useState<string | null>(null);

  const debouncedSearch = useDebounce(search, 200);

  // Single source of truth for the window: presets resolve to `from`..now, absolute ranges
  // carry both ends. Memoized on `timeRange` so `from`/`to` stay stable across renders
  // (recomputing relative presets every render would churn the query keys).
  const resolved = useMemo(() => resolveRange(timeRange), [timeRange]);
  const { from, to } = resolved;
  // Concrete window for the timeline: fall back to the earliest bucket / now when open-ended.
  const windowFrom = useMemo(() => (from ? new Date(from).getTime() : null), [from]);
  // eslint-disable-next-line react-hooks/exhaustive-deps -- re-anchored to "now" only when the range changes
  const windowTo = useMemo(() => (to ? new Date(to).getTime() : nowMs()), [to, from]);

  // On first load, auto-pick the smallest preset that still contains data — but only when the
  // user has no saved window, so a restored range is never clobbered.
  useAutoDefaultRange(currentProjectId !== null && !rangeWasRestored, currentProjectId ?? undefined, setTimeRange);

  const { traces, total, isFetching, allAgents, agentBreakdown } = useTraceQueries({
    page,
    pageSize,
    advanced,
    debouncedSearch,
    showSystem,
    from,
    to,
    sort,
  });

  // Histogram spans the active window and respects every filter; brushing it zooms the window.
  const { buckets } = useTraceHistogram({ from, to, advanced, debouncedSearch, showSystem });

  // Only surface agents that actually have traces in the current range — an agent with a
  // zero count is noise on the Traces tab (it has nothing to show).
  const callCounts = useMemo(
    () => new Map(agentBreakdown.map(b => [b.agentId, b.callCount])),
    [agentBreakdown],
  );
  const visibleAgents = showSystem ? allAgents : allAgents.filter(a => !a.isSystemAgent);
  const agents = visibleAgents.filter(a => (callCounts.get(a.id) ?? 0) > 0);

  // Heal a restored filter that points at a system agent while system traces are hidden (the
  // combo could persist before the toggle cleared it) — the chip would show the raw id.
  useEffect(() => {
    if (!showSystem && advanced.agent && allAgents.some(a => a.id === advanced.agent && a.isSystemAgent)) {
      setAdvanced({ agent: '' });
    }
  }, [showSystem, advanced.agent, allAgents, setAdvanced]);

  // Conversation grouping only makes sense in time order — under a metric sort, grouping
  // consecutive rows by conversation would silently reorder them, so every trace stays flat.
  const rows = useMemo<TraceRow[]>(
    () => (sort.field === 'time' ? buildRows(traces) : flatRows(traces)),
    [traces, sort.field],
  );
  // At-a-glance aggregate of the current page slice (recomputes as page/filter/range changes).
  const summary = useMemo(() => summarizeTraces(traces), [traces]);

  // Flat list of all individual traces for prev/next navigation in the drawer
  const flatTraces = rows.flatMap(r => r.type === 'flat' ? [r.trace] : r.turns);
  // Open trace lives in the URL (?trace=) so it survives refresh / is shareable. The detail panel
  // always fetches the full trace by id (the list rows are light).
  const [selectedTrace, selectTrace] = useSelectedTrace();
  const selectedIdx = selectedTrace ? flatTraces.findIndex(t => t.id === selectedTrace.id) : -1;

  const handleExpandConversation = useCallback((conversationId: string) => {
    setExpandedConvs(prev => {
      const next = new Set(prev);
      next.add(conversationId);
      return next;
    });
  }, []);

  const handleFocusTrace = useCallback((trace: AgentCallDto) => {
    // Select and consume the ?focus= deep-link in ONE URL update: a separate delete would read
    // the pre-selection params and clobber ?trace=, leaving the drawer closed (see useSelectedId).
    // eslint-disable-next-line lingui/no-unlocalized-strings -- query-string param key
    selectTrace(trace.id, ['focus']);
    setPendingScrollId(trace.id);
    setTimeRange(ALL_TIME);
    setZoomStack([]);
    clearAdvanced();
    setSearch('');
    setShowSystem(true);
    setPage(1);
  }, [selectTrace, setTimeRange, clearAdvanced, setSearch, setShowSystem]);

  useFocusTrace({
    onTrace: handleFocusTrace,
    onExpandConversation: handleExpandConversation,
  });

  useScrollToTrace(
    pendingScrollId,
    useCallback(() => setPendingScrollId(null), []),
    rows,
    expandedConvs,
  );

  useTraceSseStream();

  function toggleConv(id: string) {
    setExpandedConvs(prev => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  }

  function handleAdvancedChange(patch: Partial<TraceAdvancedFilters>) {
    setAdvanced(patch);
    setPage(1);
  }

  function handleClearAdvanced() {
    clearAdvanced();
    // The system-traces view toggle now reads as a filter chip, so "Clear all" drops it too.
    setShowSystem(false);
    setPage(1);
  }

  // Picking from the time-range picker is a fresh context — drop any zoom history.
  function handleTimeRangeChange(range: TimeRange) {
    setTimeRange(range);
    setZoomStack([]);
    setPage(1);
  }

  // Drag-select on the timeline: remember the current window, then zoom into the selection.
  function handleZoom(range: { from: number; to: number }) {
    setZoomStack(s => [...s, timeRange]);
    setTimeRange({ kind: 'absolute', from: new Date(range.from).toISOString(), to: new Date(range.to).toISOString() });
    setPage(1);
  }

  // Double-click the timeline: step back one zoom level.
  function handleZoomOut() {
    if (zoomStack.length === 0) return;
    setTimeRange(zoomStack[zoomStack.length - 1]);
    setZoomStack(zoomStack.slice(0, -1));
    setPage(1);
  }

  function handleSearchChange(v: string) {
    setSearch(v);
    setPage(1);
  }

  function handleShowSystemChange(v: boolean) {
    // Hiding system traces removes system agents from the filter options — drop a now-orphaned
    // selection so the chip doesn't fall back to rendering the raw agent id.
    if (!v && advanced.agent && allAgents.some(a => a.id === advanced.agent && a.isSystemAgent)) {
      setAdvanced({ agent: '' });
    }
    setShowSystem(v);
    setPage(1);
  }

  function handlePageSizeChange(v: number) {
    setStoredPageSize(v);
    setPage(1);
  }

  // A new column sorts descending (the "big values first" read a metric column implies);
  // clicking the active column toggles direction.
  function handleSortChange(field: TraceSortField) {
    setSort(sort.field === field ? { field, desc: !sort.desc } : { field, desc: true });
    setPage(1);
  }

  return (
    // md+: fixed-height column, the table scrolls internally. Below md the toolbar/KPIs leave the
    // table only a sliver, so the page scrolls naturally instead and the table takes its content height.
    <div className="w-full min-w-0 md:h-full md:min-h-0 flex flex-col gap-3.5">
      <TraceToolbar
        search={search}
        timeRange={timeRange}
        onSearchChange={handleSearchChange}
        onTimeRangeChange={handleTimeRangeChange}
        trailing={
          <TraceFilterPicker
            agents={agents}
            filters={advanced}
            onChange={handleAdvancedChange}
            showSystem={showSystem}
            onShowSystemChange={handleShowSystemChange}
          />
        }
      />

      <TraceFilterBar
        agents={agents}
        filters={advanced}
        onChange={handleAdvancedChange}
        onClearAll={handleClearAdvanced}
        showSystem={showSystem}
        onShowSystemChange={handleShowSystemChange}
      />

      {/* Keep the timeline mounted whenever the window is concrete — even if it holds no traces
          (e.g. after zooming into an empty slice) — so the user can always scroll/zoom back out. */}
      {(windowFrom !== null || buckets.length > 0) && (
        <TraceTimeline
          buckets={buckets}
          from={windowFrom ?? new Date(buckets[0].start).getTime()}
          to={windowTo}
          onZoom={handleZoom}
          onZoomOut={handleZoomOut}
          canZoomOut={zoomStack.length > 0}
        />
      )}

      <TraceSummary stats={summary} />

      <TraceTable
        rows={rows}
        isFetching={isFetching}
        filtered={hasActiveTraceFilters({ search: debouncedSearch, timeRangeActive: from != null, advanced })}
        selectedId={selectedTrace?.id ?? null}
        expandedConvs={expandedConvs}
        sort={sort}
        onSortChange={handleSortChange}
        onSelectTrace={t => selectTrace(t.id)}
        onToggleConv={toggleConv}
      />

      {total > 0 && (
        <div data-testid="trace-pagination" className="flex items-center justify-between gap-3 shrink-0">
          <FilterDropdown
            label={t`Per page:`}
            value={String(pageSize)}
            active
            direction="up"
            options={PAGE_SIZE_OPTIONS.map(n => ({ key: String(n), label: String(n) }))}
            onChange={key => handlePageSizeChange(Number(key))}
            width={110}
          />
          <Pagination page={page} total={total} pageSize={pageSize} onChange={setPage} />
          <span className="text-caption text-muted whitespace-nowrap"><Trans>{total.toLocaleString()} total</Trans></span>
        </div>
      )}

      {selectedTrace && (
        <TraceDetail
          trace={selectedTrace}
          onClose={() => selectTrace(null)}
          onPrev={selectedIdx > 0 ? () => selectTrace(flatTraces[selectedIdx - 1].id) : undefined}
          onNext={selectedIdx < flatTraces.length - 1 ? () => selectTrace(flatTraces[selectedIdx + 1].id) : undefined}
        />
      )}
    </div>
  );
}
