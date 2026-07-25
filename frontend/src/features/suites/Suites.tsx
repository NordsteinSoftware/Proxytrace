import { useMemo, useState } from 'react';
import { useSearchParams } from 'react-router';
import { Trans, useLingui } from '@lingui/react/macro';
import type { EvaluatorDetailDto, TestSuiteListItemDto } from '../../api/models';
import type { FilterDropdownOption } from '../../components/ui/FilterDropdown';
import { Modal } from '../../components/overlays/Modal';
import { ConfirmDialog } from '../../components/overlays/ConfirmDialog';
import { StepWizard } from '../../components/overlays/StepWizard';
import { Button } from '../../components/ui/Button';
import { Card } from '../../components/ui/Card';
import { LIST_RAIL_COLS } from '../../components/ui/ListRail';
import { ChevronRightIcon } from '../../components/icons';
import { agentColor } from '../../lib/colors';
import { cn } from '../../lib/cn';
import { useFilter } from '../../hooks/useFilter';
import { useSelectedId } from '../../hooks/useSelectedId';
import { useIsMobile } from '../../hooks/useMediaQuery';
import { AgentStep, NameStep, TracesStep, EvaluatorsStep } from './CreateSuiteWizard';
import { RunConfirmModal } from './RunConfirmModal';
import { SuiteList } from './components/SuiteList';
import { SuiteDetail } from './SuiteDetail';
import { useSuites, useSuiteAgents, useSuiteEvaluators } from './hooks/useSuiteQueries';
import { useStartRun, useDeleteSuite, useCreateSuite } from './hooks/useSuiteMutations';
import { useSuiteFocus } from './hooks/useSuiteFocus';
import { useScrollToSelectedSuite } from './hooks/useScrollToSelectedSuite';

