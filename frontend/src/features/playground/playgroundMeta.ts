import { msg } from '@lingui/core/macro';
import type { MessageDescriptor } from '@lingui/core';
import type { AgentCallDto, MessageDto, ModelParametersDto } from '../../api/models';

// ─── Right rail section types ────────────────────────────────────────────────
export type SectionKey = 'system' | 'parameters' | 'tools';

export const SECTION_TITLES: Record<SectionKey, MessageDescriptor> = {
  system: msg`System Prompt`,
  parameters: msg`Parameters`,
  tools: msg`Tools`,
};
import type { PlaygroundMessagePayload, PlaygroundParametersPayload } from '../../api/playground';
import { makeMessage } from './state/usePlaygroundSession';
import type { PlaygroundMessage, PlaygroundRole, PlaygroundToolRequest } from './state/types';

export function roleFromString(role: string): PlaygroundRole {
  const lower = role.toLowerCase();
  if (lower === 'user' || lower === 'assistant' || lower === 'system' || lower === 'tool') return lower;
  return 'user';
}

export function agentCallToMessages(call: AgentCallDto): PlaygroundMessage[] {
  const toMsg = (m: MessageDto): PlaygroundMessage => {
    const base = makeMessage(roleFromString(m.role), m.content ?? '');
    if (m.toolRequests && m.toolRequests.length > 0) {
      base.toolRequests = m.toolRequests.map(tr => ({ id: tr.id, name: tr.name, arguments: tr.arguments }));
    }
    if (m.toolCallId) base.toolCallId = m.toolCallId;
    return base;
  };
  const out: PlaygroundMessage[] = call.request.map(toMsg);
  if (call.response) out.push(toMsg(call.response));
  return out;
}

/**
 * Drops choice count (`n`) from the session's parameters on the way out.
 *
 * The session seeds its parameters from the agent's stored ones, which share the shape used for a
 * captured trace and so can still carry an `n`. The playground has no way to render more than one
 * completion (see {@link PlaygroundParametersPayload}), so the value is not forwarded.
 */
export function toPayloadParameters(p: ModelParametersDto): PlaygroundParametersPayload {
  // Listed rather than spread-minus-`n`, so a field added to the shared shape has to be opted into
  // the playground request deliberately instead of leaking into it.
  return {
    temperature: p.temperature,
    topP: p.topP,
    reasoningEffort: p.reasoningEffort,
    frequencyPenalty: p.frequencyPenalty,
    presencePenalty: p.presencePenalty,
    maxTokens: p.maxTokens,
    seed: p.seed,
    stop: p.stop,
  };
}

export function toPayloadMessage(m: PlaygroundMessage): PlaygroundMessagePayload {
  return {
    role: m.role,
    content: m.content,
    toolRequests: (m.toolRequests ?? []).map((tr: PlaygroundToolRequest) => ({
      id: tr.id,
      name: tr.name,
      arguments: tr.arguments,
    })),
    toolCallId: m.toolCallId ?? null,
    toolSucceeded: m.toolSucceeded ?? true,
    toolError: m.toolError ?? null,
  };
}
