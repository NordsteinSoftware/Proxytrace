import type { CostBudgetStatusDto, CostLimitDto } from '../../api/costs';

/**
 * One budget as the meters render it.
 *
 * `monthToDateSpendEur` widens the DTO's `number` to `number | null`: **null is never a server
 * value**, it is this client marking a budget it has just created and whose spend the next budget
 * status refetch will measure. Faking a 0 there would render "€0.00 / €100 — €100 left this
 * month" for a scope that may already be over, which is a wrong reassurance rather than a delay.
 */
export type BudgetRow = Omit<CostBudgetStatusDto, 'monthToDateSpendEur'> & {
  monthToDateSpendEur: number | null;
};

/**
 * Folds a saved budget into the cached budget-status list, so the meter list reacts in the same
 * tick as the toast instead of waiting on the refetch.
 *
 * A **created** budget's spend is left unknown (`null` → the meter's `measuring` state) rather than
 * derived from whatever the page happens to have cached. The status read that resolves it is one
 * aggregate scan (two with a key-scoped budget), so the unknown lasts one cheap round trip — while
 * a guess would have to reason about whether the charted window happens to be the calendar month
 * the budget is measured over, and would silently be wrong whenever it did not.
 *
 * Breach flags reset to false on purpose: a create has none yet, and `PUT /api/cost-limits/{id}`
 * deletes the limit's breach rows so the next guard tick re-evaluates against the new thresholds.
 */
export function upsertBudget(prev: readonly BudgetRow[], limit: CostLimitDto): BudgetRow[] {
  const existing = prev.find(b => b.costLimitId === limit.id);
  const row: BudgetRow = {
    costLimitId: limit.id,
    agentId: limit.agentId,
    agentName: limit.agentName,
    apiKeyId: limit.apiKeyId,
    apiKeyName: limit.apiKeyName,
    softLimitEur: limit.softLimitEur,
    hardLimitEur: limit.hardLimitEur,
    enabled: limit.enabled,
    // An edit changes thresholds, never spend — keep the measured figure rather than dropping the
    // meter back to "measuring" for a change that cannot have moved it.
    monthToDateSpendEur: existing ? existing.monthToDateSpendEur : null,
    softBreached: false,
    hardBreached: false,
  };

  // Order is irrelevant here: the list re-sorts by urgency before rendering (`sortBudgets`).
  return existing
    ? prev.map(b => (b.costLimitId === limit.id ? row : b))
    : [...prev, row];
}

/** Removes a deleted budget from the cached list. No other row's figures depend on it. */
export function dropBudget(prev: readonly BudgetRow[], costLimitId: string): BudgetRow[] {
  return prev.filter(b => b.costLimitId !== costLimitId);
}
