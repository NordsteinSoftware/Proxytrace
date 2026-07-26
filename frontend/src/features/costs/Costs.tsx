import { useMemo, useState } from 'react';
import { Trans } from '@lingui/react/macro';
import { useAuthMode } from '../../auth/authMode';
import { useCurrentUser } from '../../auth/useCurrentUser';
import useCurrentProject from '../../hooks/useCurrentProject';
import { useFeature } from '../../hooks/useLicense';
import { useLocalStorageState } from '../../hooks/useLocalStorageState';
import { ID_SHORT_LEN } from '../../lib/constants';
import type { StatisticsBucket } from '../../lib/time-range';
import type { TimeRange } from '../../lib/timeRange';
import { useAgents } from '../agents/hooks/useAgents';
import { CostToolbar } from './components/CostToolbar';
import { CostKpiRow } from './components/CostKpiRow';
import { CostOverTimeSection } from './components/CostOverTimeSection';
import { AgentCostSection } from './components/AgentCostSection';
import { BudgetSection } from './components/BudgetSection';
import { LimitEditor } from './components/LimitEditor';
import type { LimitDraft } from './limitDraft';
import { useCostLimitMutations, useCostLimits } from './hooks/useCostLimits';
import { useCostOverview } from './hooks/useCostQueries';
import { densifyCostSeries, totalOf } from './costSeries';
import { parseAmount } from './budgetMeter';

// The page opens on the period budgets are measured over, so what the meters say and what the
// chart shows agree until the user deliberately widens the window.
const DEFAULT_RANGE: TimeRange = { kind: 'all' };

type EditorState = { mode: 'closed' } | { mode: 'create' } | { mode: 'edit'; id: string };

/**
 * Costs page: a management summary of spend development for the current project, plus the monthly
 * budgets (soft warning / hard block) that govern it. Reading is free on every tier; only changing
 * a budget is licensed.
 */
export default function Costs() {
  const [timeRange, setTimeRange] = useLocalStorageState<TimeRange>('costs.timeRange', DEFAULT_RANGE);
  // eslint-disable-next-line lingui/no-unlocalized-strings -- StatisticsBucket token, not UI copy
  const [bucket, setBucket] = useLocalStorageState<StatisticsBucket>('costs.bucket', 'daily');
  const [editor, setEditor] = useState<EditorState>({ mode: 'closed' });

  const { currentProjectId } = useCurrentProject();
  const { data: authMode } = useAuthMode();
  const currentUser = useCurrentUser();
  const isAdmin = authMode?.mode === 'local' && currentUser?.role === 'Admin';
  const licensed = useFeature('CostControls');

  const { overview, from, to, isLoading, isError } = useCostOverview(timeRange, bucket);
  const { data: limits = [], isLoading: limitsLoading } = useCostLimits();
  const { create, update, remove } = useCostLimitMutations();
  const { allAgents } = useAgents();

  const series = useMemo(
    () => densifyCostSeries(overview?.series ?? [], from, to, bucket),
    [overview?.series, from, to, bucket],
  );

  const agentName = useMemo(() => {
    const byId = new Map(allAgents.map(a => [a.id, a.name]));
    return (id: string) => byId.get(id) ?? id.slice(0, ID_SHORT_LEN);
  }, [allAgents]);

  const budgets = overview?.budgets ?? [];
  const editing = editor.mode === 'edit' ? limits.find(l => l.id === editor.id) ?? null : null;
  // A scope may hold at most one budget, so an agent that already has one is not offerable again.
  const takenAgentIds = new Set(limits.filter(l => l.agentId !== null).map(l => l.agentId));
  const availableAgents = allAgents.filter(a => !a.isSystemAgent && !takenAgentIds.has(a.id));

  function handleSave(draft: LimitDraft) {
    // The form holds text; the amounts are parsed once here, and the editor has already refused
    // anything that would not validate server-side.
    const body = {
      softLimitEur: parseAmount(draft.soft).value,
      hardLimitEur: parseAmount(draft.hard).value,
      enabled: draft.enabled,
    };
    const close = { onSuccess: () => setEditor({ mode: 'closed' }) };

    if (editing) {
      update.mutate({ id: editing.id, body }, close);
      return;
    }
    if (!currentProjectId) return;
    create.mutate({ projectId: currentProjectId, agentId: draft.agentId, ...body }, close);
  }

  return (
    <div className="w-full min-w-0 flex flex-col gap-4" data-testid="costs-page">
      <div className="flex items-start justify-between gap-3 flex-wrap">
        <div>
          <h1 className="text-h1 text-primary"><Trans>Costs</Trans></h1>
          <p className="text-body-sm text-muted">
            <Trans>Derived from captured token counts and the current endpoint prices — a price correction reprices history.</Trans>
          </p>
        </div>
        <CostToolbar
          timeRange={timeRange}
          bucket={bucket}
          onTimeRangeChange={setTimeRange}
          onBucketChange={setBucket}
        />
      </div>

      {overview?.hasUnpricedEndpoints && (
        <p className="text-body-sm text-warn" data-testid="cost-unpriced-hint">
          <Trans>
            Some calls in this window ran on a model endpoint with no configured price. They add
            nothing to the figures below, so the totals understate real spend.
          </Trans>
        </p>
      )}

      <CostKpiRow
        monthToDateEur={overview?.monthToDateSpendEur ?? 0}
        previousMonthEur={overview?.previousMonthSpendEur ?? 0}
        windowTotalEur={totalOf(series)}
        blockedCount={budgets.filter(b => b.enabled && b.hardBreached).length}
        isLoading={isLoading}
      />

      <div className="grid grid-cols-1 @4xl:grid-cols-[minmax(0,1fr)_380px] gap-4 items-start @container">
        <CostOverTimeSection
          series={series}
          bucket={bucket}
          agentName={agentName}
          isLoading={isLoading}
          isError={isError}
        />
        <div className="flex flex-col gap-4 min-w-0">
          <BudgetSection
            budgets={budgets}
            canEdit={isAdmin && licensed}
            isAdmin={isAdmin}
            isLoading={isLoading || limitsLoading}
            onCreate={() => setEditor({ mode: 'create' })}
            onEdit={id => setEditor({ mode: 'edit', id })}
          />
          <AgentCostSection
            totals={overview?.agentTotals ?? []}
            isLoading={isLoading}
            isError={isError}
          />
        </div>
      </div>

      {editor.mode !== 'closed' && (
        <LimitEditor
          key={editor.mode === 'edit' ? editor.id : 'new'}
          editing={editing}
          availableAgents={availableAgents}
          isSaving={create.isPending || update.isPending || remove.isPending}
          onSave={handleSave}
          onDelete={editing ? () => remove.mutate(editing.id, { onSuccess: () => setEditor({ mode: 'closed' }) }) : undefined}
          onClose={() => setEditor({ mode: 'closed' })}
        />
      )}
    </div>
  );
}
