import ReactMarkdown from 'react-markdown';
import remarkGfm from 'remark-gfm';
import { markdownComponents } from './markdown-components';

/**
 * Renders a static Markdown string with the same DESIGN.md-themed elements as the streaming chat
 * renderer. Used for tool artifacts (e.g. `show_text`) and the message-bubble Markdown view where
 * the content is a plain string rather than an assistant message part. Content is always treated
 * as text — never raw HTML.
 */
export function Markdown({ content }: { content: string }) {
  return (
    <div className="text-reading leading-relaxed text-primary">
      <ReactMarkdown remarkPlugins={[remarkGfm]} components={markdownComponents} skipHtml>
        {content}
      </ReactMarkdown>
    </div>
  );
}
