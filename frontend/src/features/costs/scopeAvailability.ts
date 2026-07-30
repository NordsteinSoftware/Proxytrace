import type { CostLimitDto } from '../../api/costs';
import type { AgentListItemDto, ApiKeyDto } from '../../api/models';
import { SCOPE_KINDS, type DraftScope, type ScopeKind } from './limitDraft';

/**
 * Which scopes a *new* budget can still target. Every scope holds at most one budget — the API
 * answers 409 for a second — so the editor has to know what is already taken before it offers
 * anything.
 *
 * The project scope is the one the UI used to forget: agents and keys were filtered down to the
 * unbudgeted ones, but "Whole project" was offered unconditionally *and* was the default, so the
 * second budget an admin tried to create was aimed at the one scope guaranteed to fail.
 */
export interface ScopeAvailability {
  /** True when the project-wide budget already exists. */
  projectTaken: boolean;
  /** Agents that could still take a budget (real agents only — system agents are never offered). */
  agents: readonly AgentListItemDto[];
  /** Inbound API keys that could still take a budget. */
  apiKeys: readonly ApiKeyDto[];
  /** True when the project has at least one real agent at all, budgeted or not. */
  anyAgents: boolean;
  /** True when the project has at least one inbound key at all, budgeted or not. */
  anyApiKeys: boolean;
}

/** Why a scope kind has nothing to offer — the two cases need different copy and different advice. */
export type ScopeEmptyReason = 'none-exist' | 'all-taken';

export function scopeAvailability(
  limits: readonly CostLimitDto[],
  allAgents: readonly AgentListItemDto[],
  apiKeys: readonly ApiKeyDto[],
): ScopeAvailability {
  const takenAgentIds = new Set(limits.map(l => l.agentId).filter(id => id !== null));
  const takenKeyIds = new Set(limits.map(l => l.apiKeyId).filter(id => id !== null));
  // System agents (Tracey, evaluators, optimizers, detectors) are infrastructure, not spend an
  // operator budgets — the page has always excluded them and they must not count as "any agents"
  // either, or an install with only system agents would claim agent budgets are available.
  const realAgents = allAgents.filter(a => !a.isSystemAgent);

  return {
    projectTaken: limits.some(l => l.agentId === null && l.apiKeyId === null),
    agents: realAgents.filter(a => !takenAgentIds.has(a.id)),
    apiKeys: apiKeys.filter(k => !takenKeyIds.has(k.id)),
    anyAgents: realAgents.length > 0,
    anyApiKeys: apiKeys.length > 0,
  };
}

/** Whether a new budget can still be aimed at this kind of scope. */
export function isKindSelectable(kind: ScopeKind, availability: ScopeAvailability): boolean {
  switch (kind) {
    case 'project':
      return !availability.projectTaken;
    case 'agent':
      return availability.agents.length > 0;
    default:
      return availability.apiKeys.length > 0;
  }
}

/**
 * Whether a *complete* draft scope could still be created — kind **and** element.
 *
 * The element half matters: the roster is live query data. An agent picked in the dialog can be
 * deleted, or take a budget in another tab, while the dialog is open. The picker then falls back to
 * its placeholder (the value is no longer one of its items), so the field reads as unchosen while
 * the draft still carries the id — and a Save gated on the *kind* alone would happily post it.
 */
export function isScopeAvailable(scope: DraftScope, availability: ScopeAvailability): boolean {
  if (scope.kind === 'project') return !availability.projectTaken;
  if (scope.elementId === null) return false;
  const items = scope.kind === 'agent' ? availability.agents : availability.apiKeys;
  return items.some(item => item.id === scope.elementId);
}

/**
 * The kind a new budget should open on: the first one that can actually be saved. Falls back to
 * `project` when nothing is available, so the caller still gets a valid draft — it pairs with
 * {@link canCreateAny}, which is what decides whether to offer the dialog at all.
 */
export function defaultScopeKind(availability: ScopeAvailability): ScopeKind {
  return SCOPE_KINDS.find(kind => isKindSelectable(kind, availability)) ?? 'project';
}

/** True when at least one scope is still free — i.e. creating a budget can succeed. */
export function canCreateAny(availability: ScopeAvailability): boolean {
  return SCOPE_KINDS.some(kind => isKindSelectable(kind, availability));
}

/**
 * Why the element picker for `kind` has nothing to list, or null when it has options. The editor
 * could not tell these apart before, because it only ever received the already-filtered list — so
 * "you have no agents yet" and "every agent already has a budget" both rendered as a missing
 * dropdown group and no explanation at all.
 */
export function emptyReason(kind: ScopeKind, availability: ScopeAvailability): ScopeEmptyReason | null {
  if (kind === 'project') return availability.projectTaken ? 'all-taken' : null;
  if (kind === 'agent') {
    if (availability.agents.length > 0) return null;
    return availability.anyAgents ? 'all-taken' : 'none-exist';
  }
  if (availability.apiKeys.length > 0) return null;
  return availability.anyApiKeys ? 'all-taken' : 'none-exist';
}
