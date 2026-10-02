import { describe, expect, it } from 'vitest';
import type { ScopeListItemDto } from '../api/models';
import { scopeLabel, scopesById } from './scopes';

const scope = (over: Partial<ScopeListItemDto> = {}): ScopeListItemDto => ({
  id: 's1',
  projectId: 'p1',
  key: 'support-agents',
  displayName: null,
  description: null,
  createdAt: '2026-10-01T00:00:00Z',
  lastActivityAt: null,
  traceCount: 0,
  totalTokens: 0,
  agentIds: [],
  ...over,
});

describe('scopeLabel', () => {
  it('prefers the display name', () => {
    expect(scopeLabel(scope({ displayName: 'Support crew' }))).toBe('Support crew');
  });

  it.each([null, '', '   '])('falls back to the key when the display name is %j', displayName => {
    expect(scopeLabel(scope({ displayName }))).toBe('support-agents');
  });
});

describe('scopesById', () => {
  it('indexes scopes by id', () => {
    const map = scopesById([scope({ id: 'a' }), scope({ id: 'b', key: 'billing' })]);
    expect(map.get('b')?.key).toBe('billing');
    expect(map.size).toBe(2);
  });
});
