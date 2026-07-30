import { describe, expect, it } from 'vitest';
import type { CostLimitDto } from '../../api/costs';
import type { AgentListItemDto, ApiKeyDto } from '../../api/models';
import {
  canCreateAny,
  defaultScopeKind,
  emptyReason,
  isKindSelectable,
  isScopeAvailable,
  scopeAvailability,
} from './scopeAvailability';

function limit(overrides: Partial<CostLimitDto> = {}): CostLimitDto {
  return {
    id: 'l1',
    projectId: 'p1',
    agentId: null,
    agentName: null,
    apiKeyId: null,
    apiKeyName: null,
    softLimitEur: null,
    hardLimitEur: 10,
    enabled: true,
    createdAt: '2026-07-01T00:00:00.000Z',
    updatedAt: '2026-07-01T00:00:00.000Z',
    ...overrides,
  };
}

const agent = (id: string, isSystemAgent = false) =>
  ({ id, name: `Agent ${id}`, isSystemAgent }) as AgentListItemDto;

const key = (id: string) => ({ id, name: `Key ${id}`, keyPrefix: 'pt_x' }) as ApiKeyDto;

describe('scopeAvailability', () => {
  it('reports everything free when no budget exists', () => {
    const a = scopeAvailability([], [agent('a1')], [key('k1')]);

    expect(a).toMatchObject({ projectTaken: false, anyAgents: true, anyApiKeys: true });
    expect(a.agents.map(x => x.id)).toEqual(['a1']);
    expect(a.apiKeys.map(x => x.id)).toEqual(['k1']);
  });

  it('marks the project scope taken once the project-wide budget exists', () => {
    // This is the case the old UI missed: it filtered agents and keys but always offered "Whole
    // project", so the second budget an admin created was aimed at a guaranteed 409.
    expect(scopeAvailability([limit()], [], []).projectTaken).toBe(true);
  });

  it('does not treat a scoped budget as the project budget', () => {
    expect(scopeAvailability([limit({ agentId: 'a1' })], [agent('a1')], []).projectTaken).toBe(false);
    expect(scopeAvailability([limit({ apiKeyId: 'k1' })], [], [key('k1')]).projectTaken).toBe(false);
  });

  it('removes agents and keys that already hold a budget', () => {
    const a = scopeAvailability(
      [limit({ agentId: 'a1' }), limit({ apiKeyId: 'k1' })],
      [agent('a1'), agent('a2')],
      [key('k1'), key('k2')],
    );

    expect(a.agents.map(x => x.id)).toEqual(['a2']);
    expect(a.apiKeys.map(x => x.id)).toEqual(['k2']);
    // Still "any" — they exist, they are just spoken for. The distinction drives the empty copy.
    expect(a).toMatchObject({ anyAgents: true, anyApiKeys: true });
  });

  it('never offers a system agent, and does not count one as an agent existing', () => {
    const a = scopeAvailability([], [agent('sys', true)], []);

    expect(a.agents).toEqual([]);
    expect(a.anyAgents).toBe(false);
  });
});

describe('isKindSelectable / canCreateAny', () => {
  it('refuses a kind with nothing left to point at', () => {
    const a = scopeAvailability([limit()], [agent('sys', true)], []);

    expect(isKindSelectable('project', a)).toBe(false);
    expect(isKindSelectable('agent', a)).toBe(false);
    expect(isKindSelectable('apiKey', a)).toBe(false);
    expect(canCreateAny(a)).toBe(false);
  });

  it('still allows a second budget when an agent or key is free', () => {
    const a = scopeAvailability([limit()], [agent('a1')], []);

    expect(isKindSelectable('project', a)).toBe(false);
    expect(isKindSelectable('agent', a)).toBe(true);
    expect(canCreateAny(a)).toBe(true);
  });
});

describe('isScopeAvailable', () => {
  it('accepts a free project scope and refuses a taken one', () => {
    expect(isScopeAvailable({ kind: 'project', elementId: null }, scopeAvailability([], [], []))).toBe(true);
    expect(isScopeAvailable({ kind: 'project', elementId: null }, scopeAvailability([limit()], [], []))).toBe(false);
  });

  it('refuses an incomplete element scope', () => {
    const a = scopeAvailability([], [agent('a1')], [key('k1')]);

    expect(isScopeAvailable({ kind: 'agent', elementId: null }, a)).toBe(false);
    expect(isScopeAvailable({ kind: 'apiKey', elementId: null }, a)).toBe(false);
  });

  /**
   * The kind alone is not enough. The roster is live query data: the picked agent can be deleted —
   * or take a budget in another tab — while the dialog is open. The picker then shows its
   * placeholder, because the value is no longer one of its items, so a Save gated on the kind would
   * post an id the user can no longer see.
   */
  it('refuses an element that is gone from the roster even when its kind still has options', () => {
    const a = scopeAvailability([limit({ agentId: 'a1' })], [agent('a1'), agent('a2')], []);

    expect(isKindSelectable('agent', a)).toBe(true);
    expect(isScopeAvailable({ kind: 'agent', elementId: 'a1' }, a)).toBe(false);
    expect(isScopeAvailable({ kind: 'agent', elementId: 'a2' }, a)).toBe(true);
  });

  it('refuses a key that has been revoked under the dialog', () => {
    const a = scopeAvailability([], [], [key('k2')]);

    expect(isScopeAvailable({ kind: 'apiKey', elementId: 'k1' }, a)).toBe(false);
    expect(isScopeAvailable({ kind: 'apiKey', elementId: 'k2' }, a)).toBe(true);
  });
});

describe('defaultScopeKind', () => {
  it('opens on the project scope while it is free', () => {
    expect(defaultScopeKind(scopeAvailability([], [agent('a1')], [key('k1')]))).toBe('project');
  });

  it('skips a taken project scope and opens on agents', () => {
    expect(defaultScopeKind(scopeAvailability([limit()], [agent('a1')], [key('k1')]))).toBe('agent');
  });

  it('falls through to keys when the project and every agent are taken', () => {
    const a = scopeAvailability([limit(), limit({ agentId: 'a1' })], [agent('a1')], [key('k1')]);
    expect(defaultScopeKind(a)).toBe('apiKey');
  });

  it('falls back to the project scope when nothing is available at all', () => {
    // canCreateAny() is what stops the dialog opening; the draft still has to be valid.
    expect(defaultScopeKind(scopeAvailability([limit()], [], []))).toBe('project');
  });
});

describe('emptyReason', () => {
  it('says nothing while options remain', () => {
    const a = scopeAvailability([], [agent('a1')], [key('k1')]);

    expect(emptyReason('project', a)).toBeNull();
    expect(emptyReason('agent', a)).toBeNull();
    expect(emptyReason('apiKey', a)).toBeNull();
  });

  it('distinguishes "none exist" from "all taken"', () => {
    const noAgents = scopeAvailability([], [], []);
    expect(emptyReason('agent', noAgents)).toBe('none-exist');
    expect(emptyReason('apiKey', noAgents)).toBe('none-exist');

    const allTaken = scopeAvailability(
      [limit({ agentId: 'a1' }), limit({ apiKeyId: 'k1' })],
      [agent('a1')],
      [key('k1')],
    );
    expect(emptyReason('agent', allTaken)).toBe('all-taken');
    expect(emptyReason('apiKey', allTaken)).toBe('all-taken');
  });

  it('reports the taken project scope as taken', () => {
    expect(emptyReason('project', scopeAvailability([limit()], [], []))).toBe('all-taken');
  });
});
