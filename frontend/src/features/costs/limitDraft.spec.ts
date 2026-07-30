import { describe, expect, it } from 'vitest';
import type { CostLimitDto } from '../../api/costs';
import {
  draftFromLimit,
  draftScopeOf,
  emptyDraft,
  scopeIds,
  scopeOf,
  toLimitScope,
  type DraftScope,
  type LimitScope,
} from './limitDraft';

function limit(overrides: Partial<CostLimitDto>): CostLimitDto {
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

describe('scopeOf', () => {
  it('reads a project-wide budget', () => {
    expect(scopeOf(limit({}))).toEqual<LimitScope>({ kind: 'project' });
  });

  it('reads an agent budget', () => {
    expect(scopeOf(limit({ agentId: 'a1', agentName: 'Support bot' })))
      .toEqual<LimitScope>({ kind: 'agent', agentId: 'a1' });
  });

  it('reads an API key budget', () => {
    expect(scopeOf(limit({ apiKeyId: 'k1', apiKeyName: 'CI' })))
      .toEqual<LimitScope>({ kind: 'apiKey', apiKeyId: 'k1' });
  });
});

describe('scopeIds', () => {
  it('sends both ids null for the project scope', () => {
    expect(scopeIds({ kind: 'project' })).toEqual({ agentId: null, apiKeyId: null });
  });

  it('sends exactly one id for a scoped budget', () => {
    // The backend rejects a limit carrying both, so the two must never travel together.
    expect(scopeIds({ kind: 'agent', agentId: 'a1' })).toEqual({ agentId: 'a1', apiKeyId: null });
    expect(scopeIds({ kind: 'apiKey', apiKeyId: 'k1' })).toEqual({ agentId: null, apiKeyId: 'k1' });
  });
});

describe('draftScopeOf / toLimitScope', () => {
  it('round-trips every saved scope through the draft shape', () => {
    const scopes: LimitScope[] = [
      { kind: 'project' },
      { kind: 'agent', agentId: 'a1' },
      { kind: 'apiKey', apiKeyId: 'k1' },
    ];

    for (const scope of scopes) {
      expect(toLimitScope(draftScopeOf(scope))).toEqual(scope);
    }
  });

  it('keeps an agent and a key with the same id distinct', () => {
    // The two controls carry the kind separately, so the same id must not collapse the two scopes.
    expect(toLimitScope({ kind: 'agent', elementId: 'x' }))
      .not.toEqual(toLimitScope({ kind: 'apiKey', elementId: 'x' }));
  });

  it('refuses a scope whose element has not been picked yet', () => {
    // Null is the form's "not submittable" signal; sending it would be a guaranteed 400.
    expect(toLimitScope({ kind: 'agent', elementId: null })).toBeNull();
    expect(toLimitScope({ kind: 'apiKey', elementId: null })).toBeNull();
  });

  it('never asks the project scope for an element', () => {
    expect(toLimitScope({ kind: 'project', elementId: null })).toEqual<LimitScope>({ kind: 'project' });
    expect(draftScopeOf({ kind: 'project' })).toEqual<DraftScope>({ kind: 'project', elementId: null });
  });
});

describe('emptyDraft', () => {
  it('opens on the project scope by default', () => {
    expect(emptyDraft().scope).toEqual<DraftScope>({ kind: 'project', elementId: null });
  });

  it('opens on the requested kind with no element chosen', () => {
    // The page passes the first *available* kind, so the dialog never opens on a scope that is
    // already taken and could only ever answer 409.
    expect(emptyDraft('agent').scope).toEqual<DraftScope>({ kind: 'agent', elementId: null });
    expect(emptyDraft('apiKey').scope).toEqual<DraftScope>({ kind: 'apiKey', elementId: null });
  });

  it('starts enabled with both thresholds blank', () => {
    expect(emptyDraft()).toMatchObject({ soft: '', hard: '', enabled: true });
  });
});

describe('draftFromLimit', () => {
  it('carries an agent budget into a fully-populated draft scope', () => {
    expect(draftFromLimit(limit({ agentId: 'a1', agentName: 'Data Analyst', softLimitEur: 5 })))
      .toEqual({
        scope: { kind: 'agent', elementId: 'a1' },
        soft: '5',
        hard: '10',
        enabled: true,
      });
  });

  it('renders an unset threshold as an empty field rather than "null"', () => {
    expect(draftFromLimit(limit({ hardLimitEur: null, softLimitEur: 3 })))
      .toMatchObject({ soft: '3', hard: '' });
  });
});
