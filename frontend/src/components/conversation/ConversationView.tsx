import type { ReactElement, ReactNode } from 'react';
import { MessageBubble } from '../ui/MessageBubble';
import { ToolMessageBubble } from '../ui/ToolMessageBubble';
import { ToolResultBlock } from './ToolResultBlock';
import { cn } from '../../lib/cn';
import type { ConversationMessage } from './types';

interface Props {
  messages: ConversationMessage[];
  /** Extra classes for the list container (spacing/scroll is the caller's concern). */
  className?: string;
  /** Whether system messages start expanded. Defaults to collapsed. */
  defaultOpenSystem?: boolean;
  /** Navigate to a tool's definition from its tool bubble. */
  onJumpToDefinition?: (toolName: string) => void;
  /** Rendered after the message list (e.g. a finish-reason footer). */
  footer?: ReactNode;
  /** Rendered when there are no messages. */
  emptyState?: ReactNode;
}

/**
 * The single, canonical conversation renderer. Pairs tool-result messages with the
 * tool calls that produced them, then renders each turn with the shared message and
 * tool bubbles. Feed it a normalized message list via the adapters in `adapters.ts`.
 */
export function ConversationView({
  messages,
  className,
  defaultOpenSystem = false,
  onJumpToDefinition,
  footer,
  emptyState,
}: Props) {
  if (messages.length === 0 && emptyState) {
    return <>{emptyState}</>;
  }

  // Pair tool results to their originating calls so a call and its result render as one block.
  const resultByCallId = new Map<string, ConversationMessage>();
  for (const m of messages) {
    if (m.role === 'tool' && m.toolCallId) resultByCallId.set(m.toolCallId, m);
  }
  const absorbedCallIds = new Set(
    messages.flatMap(m => (m.toolCalls ?? []).map(tc => tc.id)),
  );

  // shrink-0 (self + children) keeps bubbles from vertically compressing inside a
  // flex-col scroll container — the canonical behaviour of every caller.
  return (
    <div className={cn('flex flex-col gap-2.5 shrink-0 [&>*]:shrink-0', className)}>
      {messages.flatMap((msg, i) => {
        if (msg.role === 'tool') {
          if (msg.toolCallId && absorbedCallIds.has(msg.toolCallId)) return [];
          return [<ToolResultBlock key={`m${i}`} content={msg.content} toolCallId={msg.toolCallId} />];
        }

        const blocks: ReactElement[] = [];
        if (msg.content?.trim()) {
          blocks.push(
            <MessageBubble
              key={`m${i}`}
              msg={msg}
              label={msg.label}
              defaultOpen={msg.role !== 'system' || defaultOpenSystem}
            />,
          );
        }
        msg.toolCalls?.forEach(tc => {
          const result = resultByCallId.get(tc.id);
          blocks.push(
            <ToolMessageBubble
              key={`t${tc.id}`}
              request={tc}
              result={result}
              defaultOpen={false}
              onJumpToDefinition={onJumpToDefinition ? () => onJumpToDefinition(tc.name) : undefined}
            />,
          );
        });
        return blocks;
      })}
      {footer}
    </div>
  );
}
