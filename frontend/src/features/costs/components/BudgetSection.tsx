import { Trans, useLingui } from '@lingui/react/macro';
import { Button } from '../../../components/ui/Button';
import { Card } from '../../../components/ui/Card';
import { EmptyState } from '../../../components/ui/EmptyState';
import { SkeletonList } from '../../../components/ui/Skeleton';
import { LockIcon, PlusIcon } from '../../../components/icons';
import { showUpgradeModal } from '../../../components/license/UpgradeModal';
import type { CostBudgetStatusDto } from '../../../api/costs';
import { sortBudgets } from '../budgetMeter';
import { BudgetMeterRow } from './BudgetMeterRow';

interface BudgetSectionProps {
  budgets: readonly CostBudgetStatusDto[];
  /** Admin *and* licensed — anything less renders the locked state instead of the editor. */
  canEdit: boolean;
  isAdmin: boolean;
  isLoading: boolean;
  onCreate: () => void;
  onEdit: (costLimitId: string) => void;
}

/**
 * The project's monthly budgets as consumption meters. Listing is free on every tier; only
 * changing a budget is licensed, so an unlicensed admin sees the same data behind a locked CTA.
 */
export function BudgetSection({ budgets, canEdit, isAdmin, isLoading, onCreate, onEdit }: BudgetSectionProps) {
  const { t } = useLingui();
  const rows = sortBudgets(budgets);

  return (
    <Card padding="md" data-testid="budget-section">
      <Card.Header
        title={t`Monthly budgets`}
        description={t`Spend resets on the 1st (UTC). Alerts re-arm and blocks lift automatically.`}
        action={renderAction()}
      />
      <Card.Body>
        {isLoading && <SkeletonList rows={2} height={72} gap={10} />}

        {!isLoading && rows.length === 0 && (
          <div data-testid="budget-empty-state">
            <EmptyState
              title={t`No budgets configured`}
              description={isAdmin
                ? t`Set a monthly limit to be warned — or to stop calls — before spend runs away.`
                : t`An administrator can set monthly spend limits for this project.`}
            />
          </div>
        )}

        {!isLoading && rows.length > 0 && (
          <div data-testid="budget-list">
            {rows.map(budget => (
              <BudgetMeterRow
                key={budget.costLimitId}
                budget={budget}
                canEdit={canEdit}
                onEdit={() => onEdit(budget.costLimitId)}
              />
            ))}
          </div>
        )}
      </Card.Body>
    </Card>
  );

  function renderAction() {
    if (!isAdmin) return undefined;
    if (canEdit) {
      return (
        <Button variant="primary" size="sm" onClick={onCreate} leftIcon={<PlusIcon size={14} />} data-testid="budget-create-btn">
          <Trans>New budget</Trans>
        </Button>
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
}
