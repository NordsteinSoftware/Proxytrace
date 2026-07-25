import { describe, it, expect } from 'vitest';
import type { AgentCallListItemDto } from '../../api/models';
import { summarizeTraces } from './traceSummary';

// ── Minimal fixture factory (mirrors tracesMeta.spec.ts) ──────────────────────

function trace(over: Partial<AgentCallListItemDto> & Pick<AgentCallListItemDto, 'id'>): AgentCallListItemDto {
  return {
    agentId: null,
    agentName: null,
    model: 'gpt-4o',
    provider: 'openai',
    messagePreview: null,
    toolCount: 0,
    inputTokens: 10,
    outputTokens: 5,
    cachedInputTokens: 0,
    durationMs: 200,
    httpStatus: 200,
    finishReason: 'stop',
    errorMessage: null,
    costEur: null,
    createdAt: new Date().toISOString(),
    updatedAt: new Date().toISOString(),
    conversationId: null,
    sessionId: null,
    outlierFlags: 0,
    ...over,
  };
}

describe('summarizeTraces', () => {
  it('returns a zeroed summary with null cost for an empty slice', () => {
    expect(summarizeTraces([])).toEqual({
      count: 0,
      inputTokens: 0,
      outputTokens: 0,
      cachedInputTokens: 0,
      totalCostEur: null,
      avgLatencyMs: 0,
      latencyStdDevMs: 0,
      errorCount: 0,
      errorRate: 0,
    });
  });

  it('sums input, output, and cached input tokens', () => {
    const s = summarizeTraces([
      trace({ id: 'a', inputTokens: 100, outputTokens: 20, cachedInputTokens: 60 }),
      trace({ id: 'b', inputTokens: 50, outputTokens: 5, cachedInputTokens: 10 }),
    ]);
    expect(s.count).toBe(2);
    expect(s.inputTokens).toBe(150);
    expect(s.outputTokens).toBe(25);
    expect(s.cachedInputTokens).toBe(70);
  });

  it('sums only non-null costs and keeps the total', () => {
    const s = summarizeTraces([
      trace({ id: 'a', costEur: 0.01 }),
      trace({ id: 'b', costEur: null }),
      trace({ id: 'c', costEur: 0.005 }),
    ]);
    expect(s.totalCostEur).toBeCloseTo(0.015, 10);
  });

  it('returns null cost when no trace has a cost', () => {
    const s = summarizeTraces([trace({ id: 'a' }), trace({ id: 'b' })]);
    expect(s.totalCostEur).toBeNull();
  });

  it('computes average latency and population std-dev', () => {
    const s = summarizeTraces([
      trace({ id: 'a', durationMs: 100 }),
      trace({ id: 'b', durationMs: 300 }),
    ]);
    expect(s.avgLatencyMs).toBe(200);
    expect(s.latencyStdDevMs).toBe(100); // sqrt(((100-200)^2 + (300-200)^2) / 2)
  });

  it('counts non-2xx statuses as errors and computes the rate', () => {
    const s = summarizeTraces([
      trace({ id: 'a', httpStatus: 200 }),
      trace({ id: 'b', httpStatus: 404 }),
      trace({ id: 'c', httpStatus: 500 }),
      trace({ id: 'd', httpStatus: 299 }),
    ]);
    expect(s.errorCount).toBe(2);
    expect(s.errorRate).toBe(0.5);
  });

  it('treats 1xx and 3xx as errors (non-2xx)', () => {
    const s = summarizeTraces([
      trace({ id: 'a', httpStatus: 100 }),
      trace({ id: 'b', httpStatus: 302 }),
    ]);
    expect(s.errorCount).toBe(2);
    expect(s.errorRate).toBe(1);
  });
});
