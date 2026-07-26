import { Trans, useLingui } from '@lingui/react/macro';
import { Badge, type BadgeVariant } from '../../../components/ui/Badge';
import { Button } from '../../../components/ui/Button';
import { EYEBROW_CLS } from '../../../components/ui/classes';
import { cn } from '../../../lib/cn';
import { fmtCost } from '../../../lib/format';
import type { CostBudgetStatusDto } from '../../../api/costs';
import { budgetMeter, remainingEur, type BudgetState } from '../budgetMeter';

/* eslint-disable lingui/no-unlocalized-strings -- Badge variant tokens, not UI copy */
const STATE_VARIANT: Record<BudgetState, BadgeVariant> = {
  ok: 'success',
  approaching: 'warn',
  soft: 'warn',
  hard: 'danger',
  disabled: 'neutral',
};

const FILL_CLS: Record<BudgetState, string> = {
  ok: 'bg-success',
  approaching: 'bg-warn',
  soft: 'bg-warn',
  hard: 'bg-danger',
  disabled: 'bg-muted',
};
/* eslint-enable lingui/no-unlocalized-strings */

interface BudgetMeterRowProps {
  budget: CostBudgetStatusDto;
  canEdit: boolean;
  onEdit: () => void;
}

/**
 * One budget as a consumption meter: a fill scaled against the hard limit (or the soft one when
 * that is all that is set), a marker where the soft threshold sits, and the state badge.
 */
export function BudgetMeterRow({ budget, canEdit, onEdit }: BudgetMeterRowProps) {
  const { t } = useLingui();
  const meter = budgetMeter(budget);
  const remaining = remainingEur(budget);

  const stateLabel: Record<BudgetState, string> = {
    ok: t`Within budget`,
    approaching: t`Approaching limit`,
    soft: t`Soft limit reached`,
    hard: t`Blocking calls`,
    disabled: t`Disabled`,
  };

  return (
    <div
      className="flex flex-col gap-2 border-b border-border-subtle py-3 last:border-b-0"
      data-testid={`budget-row-${budget.costLimitId}`}
    >
      <div className="flex items-center justify-between gap-3">
        <span className="flex items-center gap-2 min-w-0">
          <span className="text-body text-primary truncate" data-testid={`budget-scope-${budget.costLimitId}`}>
            {budget.agentName ?? t`Whole project`}
          </span>
          <Badge
            label={stateLabel[meter.state]}
            variant={STATE_VARIANT[meter.state]}
            size="sm"
          />
        </span>
        {canEdit && (
          <Button variant="ghost" size="sm" onClick={onEdit} data-write data-testid={`budget-edit-btn-${budget.costLimitId}`}>
            <Trans>Edit</Trans>
          </Button>
        )}
      </div>

      <div className="relative h-1.5 bg-card-2 overflow-hidden">
        <div
          className={cn('h-full', FILL_CLS[meter.state])}
          style={{ width: `${meter.fill * 100}%` }}
          data-testid={`budget-fill-${budget.costLimitId}`}
        />
        {meter.softMarker !== null && meter.softMarker < 1 && (
          // The soft threshold sits inside the track when a hard limit scales it; the tick keeps
          // the two thresholds legible without a second bar.
          <span
            className="absolute top-0 h-full w-px bg-primary/60"
            style={{ left: `${meter.softMarker * 100}%` }}
            aria-hidden="true"
          />
        )}
      </div>

      <div className="flex items-center justify-between gap-3">
        <span className="font-mono text-body-sm text-secondary" data-testid={`budget-spend-${budget.costLimitId}`}>
          {fmtCost(budget.monthToDateSpendEur)}
          {meter.scaleEur !== null && <span className="text-muted"> / {fmtCost(meter.scaleEur)}</span>}
        </span>
        <span className={cn(EYEBROW_CLS, 'normal-case tracking-normal')}>
          {remaining === null
            ? t`Soft limit only — never blocks`
            : t`${fmtCost(remaining)} left this month`}
        </span>
      </div>
    </div>
  );
}
