import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { useNavigate } from 'react-router';
import { useQuery } from '@tanstack/react-query';
import { useChatRuntime } from '@assistant-ui/react-ai-sdk';
import { lastAssistantMessageIsCompleteWithToolCalls } from 'ai';
import { traceyApi, type TraceySessionDto } from '../../api/tracey';
import { useFeature } from '../../hooks/useLicense';
import { QUERY_KEYS } from '../../api/query-keys';
import useCurrentProject from '../../hooks/useCurrentProject';
import { useCurrentUser } from '../../auth/useCurrentUser';
import { useKiosk } from '../../contexts/KioskContext';
import { TRACEY_SYSTEM_PROMPT } from './tracey-prompt';
import { TraceyTransport } from './tracey-runtime';
import type { TraceyToolContext } from './tracey-tools';
import { useAskTracey } from './useAskTracey';
import { useFollowUpSuggestions, type FollowUpState } from './useFollowUpSuggestions';
import {
  loadConversationIndex,
  loadConversationSnapshot,
  isRestorableSnapshot,
  type ConversationMeta,
  type ConversationSnapshot,
} from './tracey-storage';
import { pruneArtifacts } from './tracey-artifact-store';
import { DelegatingTransport } from './tracey-delegating-transport';
import {
  loadHistory,
  unionArtifactRefs,
  persistConversationSnapshot,
  type ConversationHistory,
} from './tracey-history';
import { useConversationActions } from './useConversationActions';

export interface TraceyChat {
  runtime: ReturnType<typeof useChatRuntime>;
  status: 'no-project' | 'loading' | 'error' | 'ready';
  /** The stored conversation history for the current user+project, newest activity first is up to the view. */
  conversations: ConversationMeta[];
  /** Id of the conversation currently loaded in the runtime, or `null` for a fresh unsaved one. */
  activeConversationId: string | null;
  /** Load a past conversation into the runtime (view == continue — the user can keep typing). */
  selectConversation: (id: string) => void;
  /** Delete a stored conversation (and its artifacts); falls back to the most recent remaining. */
  deleteConversation: (id: string) => void;
  /** Archive the current conversation (it stays in history) and switch to a fresh empty thread. */
  startNewConversation: () => void;
  /** Client-side route change (used by entity-card tool UIs). */
  navigate: (path: string) => void;
  /** Auto-generated follow-up suggestions for the last finished turn (`null` when none). */
  followUps: FollowUpState | null;
  /**
   * Provision the Tracey session (model + agent). The runtime is built app-wide so the
   * conversation survives navigation, but the session — which has backend side effects
   * (agent provisioning) — is only created once the user actually opens Tracey. Idempotent;
   * the page calls it on mount and the session then stays alive across navigation.
   */
  activate: () => void;
  /**
   * True when Tracey can actually be used here: a project is selected, kiosk mode is
   * interactive, and the Tracey license feature is on. `AskTraceyButton` renders only when
   * true (mirrors the sidebar nav gating).
   */
  available: boolean;
  /**
   * App-wide "Ask Tracey" entry: archives the current conversation, navigates to the Tracey
   * page, activates the session, and sends `prompt` as the first message of a fresh
   * conversation (queued until the session is ready).
   */
  askTracey: (prompt: string) => void;
}

