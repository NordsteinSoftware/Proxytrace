import { useLingui } from '@lingui/react/macro';
import { Card } from '../../../components/ui/Card';
import { EmptyState } from '../../../components/ui/EmptyState';
import { SkeletonList } from '../../../components/ui/Skeleton';
import { sortBudgets } from '../budgetMeter';
import type { BudgetRow } from '../budgetPatch';
import { BudgetActionButton } from './BudgetActionButton';
import { BudgetMeterRow } from './BudgetMeterRow';

interface BudgetSectionProps {
  budgets: readonly BudgetRow[];
  /** Admin *and* licensed — anything less renders the locked state instead of the editor. */
  canEdit: boolean;
  isAdmin: boolean;
  isLoading: boolean;
  /** False when every scope already holds a budget — there is nothing left to create. */
  canCreate: boolean;
  onCreate: () => void;
  onEdit: (costLimitId: string) => void;
  onDelete: (costLimitId: string) => void;
}

/**
 * The project's monthly budgets as consumption meters. Listing is free on every tier; only
 * changing a budget is licensed, so an unlicensed admin sees the same data behind a locked CTA.
 *
 * The create action sits in this card's header and nowhere else — one action, one place, always in
 * the same corner of the thing it acts on. It was previously in the page toolbar (with a second
 * copy inside the empty state), which put it a long way from the list it adds to and gave the page
 * two identical buttons whenever there were no budgets yet.
 */
export function BudgetSection({
  budgets,
  canEdit,
  isAdmin,
  isLoading,
  canCreate,
  onCreate,
  onEdit,
  onDelete,
}: BudgetSectionProps) {
  const { t } = useLingui();
  const rows = sortBudgets(budgets);

  return (
    <Card padding="md" data-testid="budget-section">
      <Card.Header
        title={t`Monthly budgets`}
        description={t`Spend resets on the 1st (UTC). Alerts re-arm and blocks lift automatically.`}
        action={(
          <BudgetActionButton
            canEdit={canEdit}
            isAdmin={isAdmin}
            canCreate={canCreate}
            isLoading={isLoading}
            onCreate={onCreate}
          />
        )}
      />
      <Card.Body>
        {isLoading && <SkeletonList rows={2} height={72} gap={10} />}

        {!isLoading && rows.length === 0 && (
          <div data-testid="budget-empty-state">
            {/* No action here: the header's "New budget" is directly above this text and always
                visible, so repeating it would be two identical buttons a few pixels apart. */}
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
                onDelete={() => onDelete(budget.costLimitId)}
              />
            ))}
          </div>
        )}
      </Card.Body>
    </Card>
  );
}
