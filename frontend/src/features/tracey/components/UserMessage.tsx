import { MessagePrimitive } from '@assistant-ui/react';
import type { TextMessagePartComponent } from '@assistant-ui/react';
import { ToolCallCard } from './ToolCallCard';
import { TRACEY_TOOL_UI } from './tool-ui/registry';

/** User text is shown verbatim (assistant text is Markdown — see {@link AssistantMessage}). */
const PlainText: TextMessagePartComponent = ({ text }) => (
  <span className="whitespace-pre-wrap break-words">{text}</span>
);

const userParts = { Text: PlainText, tools: { by_name: TRACEY_TOOL_UI, Fallback: ToolCallCard } };

export function UserMessage() {
  return (
    <MessagePrimitive.Root className="fade-up flex justify-end">
      {/* Cyan fill carries dark accent-ink text — never white on cyan. The flat fill gives the
          bubble the same finish as a primary CTA; text sits at the reading tier. */}
      <div className="max-w-[80%] bg-accent px-4 py-2.5 text-reading font-medium text-accent-ink">
        <MessagePrimitive.Parts components={userParts} />
      </div>
    </MessagePrimitive.Root>
  );
}
