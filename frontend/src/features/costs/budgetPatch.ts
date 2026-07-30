import type { CostBudgetStatusDto, CostLimitDto, CostOverviewDto } from '../../api/costs';
import { monthStartIso } from './costSeries';

/**
 * One budget as the meters render it.
 *
 * `monthToDateSpendEur` widens the DTO's `number` to `number | null`: **null is never a server
 * value**, it is this client marking a budget it has just created and whose spend the next
 * overview refetch will measure. Faking a 0 there would render "€0.00 / €100 — €100 left this
 * month" for a scope that may already be over, which is a wrong reassurance rather than a delay.
 */
export type BudgetRow = Omit<CostBudgetStatusDto, 'monthToDateSpendEur'> & {
  monthToDateSpendEur: number | null;
};

/** The cost overview as it lives in the query cache — the wire payload plus the widening above. */
export type CostOverviewCache = Omit<CostOverviewDto, 'budgets'> & { budgets: BudgetRow[] };

/**
 * True when the cached window is exactly month-to-date, i.e. its per-agent / per-key totals *are*
 * the month's spend and can be reused for a brand-new budget's meter.
 *
 * `rangeKey` is the query key's third segment, `${from}|${to}|${bucket}` (see `useCostOverview`).
 */
export function isMonthToDateWindow(rangeKey: string, nowMs: number = Date.now()): boolean {
  const [from, to] = rangeKey.split('|');
  if (from !== monthStartIso(nowMs)) return false;
  const toMs = Date.parse(to ?? '');
  // `to` is quantized to the end of the current bucket, so a live window always reaches past now.
  // An absolute range ending mid-month would only cover part of it and must not be reused.
  return Number.isFinite(toMs) && toMs >= nowMs;
}

/**
 * Month-to-date spend for a newly created budget's scope, or null when this window cannot say.
 *
 * The project figure is window-independent — the API derives it from the month regardless of what
 * is being charted — so it is always exact. The per-agent and per-key figures come from the
 * window's totals and are only the month's spend when the window *is* the month.
 */
function spendForNewLimit(
  prev: CostOverviewCache,
  limit: CostLimitDto,
  rangeKey: string,
  nowMs: number,
): number | null {
  if (limit.agentId === null && limit.apiKeyId === null) return prev.monthToDateSpendEur;
  if (!isMonthToDateWindow(rangeKey, nowMs)) return null;
  if (limit.agentId !== null) {
    return prev.agentTotals.find(t => t.agentId === limit.agentId)?.costEur ?? 0;
  }
  return prev.apiKeyTotals.find(t => t.apiKeyId === limit.apiKeyId)?.costEur ?? 0;
}

/**
 * Folds a saved budget into a cached overview, so the meter list reacts in the same tick as the
 * toast instead of waiting on the overview refetch — which re-derives the whole page (eight
 * aggregate scans of the trace table) for a change to one configuration row.
 *
 * Breach flags reset to false on purpose: a create has none yet, and `PUT /api/cost-limits/{id}`
 * deletes the limit's breach rows so the next guard tick re-evaluates against the new thresholds.
 */
export function upsertBudget(
  prev: CostOverviewCache,
  limit: CostLimitDto,
  rangeKey: string,
  nowMs: number = Date.now(),
): CostOverviewCache {
  const existing = prev.budgets.find(b => b.costLimitId === limit.id);
  const row: BudgetRow = {
    costLimitId: limit.id,
    agentId: limit.agentId,
    agentName: limit.agentName,
    apiKeyId: limit.apiKeyId,
    apiKeyName: limit.apiKeyName,
    softLimitEur: limit.softLimitEur,
    hardLimitEur: limit.hardLimitEur,
    enabled: limit.enabled,
    // An edit changes thresholds, never spend — keep the measured figure rather than re-deriving it.
    monthToDateSpendEur: existing
      ? existing.monthToDateSpendEur
      : spendForNewLimit(prev, limit, rangeKey, nowMs),
    softBreached: false,
    hardBreached: false,
  };

  // Order is irrelevant here: the list re-sorts by urgency before rendering (`sortBudgets`).
  return {
    ...prev,
    budgets: existing
      ? prev.budgets.map(b => (b.costLimitId === limit.id ? row : b))
      : [...prev.budgets, row],
  };
}

/** Removes a deleted budget from a cached overview. No other row's figures depend on it. */
export function dropBudget(prev: CostOverviewCache, costLimitId: string): CostOverviewCache {
  return { ...prev, budgets: prev.budgets.filter(b => b.costLimitId !== costLimitId) };
}
