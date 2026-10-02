import { describe, expect, it } from 'vitest';
import type { AgentListItemDto, ScopeListItemDto } from '../../api/models';
import {
  ALL_AGENT_SCOPES,
  filterAgentsByScope,
  groupAgentsByScope,
  parseScopeFilterValue,
  scopeFilterValue,
} from './agentScopes';

const agent = (id: string) => ({ id, name: id }) as AgentListItemDto;
const scope = (id: string, agentIds: string[]): ScopeListItemDto => ({
  id,
  projectId: 'p',
  key: id,
  displayName: null,
  description: null,
  createdAt: '2026-10-01T00:00:00Z',
  lastActivityAt: null,
  traceCount: 0,
  totalTokens: 0,
  agentIds,
});

const agents = [agent('triage'), agent('translator'), agent('billing'), agent('loner')];
const scopes = [scope('support', ['triage', 'translator']), scope('finance', ['billing', 'translator'])];

describe('filterAgentsByScope', () => {
  it('keeps every agent for "all"', () => {
    expect(filterAgentsByScope(agents, scopes, ALL_AGENT_SCOPES)).toHaveLength(4);
  });

  it('keeps one scope\'s members, including shared agents', () => {
    expect(filterAgentsByScope(agents, scopes, { kind: 'scope', scopeId: 'finance' }).map(a => a.id))
      .toEqual(['translator', 'billing']);
  });

  it('keeps agents in no scope for "unscoped"', () => {
    expect(filterAgentsByScope(agents, scopes, { kind: 'unscoped' }).map(a => a.id)).toEqual(['loner']);
  });

  it('falls back to every agent when the selected scope no longer exists', () => {
    expect(filterAgentsByScope(agents, scopes, { kind: 'scope', scopeId: 'gone' })).toHaveLength(4);
  });
});

describe('groupAgentsByScope', () => {
  it('lists a shared agent under each of its scopes and the rest under "no scope"', () => {
    const groups = groupAgentsByScope(agents, scopes);
    expect(groups.map(g => [g.scope?.id ?? null, g.agents.map(a => a.id)])).toEqual([
      ['support', ['triage', 'translator']],
      ['finance', ['translator', 'billing']],
      [null, ['loner']],
    ]);
  });

  it('omits sections with no listed agent', () => {
    const groups = groupAgentsByScope([agent('loner')], scopes);
    expect(groups).toEqual([{ scope: null, agents: [agent('loner')] }]);
  });
});

describe('scope filter value round-trip', () => {
  it.each([ALL_AGENT_SCOPES, { kind: 'unscoped' as const }, { kind: 'scope' as const, scopeId: 'abc' }])(
    'round-trips %j',
    filter => {
      expect(parseScopeFilterValue(scopeFilterValue(filter))).toEqual(filter);
    },
  );
});
