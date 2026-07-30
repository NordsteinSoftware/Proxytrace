import { Trans, useLingui } from '@lingui/react/macro';
import { Button } from '../../../components/ui/Button';
import { Tooltip } from '../../../components/ui/Tooltip';
import { LockIcon, PlusIcon } from '../../../components/icons';
import { showUpgradeModal } from '../../../components/license/UpgradeModal';

interface BudgetActionButtonProps {
  /** Admin *and* licensed — anything less cannot open the editor. */
  canEdit: boolean;
  isAdmin: boolean;
  /**
   * False when the project, every agent and every key already hold a budget. A scope takes at most
   * one, so there is nothing a new budget could target — opening the dialog would only lead to a
   * conflict on Save.
   */
  canCreate: boolean;
  /**
   * True while the budget list is still in flight. `canCreate` is not yet meaningful then — an
   * empty list reads as "every scope free" — and the editor seeds its scope once, when it opens, so
   * a click landing before the list arrives could freeze the dialog on an already-taken scope.
   */
  isLoading?: boolean;
  onCreate: () => void;
}

/**
 * The "New budget" call to action, in the header of the budgets card. Its own component because the
 * admin/licence/exhausted-scope branching is three states deep and has no business inside the
 * card's layout. Renders nothing for a non-admin: there is no action they could take.
 */
export function BudgetActionButton({
  canEdit,
  isAdmin,
  canCreate,
  isLoading = false,
  onCreate,
}: BudgetActionButtonProps) {
  const { t } = useLingui();
  if (!isAdmin) return null;

  if (canEdit) {
    const button = (
      <Button
        variant="primary"
        size="sm"
        onClick={onCreate}
        disabled={!canCreate}
        loading={isLoading}
        leftIcon={<PlusIcon size={14} />}
        data-testid="budget-create-btn"
      >
        <Trans>New budget</Trans>
      </Button>
    );

    // A disabled button with no explanation reads as a bug. Say which scopes are exhausted — but
    // only once we know: while loading, the button is busy, not refusing.
    return canCreate || isLoading
      ? button
      : (
        <Tooltip content={t`Every scope already has a budget — edit or delete one to change it.`}>
          <span>{button}</span>
        </Tooltip>
      );
  }

  return (
    <Button
      variant="secondary"
      size="sm"
      onClick={() => showUpgradeModal({ errorType: 'FeatureNotLicensed' })}
      leftIcon={<LockIcon size={14} />}
      data-testid="budget-upgrade-btn"
    >
      <Trans>Upgrade to set budgets</Trans>
    </Button>
  );
}