export default function Suites() {
  const { t } = useLingui();
  const [runSuite, setRunSuite] = useState<TestSuiteListItemDto | null>(null);
  const [runDone, setRunDone] = useState(false);
  const [deleteSuite, setDeleteSuite] = useState<TestSuiteListItemDto | null>(null);
  const [createOpen, setCreateOpen] = useState(false);
  const [createStep, setCreateStep] = useState(0);
  const [createAgentId, setCreateAgentId] = useState('');
  const [createName, setCreateName] = useState('');
  const [selectedCalls, setSelectedCalls] = useState<Set<string>>(new Set());
  const [selectedEvaluatorIds, setSelectedEvaluatorIds] = useState<Set<string>>(new Set());

  const { suites, isLoading, projectId } = useSuites();
  const { agents } = useSuiteAgents();
  const { evaluators } = useSuiteEvaluators();

  // Deep-link from the agent detail view: ?agentId pre-filters, ?suiteId scrolls + highlights.
  const [searchParams] = useSearchParams();
  // eslint-disable-next-line lingui/no-unlocalized-strings -- URL query-param key, not UI copy
  const initialAgentFilter = searchParams.get('agentId') ?? '';
  const highlightSuiteId = useSuiteFocus(!isLoading);

  // Persisted selection: ?id= keeps the chosen suite highlighted across refresh / links.
  const [selectedSuiteId, setSelectedSuiteId] = useSelectedId();
  useScrollToSelectedSuite(selectedSuiteId, !isLoading);

  const isMobile = useIsMobile();

  const startRun = useStartRun(() => setRunDone(true));
  // Clearing the selection when the selected suite is deleted keeps ?id= from dangling on a gone
  // suite (matches the Agents/Runs delete behaviour); the detail then falls back to the first suite.
  const delSuite = useDeleteSuite(() => {
    if (deleteSuite && deleteSuite.id === selectedSuiteId) setSelectedSuiteId(null);
    setDeleteSuite(null);
  });
  const createSuite = useCreateSuite(() => { setCreateOpen(false); resetCreate(); });

  const { filter: agentFilter, setFilter: setAgentFilter, filtered: visibleSuites } = useFilter(
    suites,
    (s, id: string) => !id || s.agentId === id,
    initialAgentFilter,
  );

  // Desktop selects the first suite by default; mobile lands on the list until one is chosen.
  const selectedSuite =
    visibleSuites.find(s => s.id === selectedSuiteId)
    ?? (isMobile ? null : visibleSuites[0] ?? null);

  function resetCreate() {
    setCreateStep(0);
    setCreateAgentId('');
    setCreateName('');
    setSelectedCalls(new Set());
    setSelectedEvaluatorIds(new Set());
  }

  function closeRunModal() { setRunSuite(null); setRunDone(false); }

  function toggleSelectedCall(id: string) {
    setSelectedCalls(prev => {
      const s = new Set(prev);
      if (s.has(id)) s.delete(id); else s.add(id);
      return s;
    });
  }

  function selectAllCalls(ids: string[]) {
    setSelectedCalls(prev => {
      const s = new Set(prev);
      ids.forEach(id => s.add(id));
      return s;
    });
  }

  function toggleEvaluator(id: string) {
    setSelectedEvaluatorIds(prev => {
      const s = new Set(prev);
      if (s.has(id)) s.delete(id); else s.add(id);
      return s;
    });
  }

  // Only agents that actually own a suite are offered as filter options — a flat
  // dropdown scales to many agents where wrapping tab strips did not.
  const agentFilterOptions = useMemo<FilterDropdownOption[]>(() => {
    const byAgent = new Map<string, { name: string; count: number }>();
    for (const s of suites) {
      const existing = byAgent.get(s.agentId);
      if (existing) existing.count += 1;
      else byAgent.set(s.agentId, { name: s.agentName, count: 1 });
    }
    return [
      { key: '', label: t`All agents (${suites.length})` },
      ...Array.from(byAgent, ([id, { name, count }]) => ({
        key: id,
        label: `${name} (${count})`,
        accent: agentColor(id),
      })),
    ];
  }, [suites, t]);

  const canAdvanceCreate =
    ([!!createAgentId, selectedCalls.size > 0, !!createName.trim(), true] as boolean[])[createStep] ?? false;

  const wizardSteps = [
    {
      label: t`Select agent`,
      content: <AgentStep agents={agents} value={createAgentId} onChange={setCreateAgentId} />,
    },
    {
      label: t`Select traces`,
      content: (
        <TracesStep
          agentId={createAgentId}
          selected={selectedCalls}
          onToggle={t => toggleSelectedCall(t.id)}
          onSelectAll={traces => selectAllCalls(traces.map(t => t.id))}
          onClear={() => setSelectedCalls(new Set())}
        />
      ),
    },
    {
      label: t`Name suite`,
      content: <NameStep value={createName} onChange={setCreateName} />,
    },
    {
      label: t`Select evaluators`,
      content: (
        <EvaluatorsStep
          evaluators={evaluators as EvaluatorDetailDto[]}
          selectedIds={selectedEvaluatorIds}
          onToggle={toggleEvaluator}
        />
      ),
    },
  ];

  return (
    <div className="w-full min-w-0 flex flex-col gap-3 h-full overflow-hidden">
      <div
        className={cn(
          'fade-up flex-1 min-h-0 [animation-delay:20ms]',
          isMobile ? 'flex flex-col' : `grid gap-4 ${LIST_RAIL_COLS}`,
        )}
      >
        {/* Left: suite list */}
        {(!isMobile || !selectedSuite) && (
          <SuiteList
            suites={visibleSuites}
            isLoading={isLoading}
            selectedId={selectedSuite?.id ?? null}
            highlightId={highlightSuiteId}
            onSelect={setSelectedSuiteId}
            onDelete={setDeleteSuite}
            onNew={() => { setCreateOpen(true); resetCreate(); }}
            agentFilter={{
              value: agentFilter,
              options: agentFilterOptions,
              accent: agentFilter ? agentColor(agentFilter) : undefined,
              onChange: setAgentFilter,
            }}
          />
        )}

        {/* Right: detail */}
        {(!isMobile || selectedSuite) && (
          <main className="min-w-0 min-h-0 overflow-y-auto pb-6 md:overflow-hidden md:pb-0">
            {isMobile && selectedSuite && (
              <Button
                variant="ghost"
                size="sm"
                className="mb-2"
                data-testid="suites-back-to-list"
                onClick={() => setSelectedSuiteId(null)}
                leftIcon={<ChevronRightIcon size={14} className="rotate-180" />}
              >
                <Trans>All suites</Trans>
              </Button>
            )}
            {selectedSuite
              ? <SuiteDetail
                  key={selectedSuite.id}
                  suiteId={selectedSuite.id}
                  projectId={projectId}
                  onRun={() => { setRunSuite(selectedSuite); setRunDone(false); }}
                  onDelete={() => setDeleteSuite(selectedSuite)}
                />
              : !isLoading
                ? <Card><div className="py-15 text-center text-muted text-body"><Trans>Select a suite to see details.</Trans></div></Card>
                : null}
          </main>
        )}
      </div>

      {/* Run confirm modal */}
      {runSuite && (
        <RunConfirmModal
          suite={runSuite}
          onClose={closeRunModal}
          onSubmit={(ids, sampleCount) => startRun.mutate({ suiteId: runSuite.id, endpointIds: ids, sampleCount })}
          loading={startRun.isPending}
          done={runDone}
        />
      )}

      {/* Create wizard */}
      {createOpen && (
        <Modal title={t`Create Test Suite`} onClose={() => { setCreateOpen(false); resetCreate(); }} size="xl">
          <StepWizard
            steps={wizardSteps}
            currentStep={createStep}
            onNext={() => setCreateStep(s => s + 1)}
            onBack={() => setCreateStep(s => s - 1)}
            onSubmit={() => createSuite.mutate({ name: createName, agentId: createAgentId, agentCallIds: Array.from(selectedCalls), evaluatorIds: Array.from(selectedEvaluatorIds) })}
            canAdvance={canAdvanceCreate}
            submitLabel={t`Create suite`}
            loading={createSuite.isPending}
          />
        </Modal>
      )}

      {/* Delete confirm */}
      {deleteSuite && (
        <ConfirmDialog
          entityName={deleteSuite.name}
          onConfirm={() => delSuite.mutate(deleteSuite.id)}
          onCancel={() => setDeleteSuite(null)}
          loading={delSuite.isPending}
        />
      )}
    </div>
  );
}
