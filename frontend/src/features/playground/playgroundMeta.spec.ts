import { describe, it, expect } from 'vitest';
import { roleFromString, agentCallToMessages, toPayloadMessage } from './playgroundMeta';
import type { AgentCallDto, MessageDto, ToolRequestDto } from '../../api/models';

// ─── Helpers ───────────────────────────────────────────────────────────────

const stubToolRequest: ToolRequestDto = {
  id: 'tr1',
  name: 'get_weather',
  arguments: '{"city":"Berlin"}',
};

function stubMessage(role: string, content: string, extras: Partial<MessageDto> = {}): MessageDto {
  return { role, content, toolRequests: [], toolCallId: null, ...extras };
}

function stubAgentCall(request: MessageDto[], response?: MessageDto): AgentCallDto {
  return {
    id: 'call1',
    agentId: 'agent1',
    agentName: 'Test Agent',
    model: 'gpt-4',
    provider: 'openai',
    request,
    response: response ?? stubMessage('assistant', ''),
    tools: [],
    inputTokens: 100,
    outputTokens: 50,
    cachedInputTokens: 0,
    durationMs: 300,
    httpStatus: 200,
    finishReason: 'stop',
    errorMessage: null,
    costEur: null,
    modelParameters: {
      temperature: null, topP: null, reasoningEffort: null,
      frequencyPenalty: null, presencePenalty: null,
      maxTokens: null, seed: null, stop: null, n: null,
    },
    createdAt: new Date().toISOString(),
    updatedAt: new Date().toISOString(),
    conversationId: null,
    sessionId: null,
    outlierFlags: 0,
  };
}

// ─── Tests ─────────────────────────────────────────────────────────────────

describe('roleFromString', () => {
  it('maps known roles case-insensitively', () => {
    expect(roleFromString('user')).toBe('user');
    expect(roleFromString('User')).toBe('user');
    expect(roleFromString('ASSISTANT')).toBe('assistant');
    expect(roleFromString('System')).toBe('system');
    expect(roleFromString('TOOL')).toBe('tool');
  });

  it('falls back to user for unknown roles', () => {
    expect(roleFromString('unknown')).toBe('user');
    expect(roleFromString('')).toBe('user');
    expect(roleFromString('function')).toBe('user');
  });
});

describe('agentCallToMessages', () => {
  it('maps request messages to PlaygroundMessages', () => {
    const call = stubAgentCall([
      stubMessage('user', 'Hello'),
    ], stubMessage('assistant', 'Hi there'));
    const msgs = agentCallToMessages(call);
    // request (1) + response (1)
    expect(msgs).toHaveLength(2);
    expect(msgs[0].role).toBe('user');
    expect(msgs[0].content).toBe('Hello');
    expect(msgs[1].role).toBe('assistant');
    expect(msgs[1].content).toBe('Hi there');
  });

  it('maps tool requests onto message', () => {
    const msgWithTools = stubMessage('assistant', '', { toolRequests: [stubToolRequest] });
    const call = stubAgentCall([msgWithTools]);
    const msgs = agentCallToMessages(call);
    expect(msgs[0].toolRequests).toHaveLength(1);
    expect(msgs[0].toolRequests?.[0].name).toBe('get_weather');
  });

  it('maps toolCallId', () => {
    const toolMsg = stubMessage('tool', 'sunny', { toolCallId: 'tr1' });
    const call = stubAgentCall([toolMsg]);
    const msgs = agentCallToMessages(call);
    expect(msgs[0].toolCallId).toBe('tr1');
  });

  it('assigns unique localIds to each message', () => {
    const call = stubAgentCall(
      [stubMessage('user', 'A'), stubMessage('user', 'B')],
      stubMessage('assistant', 'ok'),
    );
    const msgs = agentCallToMessages(call);
    const ids = msgs.map(m => m.localId);
    expect(new Set(ids).size).toBe(ids.length);
  });
});

describe('toPayloadMessage', () => {
  it('converts a plain message to payload shape', () => {
    const payload = toPayloadMessage({
      localId: 'l1',
      role: 'user',
      content: 'Hello',
    });
    expect(payload.role).toBe('user');
    expect(payload.content).toBe('Hello');
    expect(payload.toolRequests).toEqual([]);
    expect(payload.toolCallId).toBeNull();
    expect(payload.toolSucceeded).toBe(true);
    expect(payload.toolError).toBeNull();
  });

  it('includes tool requests in payload', () => {
    const payload = toPayloadMessage({
      localId: 'l2',
      role: 'assistant',
      content: '',
      toolRequests: [{ id: 'tr1', name: 'fn', arguments: '{}' }],
    });
    expect(payload.toolRequests).toHaveLength(1);
    expect(payload.toolRequests[0].id).toBe('tr1');
  });

  it('forwards toolCallId, toolSucceeded, toolError', () => {
    const payload = toPayloadMessage({
      localId: 'l3',
      role: 'tool',
      content: 'result',
      toolCallId: 'tc1',
      toolSucceeded: false,
      toolError: 'timeout',
    });
    expect(payload.toolCallId).toBe('tc1');
    expect(payload.toolSucceeded).toBe(false);
    expect(payload.toolError).toBe('timeout');
  });
});
