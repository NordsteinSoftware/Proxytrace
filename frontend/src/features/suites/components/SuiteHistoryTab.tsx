import { useNavigate } from 'react-router';
import { Trans, useLingui } from '@lingui/react/macro';
import { GroupListCard } from '../../runs/components/GroupListCard';
import { EmptyState } from '../../../components/ui/EmptyState';
import { SkeletonList } from '../../../components/ui/Skeleton';
import { useSuiteRunGroups } from '../hooks/useSuiteRunGroups';

/** History tab body: the suite's previous run groups (newest first). Each card navigates to the
 * Runs page with that group selected (`/runs?id=`). Read-only — managing/deleting runs stays on the
 * Runs page, so the cards render without a delete affordance. */
export function SuiteHistoryTab({ suiteId }: { suiteId: string }) {
  const { t } = useLingui();
  const navigate = useNavigate();
  const { groups, total, isLoading } = useSuiteRunGroups(suiteId);

  if (isLoading) return <SkeletonList rows={5} height={110} gap={8} />;

  if (groups.length === 0) {
    return (
      <EmptyState
        title={t`No runs yet`}
        description={t`Run this suite to build up a history. Each run shows up here, newest first.`}
      />
    );
  }

  return (
    <div className="flex flex-col gap-2" data-testid="suite-history-list">
      {groups.map(group => (
        <GroupListCard
          key={group.id}
          group={group}
          isSelected={false}
          onSelect={() => navigate(`/runs?id=${group.id}`)}
        />
      ))}
      {total > groups.length && (
        <p className="text-caption text-muted text-center py-2 m-0">
          <Trans>Showing the {groups.length} most recent of {total.toLocaleString()} runs — open the Runs page for the full list.</Trans>
        </p>
      )}
    </div>
  );
}
