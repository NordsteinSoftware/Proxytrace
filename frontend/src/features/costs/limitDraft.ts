import type { CostLimitDto } from '../../api/costs';

/**
 * The budget editor's form state. Amounts stay **strings** while the user types — a half-entered
 * "1." is not a number yet, and coercing it every keystroke would fight the input.
 */
export interface LimitDraft {
  agentId: string | null;
  soft: string;
  hard: string;
  enabled: boolean;
}

export function draftFromLimit(limit: CostLimitDto): LimitDraft {
  return {
    agentId: limit.agentId,
    soft: limit.softLimitEur === null ? '' : String(limit.softLimitEur),
    hard: limit.hardLimitEur === null ? '' : String(limit.hardLimitEur),
    enabled: limit.enabled,
  };
}

export function emptyDraft(): LimitDraft {
  return { agentId: null, soft: '', hard: '', enabled: true };
}
