import type { CostBudgetStatusDto } from '../../api/costs';

/**
 * Pure consumption math for the budget meters. Framework-free and unit-tested
 * (`budgetMeter.spec.ts`) so `BudgetSection` stays a presentational shell.
 */

export type BudgetState = 'ok' | 'approaching' | 'soft' | 'hard' | 'disabled';

export interface BudgetMeter {
  /** Fill fraction clamped to [0,1] — the bar never overflows its track. */
  fill: number;
  /** Position of the soft-limit marker as a fraction of the track, or null when unset. */
  softMarker: number | null;
  /** The amount the meter is scaled against: the hard limit, else the soft limit. */
  scaleEur: number | null;
  state: BudgetState;
  /** Consumption as a fraction of {@link scaleEur} — may exceed 1 while `fill` is clamped. */
  consumed: number | null;
}

/** Above this fraction of the *next* threshold the meter warns before anything has fired. */
export const APPROACHING_FRACTION = 0.8;

/**
 * Derives the meter geometry and state for one budget.
 *
 * State comes from the persisted breach flags first — those are what the guard actually fired and
 * what the proxy enforces — and only falls back to a spend-vs-threshold comparison for the
 * "approaching" hint. That ordering matters: between a threshold being crossed and the next guard
 * tick the two disagree, and the flags are the truth the rest of the system acts on.
 */
export function budgetMeter(budget: CostBudgetStatusDto): BudgetMeter {
  const scaleEur = budget.hardLimitEur ?? budget.softLimitEur;
  const consumed = scaleEur && scaleEur > 0 ? budget.monthToDateSpendEur / scaleEur : null;
  const fill = consumed === null ? 0 : Math.min(1, Math.max(0, consumed));

  const softMarker =
    budget.softLimitEur !== null && scaleEur && scaleEur > 0
      ? Math.min(1, budget.softLimitEur / scaleEur)
      : null;

  return { fill, softMarker, scaleEur, consumed, state: budgetState(budget, consumed) };
}

function budgetState(budget: CostBudgetStatusDto, consumed: number | null): BudgetState {
  if (!budget.enabled) return 'disabled';
  if (budget.hardBreached) return 'hard';
  if (budget.softBreached) return 'soft';
  if (consumed !== null && consumed >= APPROACHING_FRACTION) return 'approaching';
  return 'ok';
}

/** The EUR still available before the hard limit stops calls, or null when no hard limit is set. */
export function remainingEur(budget: CostBudgetStatusDto): number | null {
  if (budget.hardLimitEur === null) return null;
  return Math.max(0, budget.hardLimitEur - budget.monthToDateSpendEur);
}

/**
 * Sorts budgets for display: most urgent first (hard, then soft, then approaching), with the
 * project-wide budget ahead of its agent overrides inside each tier.
 */
export function sortBudgets(budgets: readonly CostBudgetStatusDto[]): CostBudgetStatusDto[] {
  const rank: Record<BudgetState, number> = { hard: 0, soft: 1, approaching: 2, ok: 3, disabled: 4 };
  return [...budgets].sort((a, b) => {
    const byState = rank[budgetMeter(a).state] - rank[budgetMeter(b).state];
    if (byState !== 0) return byState;
    const byScope = Number(a.agentId !== null) - Number(b.agentId !== null);
    if (byScope !== 0) return byScope;
    return (a.agentName ?? '').localeCompare(b.agentName ?? '');
  });
}

/**
 * Validates an edited budget the same way the API does, so the form can refuse before a round trip.
 * Returns a stable error code (the caller owns the translated copy) or null when valid.
 */
export type BudgetFormError = 'no-threshold' | 'not-positive' | 'soft-above-hard';

export function validateBudget(soft: number | null, hard: number | null): BudgetFormError | null {
  if (soft === null && hard === null) return 'no-threshold';
  if ((soft !== null && soft <= 0) || (hard !== null && hard <= 0)) return 'not-positive';
  if (soft !== null && hard !== null && soft > hard) return 'soft-above-hard';
  return null;
}

/** Parses a form field into an amount: blank means "unset", anything unparseable means invalid. */
export function parseAmount(raw: string): { value: number | null; valid: boolean } {
  const trimmed = raw.trim();
  if (trimmed === '') return { value: null, valid: true };
  const value = Number(trimmed.replace(',', '.'));
  if (!Number.isFinite(value)) return { value: null, valid: false };
  return { value, valid: true };
}
