import { describe, it, expect, vi, beforeEach } from 'vitest';

const { scopesApi } = vi.hoisted(() => ({ scopesApi: { list: vi.fn() } }));
vi.mock('../../../api/scopes', () => ({ scopesApi }));

import { createScopeTools } from './scopes';
import type { TraceyTool, TraceyToolContext } from './shared';

const store = vi.fn(async (_kind: string, _full: unknown, summary: unknown) => summary);

function run(t: TraceyTool, ctx: TraceyToolContext) {
  if (!t.execute) throw new Error('tool has no execute');
  return t.execute({}, ctx);
}

const ctx = (over: Partial<TraceyToolContext> = {}): TraceyToolContext => ({
  projectId: 'p1',
  artifactScope: 'u:p',
  navigate: vi.fn(),
  confirm: vi.fn().mockResolvedValue(true),
  loadedSkillIds: new Set<string>(),
  ...over,
});

beforeEach(() => vi.clearAllMocks());

describe('list_scopes', () => {
  it('digests each scope to its id, key, label, activity and member agents', async () => {
    scopesApi.list.mockResolvedValue([{
      id: 's1', projectId: 'p1', key: 'support-agents', displayName: null, description: null,
      createdAt: '2026-10-01T00:00:00Z', lastActivityAt: '2026-10-01T01:00:00Z',
      traceCount: 3, totalTokens: 120, agentIds: ['a1', 'a2'],
    }]);

    const c = ctx();
    const result = await run(createScopeTools(c, store).list_scopes, c);

    expect(scopesApi.list).toHaveBeenCalledWith('p1');
    expect(result).toEqual({
      count: 1,
      items: [{
        id: 's1', key: 'support-agents', name: 'support-agents', traces: 3, tokens: 120,
        agentIds: ['a1', 'a2'], lastActivityAt: '2026-10-01T01:00:00Z',
      }],
    });
  });

  it('labels a scope the way the UI does, so a blank display name falls back to the key', async () => {
    scopesApi.list.mockResolvedValue([{
      id: 's1', projectId: 'p1', key: 'support-agents', displayName: '   ', description: null,
      createdAt: '2026-10-01T00:00:00Z', lastActivityAt: null, traceCount: 0, totalTokens: 0, agentIds: [],
    }]);

    const c = ctx();
    const result = await run(createScopeTools(c, store).list_scopes, c);

    expect(result).toMatchObject({ items: [{ name: 'support-agents' }] });
  });

  it('returns nothing without a project instead of calling the API', async () => {
    const c = ctx({ projectId: undefined });
    expect(await run(createScopeTools(c, store).list_scopes, c)).toEqual({ count: 0, items: [] });
    expect(scopesApi.list).not.toHaveBeenCalled();
  });
});
