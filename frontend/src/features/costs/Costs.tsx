import { useMemo, useState } from 'react';
import { Trans, useLingui } from '@lingui/react/macro';
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
import { CostOverTimeSection, type CostDimension } from './components/CostOverTimeSection';
import { AgentCostSection } from './components/AgentCostSection';
import { ApiKeyCostSection } from './components/ApiKeyCostSection';
import { BudgetSection } from './components/BudgetSection';
import { LimitEditor } from './components/LimitEditor';
import { ConfirmDialog } from '../../components/overlays/ConfirmDialog';
import { scopeIds, toLimitScope, type LimitDraft } from './limitDraft';
import { canCreateAny, scopeAvailability } from './scopeAvailability';
import { useCostLimitMutations, useCostLimits } from './hooks/useCostLimits';
import { useCostOverview } from './hooks/useCostQueries';
import { useProjectApiKeys } from './hooks/useProjectApiKeys';
import { agentPoints, apiKeyNames, apiKeyPoints, densifyCostSeries, totalOf } from './costSeries';
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
  const [deleteId, setDeleteId] = useState<string | null>(null);

  const { currentProjectId } = useCurrentProject();
  // Role alone, matching Sidebar/ProjectSelector and — crucially — the API, which gates
  // /api/cost-limits mutations on the Admin role and nothing else. An earlier `authMode === 'local'`
  // clause here also hid the editor from OIDC admins, whose requests the backend accepts perfectly
  // well: the button vanished with no explanation while the feature was available to them.
  const isAdmin = useCurrentUser()?.role === 'Admin';
  const licensed = useFeature('CostControls');

  const { overview, from, to, isLoading, isError } = useCostOverview(timeRange, bucket);
  const { data: limits = [], isLoading: limitsLoading } = useCostLimits();
  const { create, update, remove } = useCostLimitMutations();
  const { allAgents } = useAgents();
  // Admin-gated: the rows only feed the budget scope picker, and the endpoint behind them is
  // Admin-only. Asking for a member took the page down (#490); the legend below names keys from
  // the cost overview, which every member may read.
  const { apiKeys } = useProjectApiKeys(isAdmin);

  const series = useMemo(
    () => densifyCostSeries(agentPoints(overview?.series ?? []), from, to, bucket),
    [overview?.series, from, to, bucket],
  );

  const keySeries = useMemo(
    () => densifyCostSeries(apiKeyPoints(overview?.apiKeySeries ?? []), from, to, bucket),
    [overview?.apiKeySeries, from, to, bucket],
  );

  const { t } = useLingui();
  const seriesName = useMemo(() => {
    const agentById = new Map(allAgents.map(a => [a.id, a.name]));
    const keyById = apiKeyNames(overview?.apiKeyTotals ?? []);
    return (dimension: CostDimension, seriesKey: string | null) => {
      if (seriesKey === null) return t`Unattributed`;
      const byId = dimension === 'agent' ? agentById : keyById;
      return byId.get(seriesKey) ?? seriesKey.slice(0, ID_SHORT_LEN);
    };
  }, [allAgents, overview?.apiKeyTotals, t]);

  const budgets = overview?.budgets ?? [];
  const editing = editor.mode === 'edit' ? limits.find(l => l.id === editor.id) ?? null : null;
  const deleting = deleteId === null ? null : budgets.find(b => b.costLimitId === deleteId) ?? null;
  const projectScopeLabel = t`Whole project`;
  // A scope holds at most one budget — including the project-wide one, which the picker used to
  // offer unconditionally and default to, making every second budget a guaranteed 409.
  const availability = useMemo(
    () => scopeAvailability(limits, allAgents, apiKeys),
    [limits, allAgents, apiKeys],
  );

  /**
   * Opens the editor on a clean slate. Mutation state outlives the dialog — without the reset, a
   * failed save would greet the user with its stale red message the next time they open it.
   */
  function openEditor(next: EditorState) {
    create.reset();
    update.reset();
    setEditor(next);
  }

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
    const scope = toLimitScope(draft.scope);
    // Both are already guaranteed by the editor (Save is disabled without a complete scope); the
    // guards keep that a fact rather than an assumption.
    if (!currentProjectId || scope === null) return;
    create.mutate({ projectId: currentProjectId, ...scopeIds(scope), ...body }, close);
  }

  return (
    <div className="w-full min-w-0 flex flex-col gap-4" data-testid="costs-page">
      {/*
        No page heading: the sidebar already names the view, and every other page (Traces, Error
        log, Anomalies) opens straight into its toolbar. The budget CTA lives in the budgets card's
        own header — next to what it acts on, not in the page chrome.
      */}
      <CostToolbar
        timeRange={timeRange}
        bucket={bucket}
        onTimeRangeChange={setTimeRange}
        onBucketChange={setBucket}
      />

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
          byAgent={series}
          byApiKey={keySeries}
          bucket={bucket}
          nameOf={seriesName}
          isLoading={isLoading}
          isError={isError}
        />
        <div className="flex flex-col gap-4 min-w-0">
          <BudgetSection
            budgets={budgets}
            canEdit={isAdmin && licensed}
            isAdmin={isAdmin}
            isLoading={isLoading || limitsLoading}
            canCreate={canCreateAny(availability)}
            onCreate={() => openEditor({ mode: 'create' })}
            onEdit={id => openEditor({ mode: 'edit', id })}
            onDelete={setDeleteId}
          />
          <AgentCostSection
            totals={overview?.agentTotals ?? []}
            isLoading={isLoading}
            isError={isError}
          />
          <ApiKeyCostSection
            totals={overview?.apiKeyTotals ?? []}
            isLoading={isLoading}
            isError={isError}
          />
        </div>
      </div>

      {editor.mode !== 'closed' && (
        <LimitEditor
          key={editor.mode === 'edit' ? editor.id : 'new'}
          editing={editing}
          availability={availability}
          isSaving={create.isPending || update.isPending}
          // The API's refusal belongs beside the fields that caused it, not only in a toast that
          // leaves the dialog looking as if Save did nothing at all.
          saveError={(editing ? update.error : create.error)?.message ?? null}
          onSave={handleSave}
          onClose={() => setEditor({ mode: 'closed' })}
        />
      )}

      {deleting && (
        <ConfirmDialog
          title={t`Delete the budget for "${deleting.agentName ?? deleting.apiKeyName ?? projectScopeLabel}"?`}
          message={t`This removes the budget and lifts any block it is applying. Spend keeps being tracked. This cannot be undone.`}
          onConfirm={() => remove.mutate(deleting.costLimitId, { onSuccess: () => setDeleteId(null) })}
          onCancel={() => setDeleteId(null)}
          loading={remove.isPending}
        />
      )}
    </div>
  );
}
