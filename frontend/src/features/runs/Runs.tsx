import { useState } from 'react';
import { useSearchParams } from 'react-router';
import { Trans, useLingui } from '@lingui/react/macro';
import { agentColor } from '../../lib/colors';
import { cn } from '../../lib/cn';
import { useSelectedId } from '../../hooks/useSelectedId';
import { useIsMobile } from '../../hooks/useMediaQuery';
import { Button } from '../../components/ui/Button';
import { ChevronRightIcon } from '../../components/icons';
import { ConfirmDialog } from '../../components/overlays/ConfirmDialog';
import { Card } from '../../components/ui/Card';
import { LIST_RAIL_COLS } from '../../components/ui/ListRail';
import { Tabs } from '../../components/ui/Tabs';
import { RunList } from './components/RunList';
import { SchedulesSection } from './components/SchedulesSection';
import { GroupDetail } from './GroupDetail';
import { useTestRunGroups } from './hooks/useTestRunGroups';
import { useProjectAgents } from './hooks/useProjectAgents';
import { useDeleteTestRunGroup } from './hooks/useDeleteTestRunGroup';

export default function Runs() {
  const { t } = useLingui();
  const [searchParams] = useSearchParams();
  // eslint-disable-next-line lingui/no-unlocalized-strings -- URL query-param key
  const runParam = searchParams.get('run');

  const [agentFilter, setAgentFilter] = useState('');
  // eslint-disable-next-line lingui/no-unlocalized-strings -- tab state token, not UI copy
  const [tab, setTab] = useState<'runs' | 'scheduled'>('runs');
  // Selected group lives in ?id= (survives refresh); ?run= is a one-shot deep-link
  // into a specific run that resolves to its owning group, then yields to ?id=.
  const [selectedGroupId, setSelectedGroupId] = useSelectedId();
  const [deleteGroupId, setDeleteGroupId] = useState<string | null>(null);
  // A/B (system) runs are hidden by default; deep-linking to one (?run=) reveals them.
  const [showSystem, setShowSystem] = useState(() => runParam != null);

  const { groups, isLoading, hasMore, loadMore, isLoadingMore } = useTestRunGroups(agentFilter, showSystem);
  const { agents } = useProjectAgents();
  const delGroup = useDeleteTestRunGroup();

  // Deep link: select the group that owns the linked run until the user picks another.
  const linkedGroupId = runParam
    ? groups.find(g => g.runs.some(r => r.id === runParam))?.id
    : undefined;
  // On mobile the list and detail are separate screens: only an explicit selection opens the
  // detail, so the list is the landing view. Desktop keeps the select-first default.
  const isMobile = useIsMobile();
  const explicitGroup =
    groups.find(g => g.id === selectedGroupId)
    ?? groups.find(g => g.id === linkedGroupId)
    ?? null;
  const selectedGroup = explicitGroup ?? (isMobile ? null : groups[0] ?? null);
  const agentOptions = [
    { key: '', label: t`All agents` },
    ...agents.map(a => ({ key: a.id, label: a.name, accent: agentColor(a.id) })),
  ];

  const deleteTarget = groups.find(g => g.id === deleteGroupId);

  const confirmDelete = () => {
    if (!deleteGroupId) return;
    const id = deleteGroupId;
    delGroup.mutate(id, {
      onSuccess: () => {
        setDeleteGroupId(null);
        if (id === selectedGroupId) setSelectedGroupId(null);
      },
    });
  };

  // Recent-run deep-link from a schedule card: select the group and surface the Runs tab.
  const selectRunFromSchedule = (groupId: string) => {
    // eslint-disable-next-line lingui/no-unlocalized-strings -- URL query-param key + tab state token
    setSelectedGroupId(groupId, ['run']);
    // eslint-disable-next-line lingui/no-unlocalized-strings -- tab state token, not UI copy
    setTab('runs');
  };

  return (
    <div className="w-full min-w-0 flex flex-col gap-3.5 px-1 pt-1 flex-1 min-h-0">
      <Tabs
        value={tab}
        onChange={v => setTab(v as 'runs' | 'scheduled')}
        items={[
          // eslint-disable-next-line lingui/no-unlocalized-strings -- tab value token + test id, not UI copy
          { value: 'runs', label: t`Runs`, 'data-testid': 'runs-tab' },
          // eslint-disable-next-line lingui/no-unlocalized-strings -- tab value token + test id, not UI copy
          { value: 'scheduled', label: t`Scheduled`, 'data-testid': 'schedules-tab' },
        ]}
      />

      {tab === 'scheduled' ? (
        <div className="fade-up [animation-delay:40ms] flex-1 min-h-0 overflow-y-auto">
          <SchedulesSection agentFilter={agentFilter} onSelectRun={selectRunFromSchedule} />
        </div>
      ) : (
      <div
        className={cn(
          'fade-up [animation-delay:40ms] flex-1 min-h-0',
          isMobile ? 'flex flex-col' : `grid gap-4 ${LIST_RAIL_COLS}`,
        )}
      >
        {/* Left: group list — scrolls independently of the detail panel.
            On mobile it is the landing screen and hides once a group is opened. */}
        {(!isMobile || !selectedGroup) && (
          <RunList
            groups={groups}
            isLoading={isLoading}
            selectedId={selectedGroup?.id ?? null}
            // eslint-disable-next-line lingui/no-unlocalized-strings -- URL query-param key
            onSelect={id => setSelectedGroupId(id, ['run'])}
            onDelete={id => setDeleteGroupId(id)}
            agentFilter={{
              value: agentFilter,
              options: agentOptions,
              accent: agentFilter ? agentColor(agentFilter) : undefined,
              onChange: setAgentFilter,
            }}
            showSystem={showSystem}
            onToggleSystem={() => setShowSystem(v => !v)}
            hasMore={hasMore}
            onLoadMore={loadMore}
            isLoadingMore={isLoadingMore}
          />
        )}

        {/* Right: detail — on mobile a full-screen view with a back affordance */}
        {(!isMobile || selectedGroup) && (
        <div className={cn('min-w-0 min-h-0 overflow-y-auto flex flex-col gap-2', isMobile && 'flex-1')}>
          {isMobile && (
            <Button
              variant="ghost"
              size="sm"
              className="self-start shrink-0"
              data-testid="runs-back-to-list"
              // eslint-disable-next-line lingui/no-unlocalized-strings -- URL query-param key
              onClick={() => setSelectedGroupId(null, ['run'])}
              leftIcon={<ChevronRightIcon size={14} className="rotate-180" />}
            >
              <Trans>All runs</Trans>
            </Button>
          )}
          {selectedGroup
            ? <GroupDetail key={selectedGroup.id} groupId={selectedGroup.id} onDelete={() => setDeleteGroupId(selectedGroup.id)} />
            : <Card><div className="py-15 text-center text-muted text-body"><Trans>Select a run to see details.</Trans></div></Card>
          }
        </div>
        )}
      </div>
      )}

      {deleteGroupId && deleteTarget && (
        <ConfirmDialog
          title={t`Delete run for "${deleteTarget.suiteName}"?`}
          message={t`This permanently deletes the run and its results. This action cannot be undone.`}
          onConfirm={confirmDelete}
          onCancel={() => setDeleteGroupId(null)}
          loading={delGroup.isPending}
        />
      )}
    </div>
  );
}
