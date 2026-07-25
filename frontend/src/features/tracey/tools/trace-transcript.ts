// Pure builder for `get_trace`'s verbose digest — the WHOLE captured call (every request message,
// the response, the tool schema the agent was offered, and the model parameters) flattened into a
// compact model-facing shape. The default digest is metadata only, which is enough to *describe* a
// call but not to reason about what the agent actually said; `verbose: true` routes through here.
// No JSX, no I/O — unit-tested in trace-transcript.spec.ts.

import type { AgentCallDto, MessageDto, ToolSpecDto } from '../../../api/models';
import { outlierFlagKeys, type OutlierFlagKey } from '../../../lib/outliers';
import { clip } from './run-analysis';

/**
 * Total characters of conversation text a verbose trace may push into the model context
 * (~25k tokens). A captured call has no size limit — a long agent conversation or a tool result
 * carrying a whole document would otherwise blow the turn's context budget in a single read.
 */
export const TRANSCRIPT_CHAR_BUDGET = 100_000;

/**
 * Hard per-message ceiling (~5k tokens), applied on top of {@link TRANSCRIPT_CHAR_BUDGET}. The
 * fair share alone only bites once the whole conversation busts the budget, so a call with one
 * enormous message and nothing else would still hand the model 100k characters of it. Any single
 * message longer than this is padding, a dumped document, or a runaway tool result — the model
 * needs enough to recognize it, not all of it.
 */
export const MESSAGE_CHAR_MAX = 20_000;

/** Max characters kept per tool description in the verbose tool schema. */
const TOOL_DESCRIPTION_MAX = 400;

export interface TranscriptToolRequest {
  id: string;
  name: string;
  arguments: string;
}

export interface TranscriptMessage {
  role: string;
  content: string;
  /** Set on a tool-result message: the tool request it answers. */
  toolCallId?: string;
  /** Set on an assistant message that requested tools. */
  toolRequests?: TranscriptToolRequest[];
}

export interface TranscriptToolSpec {
  name: string;
  description: string;
  arguments: { name: string; type: string; required: boolean; enumValues?: string[] }[];
}

export interface TraceTranscript {
  id: string;
  agentName: string | null;
  model: string;
  provider: string;
  httpStatus: number;
  inputTokens: number;
  cachedInputTokens: number;
  outputTokens: number;
  durationMs: number;
  costEur: number | null;
  finishReason: string | null;
  createdAt: string;
  errorMessage?: string;
  conversationId?: string;
  sessionId?: string;
  outlierReasons?: OutlierFlagKey[];
  modelParameters?: Record<string, string | number>;
  tools?: TranscriptToolSpec[];
  messageCount: number;
  messages: TranscriptMessage[];
  response: TranscriptMessage | null;
  /** Present only when the budget forced a clip — tells the model what it is missing. */
  note?: string;
}

/**
 * Largest per-string length that keeps `sum(min(length, cap))` within `budget` — the classic
 * fair-share (water-filling) split. Short messages survive intact and only the outsized ones are
 * clipped, so a single 200k-character tool result can't starve the rest of the conversation.
 * Returns `Infinity` when everything fits.
 */
export function fairShareCap(lengths: number[], budget: number): number {
  const total = lengths.reduce((sum, length) => sum + length, 0);
  if (total <= budget) return Infinity;

  let remaining = budget;
  let unresolved = lengths.length;
  for (const length of [...lengths].sort((a, b) => a - b)) {
    const share = remaining / unresolved;
    if (length > share) return Math.floor(share);
    remaining -= length;
    unresolved -= 1;
  }
  return Infinity; // unreachable: total > budget means at least one length exceeds its share
}

/** Every free-text string of the conversation, in the order the budget is shared across them. */
function conversationStrings(call: AgentCallDto): string[] {
  const messages = [...(call.request ?? []), ...(call.response ? [call.response] : [])];
  return messages.flatMap((message) => [
    message.content ?? '',
    ...(message.toolRequests ?? []).map((request) => request.arguments ?? ''),
  ]);
}

function toolSpec(spec: ToolSpecDto): TranscriptToolSpec {
  return {
    name: spec.name,
    description: clip(spec.description ?? '', TOOL_DESCRIPTION_MAX),
    arguments: (spec.arguments ?? []).map((argument) => ({
      name: argument.name,
      type: argument.type,
      required: argument.isRequired,
      ...(argument.enumValues?.length ? { enumValues: argument.enumValues } : {}),
    })),
  };
}

/** The model parameters that were actually set on the call (nulls carry no information). */
function setParameters(call: AgentCallDto): Record<string, string | number> {
  const entries = Object.entries(call.modelParameters ?? {})
    .filter((entry): entry is [string, string | number] => entry[1] != null);
  return Object.fromEntries(entries);
}

/**
 * Build the full model-facing transcript of one captured call. Every message is kept — only
 * outsized text is clipped (and then flagged in `note`), so the model always sees the true shape
 * of the conversation.
 */
export function traceTranscript(call: AgentCallDto): TraceTranscript {
  // Two ceilings: the per-message maximum always applies, the fair share tightens it further when
  // the conversation as a whole is over budget.
  const cap = Math.min(
    MESSAGE_CHAR_MAX,
    fairShareCap(conversationStrings(call).map((value) => value.trim().length), TRANSCRIPT_CHAR_BUDGET),
  );
  let clipped = false;
  const take = (value: string | null | undefined): string => {
    const text = value ?? '';
    if (text.trim().length > cap) clipped = true;
    return clip(text, cap);
  };

  const message = (source: MessageDto): TranscriptMessage => ({
    role: source.role,
    content: take(source.content),
    ...(source.toolCallId ? { toolCallId: source.toolCallId } : {}),
    ...(source.toolRequests?.length
      ? {
        toolRequests: source.toolRequests.map((request) => ({
          id: request.id,
          name: request.name,
          arguments: take(request.arguments),
        })),
      }
      : {}),
  });

  const request = call.request ?? [];
  const parameters = setParameters(call);
  const reasons = outlierFlagKeys(call.outlierFlags);

  return {
    id: call.id,
    agentName: call.agentName,
    model: call.model,
    provider: call.provider,
    httpStatus: call.httpStatus,
    inputTokens: call.inputTokens,
    cachedInputTokens: call.cachedInputTokens,
    outputTokens: call.outputTokens,
    durationMs: call.durationMs,
    costEur: call.costEur,
    finishReason: call.finishReason,
    createdAt: call.createdAt,
    ...(call.errorMessage ? { errorMessage: call.errorMessage } : {}),
    ...(call.conversationId ? { conversationId: call.conversationId } : {}),
    ...(call.sessionId ? { sessionId: call.sessionId } : {}),
    ...(reasons.length ? { outlierReasons: reasons } : {}),
    ...(Object.keys(parameters).length ? { modelParameters: parameters } : {}),
    ...(call.tools?.length ? { tools: call.tools.map(toolSpec) } : {}),
    messageCount: request.length,
    messages: request.map(message),
    response: call.response ? message(call.response) : null,
    ...(clipped
      ? {
        note: `Oversized message text was clipped to ${cap} characters to fit the context budget; `
          + "the user's trace card shows the untouched original.",
      }
      : {}),
  };
}
