/**
 * Agent-related hooks for the Playground page.
 *
 * Wraps the agent queries and all effects that depend on them:
 *  - clearing a stale/404 agent
 *  - auto-selecting the first agent when none is picked
 *  - auto-loading the last trace when a fresh agent is selected
 *  - consuming the ?agentId= search param to deep-link into an agent
 */
import { useEffect, useMemo, useRef } from 'react';
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

/** The project's agent ids as far as the list query knows them, plus the server-side total. */
export interface KnownAgents {
  ids: readonly string[];
  total: number;
}

/**
 * What to do with the agent id restored from the persisted session.
 *
 * The session id is remembered across reloads *and across API restarts*, so it can name an agent
 * this instance no longer has — most visibly in the kiosk, which re-seeds into in-memory storage on
 * every boot and therefore mints fresh agent ids each time. Fetching such an id is a guaranteed
 * 404, so the selection is checked against the already-loaded agent list first and a dead id is
 * dropped without a request.
 *
 * Only a *complete* list can prove an id is gone: past the list query's page size, an id's absence
 * says nothing, so the fetch goes ahead and the 404 path below still cleans up.
 */
export function resolveStoredAgent(
  agentId: string | null,
  known: KnownAgents | undefined,
): 'wait' | 'fetch' | 'clear' {
  if (!agentId || known === undefined) return 'wait';
  if (known.ids.includes(agentId)) return 'fetch';
  return known.ids.length >= known.total ? 'clear' : 'fetch';
}

interface UsePlaygroundAgentOptions {
  agentId: string | null;
  /** The project's agents, once {@link usePlaygroundAgentList} has loaded them. */
  known: KnownAgents | undefined;
  dispatch: SessionDispatch;
}

interface UsePlaygroundAgentResult {
  agent: AgentDto | null | undefined;
}

/**
 * Fetches the currently-selected agent and clears it from the session when it is stale, returns
 * 404, or errors.
 */
export function usePlaygroundAgent({
  agentId,
  known,
  dispatch,
}: UsePlaygroundAgentOptions): UsePlaygroundAgentResult {
  const action = resolveStoredAgent(agentId, known);

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
    enabled: action === 'fetch',
    throwOnError: false,
  });

  // Effect 1 resolution: responds to query data (agent 404/error) and to a selection the list has
  // outlived by dispatching clearAgent. Cannot be replaced by a query select because the
  // side-effect is a reducer dispatch, not a cached value. Kept minimal (1 dispatch).
  useEffect(() => {
    if (action === 'clear' || (agentId && (agent === null || agentError))) {
      dispatch({ type: 'clearAgent' });
    }
  }, [action, agentId, agent, agentError, dispatch]);

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
 *
 * Also reports the loaded ids as {@link KnownAgents}, which {@link usePlaygroundAgent} uses to
 * recognise a stale stored selection before it asks the server for it.
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

  const known = useMemo<KnownAgents | undefined>(
    () => (agentsList ? { ids: agentsList.items.map(a => a.id), total: agentsList.total } : undefined),
    [agentsList],
  );

  // Effect 2 resolution: auto-pick first agent. Cannot be converted to a TanStack
  // select because the side-effect is a reducer dispatch. Kept minimal.
  useEffect(() => {
    if (agentId) return;
    const first = agentsList?.items?.[0];
    if (first) fetchAndPickAgent(first.id, dispatch);
  }, [agentId, agentsList, dispatch]);

  return { agentsList, known };
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
