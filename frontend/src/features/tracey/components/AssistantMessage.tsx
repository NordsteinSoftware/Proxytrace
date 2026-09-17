import { MessagePrimitive } from '@assistant-ui/react';
import { MarkdownText } from './MarkdownText';
import { MessageStatusBar } from './MessageStatusBar';
import { ToolCallCard } from './ToolCallCard';
import { TRACEY_TOOL_UI } from './tool-ui/registry';

// Assistant text is rendered as Markdown; tools with a dedicated inline UI render via `by_name`,
// everything else falls back to the diagnostic tool card.
const assistantParts = { Text: MarkdownText, tools: { by_name: TRACEY_TOOL_UI, Fallback: ToolCallCard } };

export function AssistantMessage() {
  // No entrance animation on the message root: the cards inside own their fade-up, and stacking
  // a second translate on the parent would compound the motion (text streams in progressively
  // anyway, so a root-level entrance buys nothing).
  return (
    <MessagePrimitive.Root className="flex justify-start">
      <div className="flex min-w-0 flex-1 flex-col gap-3 text-reading text-primary">
        <MessagePrimitive.Parts components={assistantParts} />
        {/* Once the turn finishes, usage/duration/correlation-id land on the message metadata and
            the status row renders; while streaming the metadata is absent, so it stays hidden. */}
        <MessageStatusBar />
      </div>
    </MessagePrimitive.Root>
  );
}
