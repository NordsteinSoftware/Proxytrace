import { useEffect } from 'react';
import { useSearchParams } from 'react-router';
import { agentCallsApi } from '../../../api/agent-calls';
import type { AgentCallDto } from '../../../api/models';

interface FocusTraceActions {
  onTrace: (trace: AgentCallDto) => void;
  onExpandConversation: (conversationId: string) => void;
}

/**
 * Reads ?focus=<id> from the URL, fetches that trace, then calls back so the
 * page can select it and expose the matching conversation group. This is a
 * genuine external side-effect (URL param + API call driving UI) per
 * BEST_PRACTICES §4.1.
 *
 * The `onTrace` callback OWNS clearing the `focus` param, in the same URL
 * update that writes the selection (`selectTrace(id, ['focus'])`). The hook
 * must not delete it in a second `setSearchParams` call: both functional
 * updaters in one tick derive from the pre-update URL, so the later delete
 * would drop the just-written `?trace=` and the drawer would never open.
 */
export function useFocusTrace({ onTrace, onExpandConversation }: FocusTraceActions) {
  const [searchParams] = useSearchParams();
  const focusId = searchParams.get('focus');

  useEffect(() => {
    if (!focusId) return;
    let cancelled = false;
    agentCallsApi.get(focusId).then(trace => {
      if (cancelled) return;
      onTrace(trace);
      if (trace.conversationId) {
        onExpandConversation(trace.conversationId);
      }
    }).catch(() => {});
    return () => { cancelled = true; };
  }, [focusId, onTrace, onExpandConversation]);
}
