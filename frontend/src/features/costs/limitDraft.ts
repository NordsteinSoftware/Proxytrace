import type { CostLimitDto } from '../../api/costs';

/**
 * What a budget is scoped to. A discriminated union rather than two nullable ids, because the
 * three cases are mutually exclusive — the backend rejects a limit carrying both an agent and a
 * key — and a union makes that unrepresentable instead of merely invalid.
 */
export type LimitScope =
  | { kind: 'project' }
  | { kind: 'agent'; agentId: string }
  | { kind: 'apiKey'; apiKeyId: string };

/** The three scopes a budget can have, as a pickable token. */
export type ScopeKind = LimitScope['kind'];

export const SCOPE_KINDS: readonly ScopeKind[] = ['project', 'agent', 'apiKey'];

/** Narrows a `<Select>` value back to a scope kind. Anything unrecognised falls back to project. */
export function toScopeKind(value: string): ScopeKind {
  switch (value) {
    case 'agent':
      return 'agent';
    case 'apiKey':
      return 'apiKey';
    default:
      return 'project';
  }
}

/**
 * The scope *while it is being picked*. Two controls fill it — the kind, then the element — so
 * "agent chosen, agent not yet named" is a state the form genuinely passes through and must be
 * able to hold. {@link LimitScope} stays the saved shape, where that state is unrepresentable.
 */
export interface DraftScope {
  kind: ScopeKind;
  /** The agent or API key id. Always null for the project scope; null means "not chosen yet". */
  elementId: string | null;
}

/**
 * The budget editor's form state. Amounts stay **strings** while the user types — a half-entered
 * "1." is not a number yet, and coercing it every keystroke would fight the input.
 */
export interface LimitDraft {
  scope: DraftScope;
  soft: string;
  hard: string;
  enabled: boolean;
}

export function draftFromLimit(limit: CostLimitDto): LimitDraft {
  return {
    scope: draftScopeOf(scopeOf(limit)),
    soft: limit.softLimitEur === null ? '' : String(limit.softLimitEur),
    hard: limit.hardLimitEur === null ? '' : String(limit.hardLimitEur),
    enabled: limit.enabled,
  };
}

/** A blank draft on the given scope kind, with no element chosen yet. */
export function emptyDraft(kind: ScopeKind = 'project'): LimitDraft {
  return { scope: { kind, elementId: null }, soft: '', hard: '', enabled: true };
}

/** Reads a saved budget's scope. Agent wins if both are somehow set — the backend forbids that. */
export function scopeOf(limit: CostLimitDto): LimitScope {
  if (limit.agentId !== null) return { kind: 'agent', agentId: limit.agentId };
  if (limit.apiKeyId !== null) return { kind: 'apiKey', apiKeyId: limit.apiKeyId };
  return { kind: 'project' };
}

/** Widens a saved scope into the draft shape the form edits. */
export function draftScopeOf(scope: LimitScope): DraftScope {
  switch (scope.kind) {
    case 'agent':
      return { kind: 'agent', elementId: scope.agentId };
    case 'apiKey':
      return { kind: 'apiKey', elementId: scope.apiKeyId };
    default:
      return { kind: 'project', elementId: null };
  }
}

/**
 * Narrows a draft scope back to a saved one, or null when the element is still unchosen. Null is
 * the form's "not submittable yet" signal — the caller disables Save rather than sending a
 * half-filled scope the API would reject.
 */
export function toLimitScope(scope: DraftScope): LimitScope | null {
  switch (scope.kind) {
    case 'agent':
      return scope.elementId === null ? null : { kind: 'agent', agentId: scope.elementId };
    case 'apiKey':
      return scope.elementId === null ? null : { kind: 'apiKey', apiKeyId: scope.elementId };
    default:
      return { kind: 'project' };
  }
}

/** The ids to send to the API. Exactly one is non-null unless the scope is the whole project. */
export function scopeIds(scope: LimitScope): { agentId: string | null; apiKeyId: string | null } {
  switch (scope.kind) {
    case 'agent':
      return { agentId: scope.agentId, apiKeyId: null };
    case 'apiKey':
      return { agentId: null, apiKeyId: scope.apiKeyId };
    default:
      return { agentId: null, apiKeyId: null };
  }
}
