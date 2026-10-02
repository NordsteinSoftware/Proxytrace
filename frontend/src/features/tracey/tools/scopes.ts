import { z } from 'zod';
import { scopesApi } from '../../../api/scopes';
import { type ToolFactory, tool } from './shared';

export const createScopeTools: ToolFactory = (ctx) => ({
  list_scopes: tool({
    description:
      'List the scopes of this project. A scope is a use-case group of agents — e.g. ' +
      '"support-agents" — that clients name in the proxy URL (`/{project}/{scope}/openai/v1`) or the ' +
      '`x-proxytrace-scope` header. Each row has the scope id (pass it as `scopeId` to `find_traces` ' +
      'or `get_dashboard_stats` to look at one use case), its key, display name, trace and token ' +
      'counts within retention, and the ids of its member agents. An agent can be in several scopes.',
    parameters: z.object({}),
    confirm: false,
    execute: async () => {
      if (!ctx.projectId) return { count: 0, items: [] };
      const scopes = await scopesApi.list(ctx.projectId);
      return {
        count: scopes.length,
        items: scopes.map((s) => ({
          id: s.id,
          key: s.key,
          name: s.displayName ?? s.key,
          traces: s.traceCount,
          tokens: s.totalTokens,
          agentIds: s.agentIds,
          lastActivityAt: s.lastActivityAt,
        })),
      };
    },
  }),
});
