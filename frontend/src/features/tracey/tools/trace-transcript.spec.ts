import { describe, expect, it } from 'vitest';
import type { AgentCallDto, MessageDto } from '../../../api/models';
import { MESSAGE_CHAR_MAX, TRANSCRIPT_CHAR_BUDGET, fairShareCap, traceTranscript } from './trace-transcript';

function message(role: string, content: string, extra: Partial<MessageDto> = {}): MessageDto {
  return { role, content, toolRequests: [], toolCallId: null, ...extra };
}

function call(overrides: Partial<AgentCallDto> = {}): AgentCallDto {
  return {
    id: 't1',
    agentId: 'a1',
    agentName: 'Returns',
    model: 'gpt-4o',
    provider: 'openai',
    request: [message('system', 'You are helpful.'), message('user', 'Refund order 42?')],
    response: message('assistant', 'Yes — refunded.'),
    tools: [],
    inputTokens: 120,
    outputTokens: 30,
    cachedInputTokens: 60,
    durationMs: 900,
    httpStatus: 200,
    finishReason: 'stop',
    errorMessage: null,
    costEur: 0.002,
    modelParameters: {
      temperature: 0.2, topP: null, reasoningEffort: null,
      frequencyPenalty: null, presencePenalty: null, maxTokens: 512, seed: null,
    },
    createdAt: '2026-06-01T00:00:00Z',
    updatedAt: '2026-06-01T00:00:00Z',
    conversationId: null,
    sessionId: null,
    outlierFlags: 0,
    ...overrides,
  } as AgentCallDto;
}

describe('fairShareCap', () => {
  it('returns Infinity when everything fits the budget', () => {
    expect(fairShareCap([10, 20, 30], 100)).toBe(Infinity);
    expect(fairShareCap([], 100)).toBe(Infinity);
  });

  it('clips only the outsized strings, leaving the small ones intact', () => {
    // 3 strings, budget 100: the two short ones fit whole (15), so the huge one keeps 85.
    expect(fairShareCap([5, 10, 5000], 100)).toBe(85);
  });

  it('splits the budget evenly when every string is oversized', () => {
    expect(fairShareCap([500, 600, 700], 90)).toBe(30);
  });

  it('never lets one giant string starve the rest', () => {
    const cap = fairShareCap([1_000_000, 40, 40], 200);
    expect(cap).toBe(120);
    expect(Math.min(1_000_000, cap) + 40 + 40).toBeLessThanOrEqual(200);
  });
});

