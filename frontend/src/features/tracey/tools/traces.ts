import { z } from 'zod';
import { agentCallsApi } from '../../../api/agent-calls';
import { outlierFlagKeys, type OutlierFlagKey } from '../../../lib/outliers';
import { type ToolFactory, tool, ignore404, isEntityId, presentArg, includeSystemArg } from './shared';
import { clip } from './run-analysis';
import { traceTranscript } from './trace-transcript';

export const createTraceTools: ToolFactory = (ctx, store) => ({
  find_traces: tool({
    description:
      'Search the captured traces (real LLM calls) of this project — by agent, free-text query, ' +
      'or HTTP status — newest first. Use it to ground a tuning hypothesis in what the agent ' +
      'actually said: find failing or suspicious calls, then `get_trace` one with `verbose: true` ' +
      'to read its whole conversation. ' +
      'The matching traces are rendered to the user as a card. Hides traces of internal system ' +
      'agents (Tracey, evaluators) unless includeSystem is true. `query` searches message CONTENT, ' +
      'not ids — to open a trace whose id you already have, call `get_trace` instead. ' +
      'ONE agent turn that uses tools is SEVERAL rows: rows sharing a `conversationId` are the ' +
      'successive calls of one tool loop, oldest `createdAt` first, each capturing the conversation ' +
      'as it stood. `toolCallsRequested` says what that call decided to do; the final row has 0 ' +
      'because all it wrote was the closing summary. When a turn misbehaved, the row that MADE the ' +
      'wrong choice is an earlier one — read the loop with `get_trace` before acting on any of it.',
    parameters: z.object({
      present: presentArg,
      agentId: z.string().optional().describe('Only traces of this agent.'),
      query: z.string().optional().describe('Free-text search over the captured request/response.'),
      httpStatus: z.number().int().optional()
        .describe('Only calls with this exact upstream HTTP status (e.g. 500 for errors).'),
      limit: z.number().int().min(1).max(20).optional().describe('Max traces to return (default 10).'),
      includeSystem: includeSystemArg,
    }),
    confirm: false,
    execute: async ({ agentId, query, httpStatus, limit, includeSystem }) => {
      // A user who says "look at trace <guid>" hands over a real id, and the model sometimes routes
      // it here instead of to `get_trace`. The backend `q` is a fulltext index over the captured
      // request/response, so an id matches nothing — an empty result the model reads as "that trace
      // does not exist" rather than "wrong tool". Name the right tool instead of failing silently.
      if (query && isEntityId(query)) {
        return { count: 0, items: [], useInstead: { tool: 'get_trace', traceId: query.trim() } };
      }
      const { items } = await agentCallsApi.list({
        projectId: ctx.projectId,
        agentId,
        q: query,
        httpStatus,
        // The backend defaults this to true; pass false explicitly so system-agent traces stay hidden.
        includeSystemAgents: includeSystem ?? false,
        pageSize: limit ?? 10,
      });
      return store('trace-list', items, {
        count: items.length,
        items: items.map((t) => ({
          id: t.id,
          agentName: t.agentName,
          model: t.model,
          httpStatus: t.httpStatus,
          ...(t.errorMessage ? { error: clip(t.errorMessage, 120) } : {}),
          durationMs: t.durationMs,
          tokens: t.inputTokens + t.outputTokens,
          preview: t.messagePreview ? clip(t.messagePreview, 100) : null,
          // The two fields that make a tool loop legible. `preview` is the FIRST user message, so
          // every call of one turn previews identically — without the conversation id and the
          // per-call tool-call count the rows are indistinguishable, and "the newest one" silently
          // means "the closing summary".
          conversationId: t.conversationId,
          toolCallsRequested: t.toolCount,
          createdAt: t.createdAt,
        })),
      });
    },
  }),
  get_agent_anomalies: tool({
    description:
      'Get the recent calls of ONE agent that were auto-flagged as statistical anomalies (outliers) ' +
      'at ingestion — each call sits far outside the agent\'s own recent baseline (mean ± sigma). ' +
      'Flag reasons: HighTokens (token count / cost spike), HighLatency, LowCacheHit (prompt-cache ' +
      'hit rate collapsed), ManyToolCalls (tool-call loop). Use this to diagnose what is wrong with ' +
      'an agent, then `get_trace` a few flagged calls with `verbose: true` to read them in full. ' +
      'The matching calls are ' +
      'rendered to the user as a card with the flagged reasons.',
    parameters: z.object({
      present: presentArg,
      agentId: z.string().describe('The id of the agent whose flagged calls to fetch (from list_agents).'),
      limit: z.number().int().min(1).max(20).optional().describe('Max flagged calls to return (default 10).'),
    }),
    confirm: false,
    execute: async ({ agentId, limit }) => {
      if (!isEntityId(agentId)) return { notFound: agentId };
      // An explicit agent id already scopes the read, so include system agents like the
      // agent-detail outliers widget does — the id may name one.
      const { items } = await agentCallsApi.list({
        projectId: ctx.projectId,
        agentId,
        outlierOnly: true,
        includeSystemAgents: true,
        pageSize: limit ?? 10,
      });
      const byReason: Partial<Record<OutlierFlagKey, number>> = {};
      for (const item of items) {
        for (const key of outlierFlagKeys(item.outlierFlags)) {
          byReason[key] = (byReason[key] ?? 0) + 1;
        }
      }
      return store('trace-list', items, {
        agentId,
        count: items.length,
        byReason,
        items: items.map((t) => ({
          id: t.id,
          reasons: outlierFlagKeys(t.outlierFlags),
          tokens: t.inputTokens + t.outputTokens,
          cachedInputTokens: t.cachedInputTokens,
          toolCount: t.toolCount,
          durationMs: t.durationMs,
          httpStatus: t.httpStatus,
          preview: t.messagePreview ? clip(t.messagePreview, 100) : null,
          // Calls sharing a conversationId are one tool loop — ManyToolCalls in particular flags a
          // loop, so without this the same turn reads as several unrelated anomalies.
          conversationId: t.conversationId,
          createdAt: t.createdAt,
        })),
      });
    },
  }),
  get_trace: tool({
    description:
      'Get a single captured trace (agent call) by id. By default returns only a metadata summary ' +
      '(model, status, token usage, latency, cost) — enough to describe the call, NOT to read it. ' +
      'Set `verbose: true` to also get the WHOLE conversation: every request message (system, user, ' +
      'assistant, tool results) with its tool calls, the full response, the tool schema the agent ' +
      'was offered, and the model parameters. Use verbose whenever you need to reason about what ' +
      'the agent actually said or did. Either way the full trace is rendered to the user as a card.',
    parameters: z.object({
      present: presentArg,
      traceId: z.string().describe('The id of the trace / agent call to fetch.'),
      verbose: z.boolean().optional().describe(
        'Return the complete trace — the whole conversation (all messages + tool calls), the ' +
        'response, the tool schema and model parameters — instead of the metadata summary. ' +
        'Set true whenever the CONTENT of the call matters (diagnosing a failure, quoting the ' +
        'prompt, building a test case); it costs context, so leave it off for a metadata glance.',
      ),
    }),
    confirm: false,
    execute: async ({ traceId, verbose }) => {
      const call = await ignore404(() => agentCallsApi.get(traceId, { silentStatuses: [404] }));
      if (!call) return { notFound: traceId };
      // The card resolves the same stored artifact either way — `verbose` only widens what the
      // model itself receives, from metadata to the full transcript.
      return store('trace', call, verbose ? traceTranscript(call) : {
        id: call.id,
        // The agent this call belongs to, by id — a pasted trace is often the only thing the user
        // gives you, and a name would have to be matched back through list_agents to be usable.
        agentId: call.agentId,
        agentName: call.agentName,
        model: call.model,
        provider: call.provider,
        httpStatus: call.httpStatus,
        inputTokens: call.inputTokens,
        outputTokens: call.outputTokens,
        durationMs: call.durationMs,
        costEur: call.costEur,
      });
    },
  }),
});
