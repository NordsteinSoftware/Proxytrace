/**
 * Agent-related hooks for the Playground page.
 *
 * Wraps the agent queries and all effects that depend on them:
 *  - clearing a stale/404 agent
 *  - auto-selecting the first agent when none is picked
 *  - auto-loading the last trace when a fresh agent is selected
 *  - consuming the ?agentId= search param to deep-link into an agent
 */
import { useEffect, useRef } from 'react';
import { useQuery } from '@tanstack/react-query';
import { useSearchParams } from 'react-router';
import { agentsApi } from '../../../api/agents';
import { agentCallsApi } from '../../../api/agent-calls';
import { QUERY_KEYS } from '../../../api/query-keys';
import type { AgentDto } from '../../../api/models';
import { agentCallToMessages } from '../playgroundMeta';
import type { PlaygroundMessage } from '../state/types';

// Re-export for callers so they don't need a second import.
export { overridesFromAgent } from '../state/usePlaygroundSession';

// The reducer dispatch type — mirrors the Action union from usePlaygroundSession.
type SessionDispatch = ReturnType<typeof import('../state/usePlaygroundSession').usePlaygroundSession>['dispatch'];

/**
 * Fetches the full agent by id (the agents list is light and has no system message / tools /
 * parameters) and dispatches `pickAgent`, which seeds the session overrides from the agent's
 * defaults. Used by every pick path: the picker dropdown and the auto-pick of the first agent.
 */
export function fetchAndPickAgent(agentId: string, dispatch: SessionDispatch): void {
  void agentsApi.get(agentId)
    .then(a => dispatch({ type: 'pickAgent', agent: a }))
    .catch(() => { /* ignore — a stale id simply leaves the current selection */ });
}

// ─────────────────────────────────────────────────────────────────────────────
// usePlaygroundAgent — single-agent query + stale-agent clear
// ─────────────────────────────────────────────────────────────────────────────

interface UsePlaygroundAgentOptions {
  agentId: string | null;
  dispatch: SessionDispatch;
}

interface UsePlaygroundAgentResult {
  agent: AgentDto | null | undefined;
}

/**
 * Fetches the currently-selected agent and clears it from the session when it
 * returns 404 or errors.
 */
export function usePlaygroundAgent({
  agentId,
  dispatch,
}: UsePlaygroundAgentOptions): UsePlaygroundAgentResult {
  const { data: agent, error: agentError } = useQuery({
    queryKey: QUERY_KEYS.agent(agentId),
    queryFn: async () => {
      try {
        return await agentsApi.get(agentId ?? '');
      } catch (e) {
        if (e instanceof Error && e.message.startsWith('404')) return null;
        throw e;
      }
    },
    enabled: !!agentId,
    throwOnError: false,
  });

  // Effect 1 resolution: responds to query data (agent 404/error) by dispatching
  // clearAgent. Cannot be replaced by a query select because the side-effect is a
  // reducer dispatch, not a cached value. Kept minimal (1 dispatch).
  useEffect(() => {
    if (agentId && (agent === null || agentError)) {
      dispatch({ type: 'clearAgent' });
    }
  }, [agentId, agent, agentError, dispatch]);

  return { agent: agent ?? null };
}

// ─────────────────────────────────────────────────────────────────────────────
// usePlaygroundAgentList — agents list query + auto-pick first agent
// ─────────────────────────────────────────────────────────────────────────────

interface UsePlaygroundAgentListOptions {
  projectId: string | undefined;
  agentId: string | null;
  dispatch: SessionDispatch;
}

/**
 * Fetches all agents for the current project and auto-selects the first one
 * when no agent is currently selected.
 */
export function usePlaygroundAgentList({
  projectId,
  agentId,
  dispatch,
}: UsePlaygroundAgentListOptions) {
  const { data: agentsList } = useQuery({
    queryKey: QUERY_KEYS.agents(projectId),
    queryFn: () => agentsApi.list({ projectId: projectId ?? '', pageSize: 200 }),
    enabled: !!projectId,
  });

  // Effect 2 resolution: auto-pick first agent. Cannot be converted to a TanStack
  // select because the side-effect is a reducer dispatch. Kept minimal.
  useEffect(() => {
    if (agentId) return;
    const first = agentsList?.items?.[0];
    if (first) fetchAndPickAgent(first.id, dispatch);
  }, [agentId, agentsList, dispatch]);

  return { agentsList };
}

// ─────────────────────────────────────────────────────────────────────────────
// useAutoLoadAgentCall — seed conversation from last trace on first open
// ─────────────────────────────────────────────────────────────────────────────

interface UseAutoLoadAgentCallOptions {
  agentId: string | null;
  agent: AgentDto | null | undefined;
  messages: PlaygroundMessage[];
  dispatch: SessionDispatch;
}

/**
 * When a new agent is selected and the conversation is empty, auto-loads the
 * most recent agent call to seed the conversation with realistic messages.
 *
 * This is a genuine external side-effect: the result flows into the reducer, not
 * query cache. Uses a ref to avoid re-loading the same agent twice.
 */
export function useAutoLoadAgentCall({
  agentId,
  agent,
  messages,
  dispatch,
}: UseAutoLoadAgentCallOptions) {
  const autoLoadedRef = useRef<string | null>(null);

  // Effect 3 resolution: legitimate external async fetch with cancellation.
  // Cannot be TanStack Query because data goes to reducer, not cache.
  useEffect(() => {
    if (!agentId || !agent) return;
    if (autoLoadedRef.current === agentId) return;
    if (messages.length > 0) {
      autoLoadedRef.current = agentId;
      return;
    }
    let cancelled = false;
    autoLoadedRef.current = agentId;
    agentCallsApi
      .listFull({ agentId, pageSize: 1, includeSystemAgents: true })
      .then(res => {
        if (cancelled) return;
        const call = res.items[0];
        if (!call) return;
        dispatch({ type: 'setMessages', messages: agentCallToMessages(call) });
      })
      .catch(() => { /* ignore */ });
    return () => { cancelled = true; };
  }, [agentId, agent, messages.length, dispatch]);
}

// ─────────────────────────────────────────────────────────────────────────────
// useAgentFromSearchParam — deep-link via ?agentId= URL param
// ─────────────────────────────────────────────────────────────────────────────

interface UseAgentFromSearchParamOptions {
  agentId: string | null;
  dispatch: SessionDispatch;
}

/**
 * Reads the `?agentId=` search param on mount / navigation, fetches that agent,
 * dispatches pickAgent, then clears the param so the URL stays clean.
 *
 * This is a genuine external effect (router/URL sync) with no Query equivalent.
 */
export function useAgentFromSearchParam({
  agentId,
  dispatch,
}: UseAgentFromSearchParamOptions) {
  const [searchParams, setSearchParams] = useSearchParams();
  const requestedAgentId = searchParams.get('agentId');

  // Effect 4 resolution: URL param sync. Kept as effect — it reads external router
  // state and performs a one-shot fetch + dispatch to hydrate the session.
  useEffect(() => {
    if (!requestedAgentId) return;
    if (agentId === requestedAgentId) {
      setSearchParams({}, { replace: true });
      return;
    }
    let cancelled = false;
    agentsApi.get(requestedAgentId).then(a => {
      if (cancelled) return;
      dispatch({ type: 'pickAgent', agent: a });
      setSearchParams({}, { replace: true });
    }).catch(() => {
      if (!cancelled) setSearchParams({}, { replace: true });
    });
    return () => { cancelled = true; };
  }, [requestedAgentId, agentId, dispatch, setSearchParams]);
}
