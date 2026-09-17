import { useMemo } from 'react';
import { useLingui } from '@lingui/react/macro';
import { JsonBlock } from '../ui/JsonBlock';
import { Markdown } from '../markdown/Markdown';
import { AlertTriangleIcon } from '../icons';
import { sanitizeHtml } from '../../lib/sanitize';
import { cn } from '../../lib/cn';
import { tryParseJson, type MessageView } from './messageView';

interface Props {
  content: string;
  view: MessageView;
  isSystem?: boolean;
}

function WarningBanner({ children }: { children: string }) {
  return (
    <div
      data-testid="message-view-warning"
      className="mb-2 flex items-start gap-1.5 text-warn text-body-sm"
    >
      <AlertTriangleIcon size={13} className="mt-0.5 shrink-0" />
      <span>{children}</span>
    </div>
  );
}

function RawText({ content, isSystem }: { content: string; isSystem?: boolean }) {
  return (
    <div
      className={cn(
        'text-reading leading-[1.65] whitespace-pre-wrap',
        isSystem ? 'text-secondary italic' : 'text-primary',
      )}
    >
      {content}
    </div>
  );
}

/**
 * Renders a message body in the chosen view. JSON falls back to RAW with a warning when the
 * content is not valid JSON; HTML is rendered from DOMPurify-sanitized markup (the sanctioned
 * exception to the no-dangerouslySetInnerHTML rule — DESIGN §9), warning when tags were stripped.
 */
export function MessageContent({ content, view, isSystem }: Props) {
  const { t } = useLingui();
  const json = useMemo(() => (view === 'json' ? tryParseJson(content) : null), [view, content]);
  const sanitized = useMemo(() => (view === 'html' ? sanitizeHtml(content) : null), [view, content]);

  return (
    <div data-testid="message-content">
      {view === 'raw' && <RawText content={content} isSystem={isSystem} />}

      {view === 'json' &&
        (json?.ok ? (
          <JsonBlock value={content} transparent />
        ) : (
          <>
            <WarningBanner>{t`Not valid JSON — showing raw text`}</WarningBanner>
            <RawText content={content} isSystem={isSystem} />
          </>
        ))}

      {view === 'markdown' && <Markdown content={content} />}

      {view === 'html' && sanitized && (
        <>
          {sanitized.modified && <WarningBanner>{t`Some HTML was removed for safety`}</WarningBanner>}
          {/* DOMPurify-sanitized markup — see lib/sanitize.ts sanitizeHtml. */}
          <div
            className="text-reading leading-relaxed text-primary"
            dangerouslySetInnerHTML={{ __html: sanitized.html }}
          />
        </>
      )}
    </div>
  );
}
