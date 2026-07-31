import { useLingui } from '@lingui/react/macro';
import type { AgentCallDto } from '../../../api/models';
import { cn } from '../../../lib/cn';
import { EYEBROW_CLS } from '../../ui/classes';
import { ConversationView } from '../../conversation/ConversationView';
import { fromAgentCall } from '../../conversation/adapters';

interface Props {
  calls: AgentCallDto[];
  highlightedCallId: string | null;
}

/**
 * The conversation, one block per captured call. Each block is anchored by its call id so a
 * proposal can point at the turn it came from.
 *
 * Rendering per call — rather than one flat stream — is deliberate: every call's request
 * re-contains the whole prior conversation, so a naive flatMap would repeat every earlier turn,
 * and the per-call boundary is exactly what a proposal names.
 */
export function TranscriptPane({ calls, highlightedCallId }: Props) {
  const { t } = useLingui();

  if (calls.length === 0) {
    return (
      <div className="px-3 py-4 text-body text-muted text-center" data-testid="synthesis-transcript-empty">
        {t`No calls in this conversation.`}
      </div>
    );
  }

  return (
    <div className="flex flex-col gap-4" data-testid="synthesis-transcript">
      {calls.map((call, index) => (
        <div
          key={call.id}
          id={`synthesis-call-${call.id}`}
          data-testid={`synthesis-call-${call.id}`}
          className={cn(
            'flex flex-col gap-2 p-3 transition-colors duration-150',
            call.id === highlightedCallId ? 'bg-accent-subtle' : 'bg-card-2',
          )}
        >
          <span className={EYEBROW_CLS}>{t`Call ${index + 1}`}</span>
          <ConversationView messages={fromAgentCall(call)} />
        </div>
      ))}
    </div>
  );
}