export function useTraceyChat(): TraceyChat {
  const navigate = useNavigate();
  const { currentProject } = useCurrentProject();
  const currentUser = useCurrentUser();
  // Tracey makes real LLM calls; in kiosk she's only available when an LLM endpoint is configured.
  // Since the runtime mounts app-wide (above the router), gate the session here so it's only
  // provisioned when Tracey is actually available.
  const { interactive } = useKiosk();
  // Tracey is an Enterprise feature; on a Free install the session endpoint 402s, so never fire it.
  const traceyLicensed = useFeature('Tracey');
  // The runtime mounts app-wide, but the session (and its backend agent provisioning) is only
  // created once the user opens Tracey. Latched on, so it stays alive across navigation.
  const [activated, setActivated] = useState(false);
  const activate = useCallback(() => setActivated(true), []);
  const projectId = currentProject?.id;
  const userKey = currentUser?.email ?? 'anon';
  const projectKey = projectId ?? 'none';
  // Artifact scope is per user+project and SHARED across that project's conversations (artifact
  // refs are globally unique, so there is no cross-conversation collision). Keeping it stable means
  // the tool context / transport are not rebuilt when the user switches conversation, and mount
  // pruning must keep the UNION of every conversation's refs (see the restore effect below).
  const artifactScope = `${userKey}:${projectKey}`;

  // Conversation history for the rail, read from localStorage and re-read on user/project change
  // via the render-time pattern (pages don't remount on a project switch — see useTraceFilters).
  // `activeIdRef`/`createdAtRef` mirror the active conversation for the persist closure;
  // `restoringRef` gates persistence while we swap threads + import so the transient empty-thread /
  // import notifications can't clobber a stored snapshot.
  const scopeKey = `${userKey}:${projectKey}`;
  const [history, setHistory] = useState<ConversationHistory>(() => loadHistory(userKey, projectKey));
  const [trackedScope, setTrackedScope] = useState(scopeKey);
  if (scopeKey !== trackedScope) {
    setTrackedScope(scopeKey);
    setHistory(loadHistory(userKey, projectKey));
  }
  const conversations = history.items;
  const activeConversationId = history.activeId;
  const activeIdRef = useRef<string | null>(null);
  const createdAtRef = useRef<number>(0);
  const restoringRef = useRef(false);

  // Write tools are always auto-approved. The `confirm` seam stays in the tool layer (every
  // write still calls it before mutating) so a confirmation gate can be reintroduced there —
  // never in the model — if it's ever needed again.
  const confirm = useCallback(() => Promise.resolve(true), []);

  const { data: session, status: queryStatus } = useQuery<TraceySessionDto>({
    queryKey: QUERY_KEYS.traceySession(projectId),
    queryFn: () => traceyApi.getSession(projectId),
    enabled: !!projectId && interactive && traceyLicensed && activated,
    // Refresh comfortably before the 1-hour key expiry.
    staleTime: 50 * 60 * 1000,
    refetchInterval: 50 * 60 * 1000,
    // This hook is mounted app-wide (in Shell, above the router), so a failed session must not
    // bubble to an ErrorBoundary and crash the shell on every page. The 'error' status is
    // surfaced as a contained empty state on the Tracey page instead (see TraceyAI).
    throwOnError: false,
  });

  const transport = useMemo(() => new DelegatingTransport(), []);
  // `ask_questions` is a human-in-the-loop tool with no `execute`: the model emits the call and
  // the widget supplies the answers via `addResult`. This makes the runtime auto-resubmit once the
  // pending tool call has its result, so the model continues the same turn (no extra user message).
  const runtime = useChatRuntime({
    transport,
    sendAutomaticallyWhen: lastAssistantMessageIsCompleteWithToolCalls,
  });

  const toolContext = useMemo<TraceyToolContext>(
    () => ({ projectId, artifactScope, navigate, confirm, loadedSkillIds: new Set<string>() }),
    [projectId, artifactScope, navigate, confirm],
  );

  // Swap in the real same-origin transport whenever the session (or tool context) changes.
  // `innerRef` mirrors it for the follow-up generation callback (which must not rebuild the hook
  // chain when the session refreshes).
  const innerRef = useRef<TraceyTransport | null>(null);
  useEffect(() => {
    const inner = session && projectId
      ? new TraceyTransport(projectId, session.model, toolContext, TRACEY_SYSTEM_PROMPT)
      : null;
    innerRef.current = inner;
    transport.setInner(inner);
  }, [transport, session, projectId, toolContext]);

  // Follow-up suggestions: after each finished turn, one small extra LLM call proposes what the
  // user might send next (chips under the last message; see useFollowUpSuggestions).
  const generateFollowUps = useCallback(
    (userText: string, assistantText: string, signal: AbortSignal) =>
      innerRef.current ? innerRef.current.generateFollowUps(userText, assistantText, signal) : null,
    [],
  );
  const followUps = useFollowUpSuggestions(runtime, generateFollowUps);

  // Conversation restore + persistence. Re-runs on user/project change (pages don't remount on a
  // project switch), so switching projects restores that project's active thread. The subscription
  // is set up last; the restore runs first (guarded) so its notifications don't persist. Display
  // state is owned by the render-time read above — this effect only touches the runtime + refs and
  // pushes structural updates back through `setHistory` from the (subscription) persist callback.
  useEffect(() => {
    const thread = runtime.thread;
    const index = loadConversationIndex(userKey, projectKey);
    activeIdRef.current = index.activeId;
    const activeMeta = index.activeId ? index.items.find(i => i.id === index.activeId) : undefined;
    createdAtRef.current = activeMeta?.createdAt ?? 0;

    // Prune artifacts against the UNION of every conversation's refs, not just the active one —
    // they share one scope, so an active-only prune would delete the other conversations' charts.
    // Mount-only (the set is stable here); pruning mid-stream could race a just-written blob.
    void pruneArtifacts(artifactScope, unionArtifactRefs(userKey, projectKey, index.items)).catch(() => {});

    // Restore the active conversation. Snapshots round-trip via export/importExternalState — the
    // JSON-safe AI SDK message format; the plain export()/import() pair loses the Symbol-linked AI
    // SDK messages in JSON, leaving an imported thread that renders empty. The import replaces the
    // thread, so it also clears a previous project's messages on a project switch; when there is
    // nothing to restore we switch to a fresh thread to clear. Guard persistence for the whole restore.
    restoringRef.current = true;
    const saved = index.activeId
      ? loadConversationSnapshot<ConversationSnapshot>(userKey, projectKey, index.activeId)
      : null;
    if (saved && isRestorableSnapshot(saved)) {
      try {
        thread.importExternalState(saved);
        restoringRef.current = false;
      } catch {
        // Incompatible snapshot: fall back to a clean thread rather than showing stale messages.
        void runtime.threads.switchToNewThread().finally(() => { restoringRef.current = false; });
      }
    } else {
      void runtime.threads.switchToNewThread().finally(() => { restoringRef.current = false; });
    }

    // Mirror every thread change to the ACTIVE conversation (keyed off `activeIdRef`, not a fixed
    // key). Fires per streamed token, so it only re-renders the rail on a structural change.
    const persist = () => {
      if (restoringRef.current) return;
      let snapshot: ConversationSnapshot;
      try {
        snapshot = thread.exportExternalState() as ConversationSnapshot;
      } catch {
        return;
      }
      const count = snapshot.messages?.length ?? 0;
      if (count === 0) return; // never persist an empty thread (a just-started new conversation)

      const wasNew = activeIdRef.current === null;
      if (wasNew) {
        activeIdRef.current = crypto.randomUUID();
        createdAtRef.current = Date.now();
      }
      const id = activeIdRef.current;
      if (id === null) return; // unreachable after the mint above; narrows the type without `!`

      const { items, structural } = persistConversationSnapshot(
        userKey, projectKey, artifactScope, id, snapshot, createdAtRef.current, count,
        evictedItems => setHistory(h => ({ items: evictedItems, activeId: h.activeId })),
      );
      // Re-render the rail only when the conversation set changed (new conversation / eviction) —
      // not on every token; title/updatedAt of the active row settle without live churn.
      if (wasNew || structural) {
        setHistory({ items, activeId: id });
      }
    };
    return thread.subscribe(persist);
  }, [runtime, userKey, projectKey, artifactScope]);

  const { startFreshThread, startNewConversation, selectConversation, deleteConversation } =
    useConversationActions({
      runtime, userKey, projectKey, artifactScope,
      activeIdRef, createdAtRef, restoringRef, setHistory,
    });

  const status: TraceyChat['status'] = !projectId || !interactive
    ? 'no-project'
    : queryStatus === 'pending'
      ? 'loading'
      : queryStatus === 'error'
        ? 'error'
        : 'ready';

  const available = !!projectId && interactive && traceyLicensed;
  const askTracey = useAskTracey({ runtime, status, activate, startFreshThread, navigate });

  return {
    runtime,
    status,
    conversations,
    activeConversationId,
    selectConversation,
    deleteConversation,
    startNewConversation,
    navigate,
    followUps,
    activate,
    available,
    askTracey,
  };
}
