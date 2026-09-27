// Pure helpers for deriving display bits from a captured trace, plus conversation
// grouping shared by the Traces tab and the dashboard live stream. No JSX, no I/O.
// Operates on the light AgentCallListItemDto — the list shape — so list rows never
// need the fat AgentCallDto (full request/response/tools).

import type { AgentCallListItemDto } from '../api/models';

/**
 * Preview text for a trace row: the latest user message in the request, with collapsed whitespace
 * (precomputed by the backend into {@link AgentCallListItemDto.messagePreview}). Null when the
 * request had no user message — including the empty-string marker the preview backfill writes for
 * such rows — so callers render an em-dash placeholder.
 */
export function tracePreview(call: AgentCallListItemDto): string | null {
  return call.messagePreview || null;
}

// ── Conversation grouping ──────────────────────────────────────────────────────

export type ConversationGroup = {
  type: 'conversation';
  conversationId: string;
  turns: AgentCallListItemDto[];
};
export type FlatTrace = { type: 'flat'; trace: AgentCallListItemDto };
export type TraceRow = ConversationGroup | FlatTrace;

/**
 * Groups traces that share a conversationId (multi-turn) into ConversationGroup rows;
 * traces with no conversationId, or whose conversationId appears only once, become FlatTrace rows.
 * Order is preserved from the input array.
 */
export function buildRows(traces: AgentCallListItemDto[]): TraceRow[] {
  const groups = new Map<string, AgentCallListItemDto[]>();
  for (const t of traces) {
    if (t.conversationId) {
      const g = groups.get(t.conversationId) ?? [];
      g.push(t);
      groups.set(t.conversationId, g);
    }
  }
  const multi = new Set(
    [...groups.entries()].filter(([, v]) => v.length > 1).map(([k]) => k),
  );

  const rows: TraceRow[] = [];
  const emitted = new Set<string>();
  for (const t of traces) {
    if (t.conversationId && multi.has(t.conversationId)) {
      if (!emitted.has(t.conversationId)) {
        emitted.add(t.conversationId);
        rows.push({
          type: 'conversation',
          conversationId: t.conversationId,
          turns: groups.get(t.conversationId) ?? [],
        });
      }
    } else {
      rows.push({ type: 'flat', trace: t });
    }
  }
  return rows;
}
