import { z } from 'zod';
import { agentCallsApi } from '../../../api/agent-calls';
import { outlierFlagKeys, type OutlierFlagKey } from '../../../lib/outliers';
import { type ToolFactory, tool, ignore404, isEntityId, presentArg, includeSystemArg } from './shared';
import { clip } from './run-analysis';

export const createTraceTools: ToolFactory = (ctx, store) => ({
  find_traces: tool({
    description:
      'Search the captured traces (real LLM calls) of this project — by agent, free-text query, ' +
      'or HTTP status — newest first. Use it to ground a tuning hypothesis in what the agent ' +
      'actually said: find failing or suspicious calls, then `get_trace` one for full detail. ' +
      'The matching traces are rendered to the user as a card. Hides traces of internal system ' +
      'agents (Tracey, evaluators) unless includeSystem is true. `query` searches message CONTENT, ' +
      'not ids — to open a trace whose id you already have, call `get_trace` instead.',
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
      'an agent, then `get_trace` a few flagged calls for full detail. The matching calls are ' +
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
          createdAt: t.createdAt,
        })),
      });
    },
  }),
  get_trace: tool({
    description:
      'Get a single captured trace (agent call) by id. Returns a curated summary (model, status, ' +
      'token usage, latency, cost) plus a reference; the full trace is rendered to the user as a card.',
    parameters: z.object({ present: presentArg, traceId: z.string().describe('The id of the trace / agent call to fetch.') }),
    confirm: false,
    execute: async ({ traceId }) => {
      const call = await ignore404(() => agentCallsApi.get(traceId, { silentStatuses: [404] }));
      if (!call) return { notFound: traceId };
      return store('trace', call, {
        id: call.id,
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
