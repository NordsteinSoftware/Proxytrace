// Pure scope filtering/grouping for the agents rail. No JSX, no I/O — see agentScopes.spec.ts.

import type { AgentListItemDto, ScopeListItemDto } from '../../api/models';

/** Rail scope filter: every agent, only agents with no scope membership, or one scope's members. */
export type AgentScopeFilter = { kind: 'all' } | { kind: 'unscoped' } | { kind: 'scope'; scopeId: string };

export const ALL_AGENT_SCOPES: AgentScopeFilter = { kind: 'all' };

/** One rail section when grouping by scope; `scope: null` is the "no scope" section. */
export interface AgentScopeGroup {
  scope: ScopeListItemDto | null;
  agents: AgentListItemDto[];
}

function memberIds(scopes: readonly ScopeListItemDto[]): Set<string> {
  return new Set(scopes.flatMap(s => s.agentIds));
}

/** The agents the rail shows under a scope filter. A filter naming a vanished scope shows all. */
export function filterAgentsByScope(
  agents: readonly AgentListItemDto[],
  scopes: readonly ScopeListItemDto[],
  filter: AgentScopeFilter,
): AgentListItemDto[] {
  switch (filter.kind) {
    case 'all':
      return [...agents];
    case 'unscoped': {
      const members = memberIds(scopes);
      return agents.filter(a => !members.has(a.id));
    }
    case 'scope': {
      const scope = scopes.find(s => s.id === filter.scopeId);
      if (!scope) return [...agents];
      const members = new Set(scope.agentIds);
      return agents.filter(a => members.has(a.id));
    }
  }
}

/**
 * Sections for "group by scope": one per scope that has at least one listed agent (in the scopes'
 * order), then the agents with no scope. An agent serving several scopes appears in each — scope
 * membership is derived from traffic, not an exclusive assignment.
 */
export function groupAgentsByScope(
  agents: readonly AgentListItemDto[],
  scopes: readonly ScopeListItemDto[],
): AgentScopeGroup[] {
  const groups: AgentScopeGroup[] = scopes
    .map(scope => {
      const members = new Set(scope.agentIds);
      return { scope, agents: agents.filter(a => members.has(a.id)) };
    })
    .filter(g => g.agents.length > 0);

  const members = memberIds(scopes);
  const unscoped = agents.filter(a => !members.has(a.id));
  if (unscoped.length > 0) groups.push({ scope: null, agents: unscoped });
  return groups;
}

/** Stable string form of a filter for a Select value (scope ids are GUIDs, so no clash). */
export function scopeFilterValue(filter: AgentScopeFilter): string {
  return filter.kind === 'scope' ? filter.scopeId : filter.kind;
}

export function parseScopeFilterValue(value: string): AgentScopeFilter {
  if (value === 'all') return ALL_AGENT_SCOPES;
  if (value === 'unscoped') return { kind: 'unscoped' };
  return { kind: 'scope', scopeId: value };
}
