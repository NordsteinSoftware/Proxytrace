import { useAui, useAuiState } from '@assistant-ui/react';
import { useLingui } from '@lingui/react/macro';
import { Badge } from '../../../components/ui/Badge';
import { cn } from '../../../lib/cn';
import { useTraceyChatContext } from '../tracey-chat-context';

// Same chip anatomy as the starter ToolChips: fade-up on the button (staggered per chip); hover is
// a color change only.
const CHIP = cn(
  'group fade-up rounded-none cursor-pointer focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[color-mix(in_srgb,var(--accent-primary)_60%,transparent)]',
);

/**
 * The auto-generated follow-up chips under the last assistant message. Shown only while the
 * message they were generated for is still the thread's last message and no turn is running —
 * so sending anything (including clicking a chip, which sends its text as a user message)
 * removes them immediately.
 */
export function FollowUpSuggestions() {
  const { t } = useLingui();
  const { followUps } = useTraceyChatContext();
  const composer = useAui().composer;
  const isRunning = useAuiState(s => s.thread.isRunning);
  const lastMessageId = useAuiState(s => s.thread.messages.at(-1)?.id);

  if (!followUps || isRunning || followUps.messageId !== lastMessageId) return null;

  const send = (text: string): void => {
    composer.setText(text);
    composer.send();
  };

  return (
    <div
      className="flex flex-wrap items-center gap-1.5"
      role="group"
      aria-label={t`Suggested follow-ups`}
      data-testid="tracey-follow-ups"
    >
      {followUps.items.map((item, index) => (
        // eslint-disable-next-line no-restricted-syntax -- Badge-wrapping suggestion chip (sends its text as a user message)
        <button
          key={item}
          type="button"
          onClick={() => send(item)}
          className={CHIP}
          style={{ animationDelay: `${index * 45}ms` }}
          data-testid={`tracey-follow-up-btn-${index}`}
        >
          <Badge label={item} variant="accent" size="md" />
        </button>
      ))}
    </div>
  );
}
