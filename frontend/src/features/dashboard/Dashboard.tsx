import { useMemo } from 'react';
import { Trans, useLingui } from '@lingui/react/macro';
import { useQueryClient } from '@tanstack/react-query';
import { useTraceStream } from '../../api/event-stream';
import { QUERY_KEYS } from '../../api/query-keys';
import type { AgentListItemDto } from '../../api/models';
import useCurrentProject from '../../hooks/useCurrentProject';
import { bucketFor, rangeFromOpt, RANGE_KEYS, type RangeKey } from '../../lib/time-range';
import { useLocalStorageState } from '../../hooks/useLocalStorageState';
import { cn } from '../../lib/cn';
import {
  computeLatencyStats,
  computeTokenSeries,
  computeModelSplit,
  computeEndpointLatency,
  computeAgentFleet,
} from './dashboardMeta';
import { useDashboardView } from './hooks/useDashboardQueries';
import { useLiveClock } from './hooks/useLiveClock';
import { useFreshTraces } from './hooks/useFreshTraces';
import { usePulse } from './hooks/usePulse';
import { useDashboardScope } from './hooks/useDashboardScope';
import { useDraftProposalCount } from '../../hooks/useProposals';
import { PulseBand } from './components/PulseBand';
import { HeroTokenCard } from './components/HeroTokenCard';
import { StatTileGrid } from './components/StatTileGrid';
import { LiveTraceStream } from './components/LiveTraceStream';
import { PassRateGauge } from './components/PassRateGauge';
import { AgentFleetSection } from './components/AgentFleetSection';
import { LatencySection } from './components/LatencySection';
import { DashboardScopeSelect } from './components/DashboardScopeSelect';
import { AskTraceyButton } from '../../components/tracey/AskTraceyButton';
import { projectHealthPrompt } from '../../components/tracey/askTraceyPrompts';

const NO_AGENTS: AgentListItemDto[] = [];

