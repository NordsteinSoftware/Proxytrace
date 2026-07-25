import { useState } from 'react';
import { useNavigate } from 'react-router';
import { Trans, useLingui } from '@lingui/react/macro';
import type { TestRunScheduleDto } from '../../../api/models';
import { useFeature } from '../../../hooks/useLicense';
import { showUpgradeModal } from '../../../components/license/UpgradeModal';
import { Button } from '../../../components/ui/Button';
import { EmptyState } from '../../../components/ui/EmptyState';
import { SkeletonList } from '../../../components/ui/Skeleton';
import { ConfirmDialog } from '../../../components/overlays/ConfirmDialog';
import { ClockIcon, LockIcon } from '../../../components/icons';
import { useTestRunSchedules } from '../../runs/hooks/useTestRunSchedules';
import { useTestRunScheduleMutations } from '../../runs/hooks/useTestRunScheduleMutations';
import { ScheduleCard } from '../../runs/components/ScheduleCard';
import { ScheduleFormDialog, type ScheduleFormValues } from '../../runs/components/ScheduleFormDialog';

interface Props { suiteId: string; suiteName: string; agentId: string; }

/**
 * Per-suite schedules surfaced inside the suite detail. Reuses the Runs schedule card/dialog/hooks,
 * filtering the agent-scoped list down to this suite and locking creation to it. Gated behind the
 * `ScheduledTestRuns` feature like the rest of scheduling.
 */
export function SuiteSchedulesSection({ suiteId, suiteName, agentId }: Props) {
  const { t } = useLingui();
  const licensed = useFeature('ScheduledTestRuns');
  const navigate = useNavigate();
  const { schedules, isLoading } = useTestRunSchedules(agentId);
  const { create, update, remove } = useTestRunScheduleMutations();

  const suiteSchedules = schedules.filter(s => s.suiteId === suiteId);
  const activeCount = suiteSchedules.filter(s => s.isEnabled).length;
  const [dialogOpen, setDialogOpen] = useState(false);
  const [editing, setEditing] = useState<TestRunScheduleDto | null>(null);
  const [deleteTarget, setDeleteTarget] = useState<TestRunScheduleDto | null>(null);

  function close() { setDialogOpen(false); setEditing(null); }
  function openCreate() {
    if (!licensed) { showUpgradeModal({ errorType: 'FeatureNotLicensed' }); return; }
    setEditing(null);
    setDialogOpen(true);
  }
  function submit(form: ScheduleFormValues) {
    if (editing) {
      update.mutate(
        { id: editing.id, body: { name: form.name, modelEndpointIds: form.modelEndpointIds, intervalMinutes: form.intervalMinutes, anchorAt: form.anchorAt, enabled: form.enabled } },
        { onSuccess: close },
      );
    } else {
      create.mutate(form, { onSuccess: close });
    }
  }
  function toggle(s: TestRunScheduleDto, next: boolean) {
    update.mutate({ id: s.id, body: { name: s.name, modelEndpointIds: s.endpoints.map(e => e.id), intervalMinutes: s.intervalMinutes, enabled: next } });
  }

  return (
    <div className="flex flex-col gap-2" data-testid="suite-schedules-section">
      <div className="flex items-center justify-between">
        <span className="text-caption font-semibold text-secondary uppercase tracking-[0.08em]">
          <Trans>{activeCount} active · {suiteSchedules.length} total</Trans>
        </span>
        <Button
          variant="secondary"
          size="sm"
          onClick={openCreate}
          leftIcon={licensed ? <ClockIcon size={13} /> : <LockIcon size={13} />}
          data-testid="suite-schedule-create-btn"
        >
          {licensed ? <Trans>New schedule</Trans> : <Trans>Upgrade to schedule</Trans>}
        </Button>
      </div>

      {isLoading && <SkeletonList rows={2} height={90} gap={8} />}

      {!isLoading && suiteSchedules.length === 0 && (
        <div data-testid="suite-schedules-empty-state">
          <EmptyState title={t`No schedules`} description={t`Run this suite on a recurring cadence.`} />
        </div>
      )}

      {!isLoading && suiteSchedules.length > 0 && (
        <div className="flex flex-col gap-2" data-testid="suite-schedules-list">
          {suiteSchedules.map(s => (
            <ScheduleCard
              key={s.id}
              schedule={s}
              toggling={update.isPending}
              onToggle={next => toggle(s, next)}
              onEdit={() => { setEditing(s); setDialogOpen(true); }}
              onDelete={() => setDeleteTarget(s)}
              onSelectRun={groupId => navigate(`/runs?id=${groupId}`)}
            />
          ))}
        </div>
      )}

      {dialogOpen && (
        <ScheduleFormDialog
          schedule={editing ?? undefined}
          lockedSuite={editing ? undefined : { id: suiteId, name: suiteName }}
          onClose={close}
          onSubmit={submit}
          pending={create.isPending || update.isPending}
        />
      )}

      {deleteTarget && (
        <ConfirmDialog
          title={t`Delete schedule "${deleteTarget.name}"?`}
          message={t`This stops future runs. Existing runs it produced are kept.`}
          onConfirm={() => remove.mutate(deleteTarget.id, { onSuccess: () => setDeleteTarget(null) })}
          onCancel={() => setDeleteTarget(null)}
          loading={remove.isPending}
        />
      )}
    </div>
  );
}
