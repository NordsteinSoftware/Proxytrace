import { describe, it, expect, vi, beforeEach } from 'vitest';

const { agentCallsApi } = vi.hoisted(() => ({
  agentCallsApi: { list: vi.fn(), get: vi.fn() },
}));
vi.mock('../../../api/agent-calls', () => ({ agentCallsApi }));

import { createTraceTools } from './traces';
import type { TraceyTool, TraceyToolContext } from './shared';

const store = vi.fn(async (_kind: string, _full: unknown, summary: unknown) => summary);

function run(t: TraceyTool, args: Record<string, unknown>, ctx: TraceyToolContext) {
  if (!t.execute) throw new Error('tool has no execute');
  return t.execute(args, ctx);
}

const ctx = (): TraceyToolContext => ({
  projectId: 'p1',
  artifactScope: 'u:p',
  navigate: vi.fn(),
  confirm: vi.fn().mockResolvedValue(true),
  loadedSkillIds: new Set<string>(),
});

/** One list row as the API returns it. `messagePreview` is the FIRST user message of the request. */
const call = (id: string, over: Record<string, unknown> = {}) => ({
  id,
  agentName: 'Support',
  model: 'm',
  httpStatus: 200,
  errorMessage: null,
  durationMs: 100,
  inputTokens: 10,
  outputTokens: 5,
  cachedInputTokens: 0,
  messagePreview: 'Maria promised me a refund.',
  toolCount: 1,
  conversationId: 'conv-1',
  outlierFlags: 0,
  createdAt: '2026-07-26T07:13:27Z',
  ...over,
});

beforeEach(() => vi.clearAllMocks());

describe('find_traces digest', () => {
  it('carries the conversation id and per-call tool-call count', async () => {
    // Every call of one tool loop previews the SAME first user message, so these two fields are the
    // only thing that makes the loop legible — and that tells the decision points apart from the
    // closing summary (toolCallsRequested: 0).
    agentCallsApi.list.mockResolvedValue({
      items: [
        call('t3', { toolCount: 0, createdAt: '2026-07-26T07:13:40Z' }),
        call('t2', { toolCount: 1, createdAt: '2026-07-26T07:13:35Z' }),
      ],
    });

    const c = ctx();
    const result = await run(createTraceTools(c, store).find_traces, {}, c) as {
      items: { id: string; conversationId: string; toolCallsRequested: number; preview: string }[];
    };

    expect(result.items).toEqual([
      expect.objectContaining({ id: 't3', conversationId: 'conv-1', toolCallsRequested: 0 }),
      expect.objectContaining({ id: 't2', conversationId: 'conv-1', toolCallsRequested: 1 }),
    ]);
    // Proving the point: the previews are identical, so they cannot distinguish the rows.
    expect(result.items[0].preview).toBe(result.items[1].preview);
  });
});

describe('get_agent_anomalies digest', () => {
  it('carries the conversation id so one flagged tool loop is not read as several incidents', async () => {
    agentCallsApi.list.mockResolvedValue({ items: [call('t1'), call('t2')] });

    const c = ctx();
    const result = await run(
      createTraceTools(c, store).get_agent_anomalies,
      { agentId: '11111111-1111-1111-1111-111111111111' },
      c,
    ) as { items: { id: string; conversationId: string }[] };

    expect(result.items.map((item) => item.conversationId)).toEqual(['conv-1', 'conv-1']);
  });
});
