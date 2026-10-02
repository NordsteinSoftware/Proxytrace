import { describe, expect, it } from 'vitest';
import type { ScopeListItemDto } from '../../api/models';
import { resolveDashboardScope } from './dashboardScope';

const scope: ScopeListItemDto = {
  id: 'scope-1', projectId: 'p1', key: 'support', displayName: null, description: null,
  createdAt: '2026-10-01T00:00:00Z', lastActivityAt: null, traceCount: 0, totalTokens: 0, agentIds: [],
};

describe('resolveDashboardScope', () => {
  it('resolves to all scopes at once when nothing is remembered', () => {
    expect(resolveDashboardScope(undefined, [], true)).toEqual({ scopeId: undefined, isResolved: true });
  });

  it('holds a remembered scope unresolved while the scope list loads', () => {
    expect(resolveDashboardScope('scope-1', [], true)).toEqual({ scopeId: undefined, isResolved: false });
  });

  it('keeps a remembered scope that still exists', () => {
    expect(resolveDashboardScope('scope-1', [scope], false)).toEqual({ scopeId: 'scope-1', isResolved: true });
  });

  it('falls back to all scopes when the remembered scope is gone', () => {
    expect(resolveDashboardScope('deleted', [scope], false)).toEqual({ scopeId: undefined, isResolved: true });
  });
});