describe('traceTranscript', () => {
  it('returns the whole conversation — every request message and the response', () => {
    const transcript = traceTranscript(call());

    expect(transcript.messageCount).toBe(2);
    expect(transcript.messages).toEqual([
      { role: 'system', content: 'You are helpful.' },
      { role: 'user', content: 'Refund order 42?' },
    ]);
    expect(transcript.response).toEqual({ role: 'assistant', content: 'Yes — refunded.' });
    expect(transcript.note).toBeUndefined();
  });

  it('carries the metadata the summary digest has, plus cached tokens and finish reason', () => {
    const transcript = traceTranscript(call());

    expect(transcript).toMatchObject({
      id: 't1', agentName: 'Returns', model: 'gpt-4o', provider: 'openai', httpStatus: 200,
      inputTokens: 120, cachedInputTokens: 60, outputTokens: 30, durationMs: 900,
      costEur: 0.002, finishReason: 'stop',
    });
  });

  it('keeps tool requests and tool-result correlation ids', () => {
    const transcript = traceTranscript(call({
      request: [
        message('user', 'Refund order 42?'),
        message('assistant', '', { toolRequests: [{ id: 'c1', name: 'refund', arguments: '{"order":42}' }] }),
        message('tool', '{"ok":true}', { toolCallId: 'c1' }),
      ],
    }));

    expect(transcript.messages[1].toolRequests)
      .toEqual([{ id: 'c1', name: 'refund', arguments: '{"order":42}' }]);
    expect(transcript.messages[2].toolCallId).toBe('c1');
  });

  it('includes the tool schema the agent was offered', () => {
    const transcript = traceTranscript(call({
      tools: [{
        name: 'refund',
        description: 'Refund an order.',
        arguments: [
          { name: 'order', description: null, type: 'number', isRequired: true, enumValues: null },
          { name: 'reason', description: null, type: 'string', isRequired: false, enumValues: ['damaged', 'late'] },
        ],
      }],
    }));

    expect(transcript.tools).toEqual([{
      name: 'refund',
      description: 'Refund an order.',
      arguments: [
        { name: 'order', type: 'number', required: true },
        { name: 'reason', type: 'string', required: false, enumValues: ['damaged', 'late'] },
      ],
    }]);
  });

  it('keeps only the model parameters that were actually set, and drops empty optionals', () => {
    const transcript = traceTranscript(call());

    expect(transcript.modelParameters).toEqual({ temperature: 0.2, maxTokens: 512 });
    expect(transcript).not.toHaveProperty('tools');
    expect(transcript).not.toHaveProperty('errorMessage');
    expect(transcript).not.toHaveProperty('outlierReasons');
  });

  it('surfaces the error, correlation ids and decoded outlier reasons when present', () => {
    const transcript = traceTranscript(call({
      httpStatus: 500,
      errorMessage: 'upstream timeout',
      conversationId: 'conv-1',
      sessionId: 'sess-1',
      outlierFlags: 3, // HighTokens | HighLatency
      response: null,
    }));

    expect(transcript).toMatchObject({
      errorMessage: 'upstream timeout', conversationId: 'conv-1', sessionId: 'sess-1',
      outlierReasons: ['HighTokens', 'HighLatency'],
    });
    expect(transcript.response).toBeNull();
  });

  it('clips an outsized message to the per-message maximum, keeps every message, and says so', () => {
    const huge = 'x'.repeat(MESSAGE_CHAR_MAX * 3);
    const transcript = traceTranscript(call({
      request: [message('system', 'short'), message('tool', huge)],
    }));

    expect(transcript.messages).toHaveLength(2);
    expect(transcript.messages[0].content).toBe('short'); // the small message survives whole
    expect(transcript.messages[1].content).toBe(`${'x'.repeat(MESSAGE_CHAR_MAX)}…`);
    expect(transcript.note).toContain('clipped');
  });

  it('caps a single message even when the conversation is well within the total budget', () => {
    // One message, far below TRANSCRIPT_CHAR_BUDGET overall — the fair share never bites, so only
    // the per-message ceiling stops it from entering the context whole.
    const long = 'y'.repeat(MESSAGE_CHAR_MAX + 5_000);
    expect(long.length).toBeLessThan(TRANSCRIPT_CHAR_BUDGET);
    const transcript = traceTranscript(call({ request: [message('user', long)], response: null }));

    expect(transcript.messages[0].content).toBe(`${'y'.repeat(MESSAGE_CHAR_MAX)}…`);
    expect(transcript.note).toContain('clipped');
  });

  it('clips oversized tool-call arguments the same way', () => {
    const args = `{"doc":"${'z'.repeat(MESSAGE_CHAR_MAX * 2)}"}`;
    const transcript = traceTranscript(call({
      request: [message('assistant', '', { toolRequests: [{ id: 'c1', name: 'index', arguments: args }] })],
      response: null,
    }));

    expect(transcript.messages[0].toolRequests?.[0].arguments.length).toBe(MESSAGE_CHAR_MAX + 1);
    expect(transcript.note).toContain('clipped');
  });

  it('leaves a conversation that fits both ceilings completely untouched', () => {
    const transcript = traceTranscript(call({
      request: Array.from({ length: 40 }, (_, i) => message('user', 'a'.repeat(1_000) + i)),
    }));

    expect(transcript.messages.every((m) => m.content.length >= 1_000)).toBe(true);
    expect(transcript.messages.some((m) => m.content.endsWith('…'))).toBe(false);
    expect(transcript.note).toBeUndefined();
  });
});
