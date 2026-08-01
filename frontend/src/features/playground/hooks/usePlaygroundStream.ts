/**
 * Encapsulates the LLM streaming logic: building the payload, managing the abort
 * controller, dispatching token / tool-request / done / error events.
 */
import { useCallback, useRef } from 'react';
import {
  streamPlaygroundCompletion,
  type PlaygroundCompletePayload,
  type PlaygroundStreamEvent,
} from '../../../api/playground';
import { toPayloadMessage, toPayloadParameters } from '../playgroundMeta';
import type { PlaygroundMessage, PlaygroundOverrides, PlaygroundToolRequest } from '../state/types';

type SessionDispatch = ReturnType<typeof import('../state/usePlaygroundSession').usePlaygroundSession>['dispatch'];

interface UsePlaygroundStreamOptions {
  agentId: string | null;
  overrides: PlaygroundOverrides | null;
  dispatch: SessionDispatch;
  setStreamingId: (id: string | null) => void;
}

interface UsePlaygroundStreamResult {
  startStream: (messagesForBackend: PlaygroundMessage[], placeholderId: string) => void;
  abortStream: () => void;
}

export function usePlaygroundStream({
  agentId,
  overrides,
  dispatch,
  setStreamingId,
}: UsePlaygroundStreamOptions): UsePlaygroundStreamResult {
  const abortRef = useRef<{ abort: () => void } | null>(null);

  const buildPayload = useCallback(
    (messages: PlaygroundMessage[]): PlaygroundCompletePayload | null => {
      if (!agentId || !overrides) return null;
      return {
        agentId,
        endpointId: overrides.endpointId,
        systemPrompt: overrides.systemPrompt,
        parameters: toPayloadParameters(overrides.parameters),
        tools: overrides.tools.map(t => ({
          name: t.name,
          description: t.description,
          arguments: t.arguments.map(a => ({
            name: a.name,
            description: a.description || null,
            type: a.type,
            isRequired: a.isRequired,
          })),
        })),
        messages: messages.map(toPayloadMessage),
      };
    },
    [agentId, overrides],
  );

  const startStream = useCallback(
    (messagesForBackend: PlaygroundMessage[], placeholderId: string) => {
      const payload = buildPayload(messagesForBackend);
      if (!payload) return;

      setStreamingId(placeholderId);
      dispatch({ type: 'startStreaming' });

      let collectedTools: PlaygroundToolRequest[] = [];
      let firstPending: PlaygroundToolRequest | null = null;

      abortRef.current?.abort();
      abortRef.current = streamPlaygroundCompletion(payload, (e: PlaygroundStreamEvent) => {
        if (e.type === 'token') {
          dispatch({ type: 'appendDelta', localId: placeholderId, delta: e.delta });
        } else if (e.type === 'tool-request') {
          const req: PlaygroundToolRequest = { id: e.id, name: e.name, arguments: e.arguments };
          collectedTools = [...collectedTools, req];
          dispatch({ type: 'attachToolRequests', localId: placeholderId, toolRequests: collectedTools });
          if (!firstPending) firstPending = req;
        } else if (e.type === 'done') {
          dispatch({
            type: 'finishStreaming',
            stats: {
              inputTokens: e.inputTokens,
              outputTokens: e.outputTokens,
              cachedInputTokens: e.cachedInputTokens,
              latencyMs: e.latencyMs,
              costEur: e.costEur,
              finishReason: e.finishReason,
            },
          });
          setStreamingId(null);
          if (firstPending) dispatch({ type: 'setPendingTool', request: firstPending });
        } else if (e.type === 'error') {
          dispatch({ type: 'updateMessage', localId: placeholderId, patch: { errored: true } });
          dispatch({ type: 'setError', message: e.message });
          setStreamingId(null);
        }
      });
    },
    [buildPayload, dispatch, setStreamingId],
  );

  const abortStream = useCallback(() => {
    abortRef.current?.abort();
  }, []);

  return { startStream, abortStream };
}