export default function Dashboard() {
  const { t } = useLingui();
  const qc = useQueryClient();
  // Range selector persists across refresh / navigation. Defaults to the all-time bucket
  // until the user picks a window.
  // eslint-disable-next-line lingui/no-unlocalized-strings -- RangeKey enum token, not UI copy
  const [storedRange, setRange] = useLocalStorageState<RangeKey>('dashboard.range', 'all');
  // Guard against a stale/garbage stored value — only accept a known key.
  // eslint-disable-next-line lingui/no-unlocalized-strings -- RangeKey enum token, not UI copy
  const range = RANGE_KEYS.includes(storedRange) ? storedRange : 'all';
  // Memoize so `from` is stable across renders; recomputing `new Date()` each
  // render would churn every queryKey below and cause an infinite refetch loop.
  // `undefined` for the all-time bucket (no `from` filter).
  const from = useMemo(() => rangeFromOpt(range), [range]);
  const { currentProjectId, currentProject } = useCurrentProject();
  const projectId = currentProjectId ?? undefined;
  const { scopes, scopeId, isResolved: scopeResolved, setScopeId } = useDashboardScope(projectId);
  const enabled = currentProjectId !== null && scopeResolved;

  const clock = useLiveClock();

  // ── Data hooks ──────────────────────────────────────────────────────────────

  const queryOpts = { from, projectId, scopeId, enabled };

  const { data: dashboard, isLoading: dashboardFetching } = useDashboardView(queryOpts);
  // A request held back for the remembered scope is still loading, not "no data".
  const dashboardLoading = dashboardFetching || !scopeResolved;
  const { pulse, lastBeat } = usePulse(dashboard?.pulse, projectId, scopeId);
  const proposalCount = useDraftProposalCount();

  // ── SSE: invalidate on new traces ───────────────────────────────────────────

  useTraceStream(() => {
    qc.invalidateQueries({ queryKey: QUERY_KEYS.statisticsDashboard(from, projectId, scopeId) });
  });

  // ── Derived data ────────────────────────────────────────────────────────────

  const summary = dashboard?.summary;
  const telemetry = dashboard?.liveTelemetry;
  const trends = dashboard?.trends;
  const agentBreakdown = dashboard?.agentBreakdown;
  const recentTraces = dashboard?.recentTraces ?? [];
  // Referentially stable while loading (a `?? []` literal would re-allocate every render
  // and defeat the fleet useMemo below).
  const agents = dashboard?.agents ?? NO_AGENTS;
  const freshIds = useFreshTraces(recentTraces);

  const latencyStats = computeLatencyStats(dashboard?.latency ?? []);
  const tokenBucket = dashboard?.tokenBucket ?? bucketFor(range);
  const tokenSeries = computeTokenSeries(dashboard?.tokenUsage ?? [], range, tokenBucket);
  const modelSplit = computeModelSplit(dashboard?.modelBreakdown ?? []);
  const endpointLatency = computeEndpointLatency(dashboard?.latency ?? [], agents);
  // Memoized: the page re-renders every second (live clock tick), and the fleet derivation
  // gap-fills a bucket grid per agent — the only per-render dashboard derivation heavy
  // enough to be worth caching between data refreshes.
  const tokenUsageByAgent = dashboard?.tokenUsageByAgent;
  const fleet = useMemo(
    () => computeAgentFleet(agents, agentBreakdown ?? [], tokenUsageByAgent ?? [], range, tokenBucket),
    [agents, agentBreakdown, tokenUsageByAgent, range, tokenBucket],
  );

  return (
    <div className="w-full min-w-0 flex flex-col gap-2">

      {/* Title + clock */}
      <div className="fade-up flex items-center justify-between gap-3 px-0.5">
        <div className="flex items-center gap-3.5">
          <span className="text-caption text-accent-hover font-mono tracking-[0.18em] flex items-center gap-1.5 font-semibold">
            <span className="size-1.5 rounded-full bg-success pulse-dot" />
            <Trans>LIVE</Trans>
          </span>
          <h1 className="text-h1 font-bold tracking-[-0.025em] leading-none"><Trans>Mission Control</Trans></h1>
          <p className="text-body-sm text-muted">{currentProject?.name ?? t`All projects`}</p>
        </div>
        <div className="flex items-center gap-3">
          <DashboardScopeSelect scopes={scopes} scopeId={scopeId} onChange={setScopeId} />
          <AskTraceyButton data-testid="ask-tracey-btn-dashboard" prompt={projectHealthPrompt()} />
          <div className="flex items-center gap-2.5 font-mono text-caption text-muted">
            <span className="text-primary font-semibold tracking-[0.04em] tabular-nums">{clock}</span>
            <span className="tracking-[0.14em] uppercase"><Trans>UTC · proxy</Trans></span>
          </div>
        </div>
      </div>

      {/* ① Pulse band — the 5-second hook */}
      <div className="fade-up [animation-delay:40ms]">
        <PulseBand pulse={pulse} lastBeat={lastBeat} telemetry={telemetry} />
      </div>

      {/* ② Live theater + ③ token hero */}
      <div className="fade-up grid grid-cols-1 lg:grid-cols-[minmax(0,1.45fr)_minmax(0,1.15fr)] gap-2 [animation-delay:80ms]">
        <LiveTraceStream traces={recentTraces} isLoading={dashboardLoading} freshIds={freshIds} />
        <HeroTokenCard
          summary={summary}
          tokenVolume={tokenSeries.values}
          tokenBuckets={tokenSeries.buckets}
          bucket={tokenBucket}
          modelSplit={modelSplit}
          range={range}
          onRangeChange={setRange}
          isLoading={dashboardLoading}
        />
      </div>

      {/* ④ Scale + quality band */}
      {/* Test runs carry no scope, so a scoped pass rate could only be the scope's agents' runs —
          shared agents would count in full. The gauge is hidden rather than shown misleading. */}
      <div
        className={cn(
          'fade-up grid grid-cols-1 gap-2 [animation-delay:120ms]',
          !scopeId && 'lg:grid-cols-[minmax(0,2.1fr)_minmax(0,1fr)]',
        )}
      >
        <StatTileGrid
          summary={summary}
          telemetry={telemetry}
          trends={trends}
          latencyStats={latencyStats}
        />
        {!scopeId && <PassRateGauge summary={summary} passRateTrend={trends?.passRate} />}
      </div>

      {/* ⑤ Fleet roster + latency spectrum */}
      <div className="fade-up grid grid-cols-1 lg:grid-cols-[minmax(0,1.55fr)_minmax(0,1fr)] gap-2 [animation-delay:160ms]">
        <AgentFleetSection fleet={fleet} isLoading={dashboardLoading} proposalCount={proposalCount} />
        <LatencySection rows={endpointLatency} latencyStats={latencyStats} isLoading={dashboardLoading} />
      </div>

    </div>
  );
}
