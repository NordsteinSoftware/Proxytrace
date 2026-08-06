import { useAuiState } from '@assistant-ui/react';
import { Trans, useLingui } from '@lingui/react/macro';
import { LayoutSidebarIcon, SparklesIcon } from '../../../components/icons';
import { IconButton } from '../../../components/ui/Button';
import { cn } from '../../../lib/cn';
import type { TraceyChat } from '../useTraceyChat';
import { TraceyConversation } from '../TraceyConversation';
import { TraceyComposer } from './TraceyComposer';

interface TraceyChatPanelProps {
  chat: TraceyChat;
  /** Whether the conversation-history rail is collapsed (drives the toggle's label). */
  railCollapsed: boolean;
  onToggleRail: () => void;
}

/** The chat column: header controls, message list, composer. */
export function TraceyChatPanel({ chat, railCollapsed, onToggleRail }: TraceyChatPanelProps) {
  const { t } = useLingui();
  const { startNewConversation } = chat;
  // Empty thread → "initial view": composer floats toward the middle with starter chips. The
  // bottom spacer animates to 0 on the first message (and back when the conversation is cleared),
  // which slides the composer down to / up from the bottom.
  const isEmpty = useAuiState(s => s.thread.messages.length === 0);
  // The header halo spins fast while a turn runs — the panel-level "Tracey is working" signal.
  const isRunning = useAuiState(s => s.thread.isRunning);

  return (
    <div className="flex min-h-0 flex-1 flex-col rounded-lg border border-border bg-surface-2">
      <header className="flex items-center justify-between gap-3 border-b border-hairline px-4 py-2.5">
        <div className="flex items-center gap-2.5">
          {/* Identity halo instead of a static ring; spins up while she's thinking. */}
          <span
            className={cn(
              'tracey-halo flex size-8 items-center justify-center rounded-lg bg-accent-subtle text-accent',
              isRunning && 'tracey-halo-active',
            )}
          >
            <SparklesIcon size={16} />
          </span>
          <div>
            <div className="tracey-gradient-text text-h2 font-semibold leading-tight"><Trans>Tracey AI</Trans></div>
            <div className="text-body-sm text-muted"><Trans>Your in-app assistant</Trans></div>
          </div>
        </div>
        <IconButton
          onClick={onToggleRail}
          aria-label={railCollapsed ? t`Show conversations` : t`Hide conversations`}
          title={railCollapsed ? t`Show conversations` : t`Hide conversations`}
          data-testid="tracey-rail-toggle"
        >
          {/* Mirrored: the divided pane of the glyph matches the rail's right-hand position. */}
          <LayoutSidebarIcon size={16} className="-scale-x-100" />
        </IconButton>
      </header>

      <div className="flex min-h-0 flex-1 flex-col pb-3 pt-1">
        <TraceyConversation />
        <TraceyComposer
          onNewConversation={startNewConversation}
          showStarters={isEmpty}
        />
        <div
          aria-hidden
          className={cn(
            'shrink-0 transition-[height] duration-[var(--motion-slow)] ease-[var(--ease-standard)] motion-reduce:transition-none',
            isEmpty ? 'h-[34vh]' : 'h-0',
          )}
        />
      </div>
    </div>
  );
}
