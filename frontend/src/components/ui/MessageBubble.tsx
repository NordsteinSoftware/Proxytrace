import { useState, type ReactNode } from 'react';
import { Trans, useLingui } from '@lingui/react/macro';
import { msg } from '@lingui/core/macro';
import type { MessageDescriptor } from '@lingui/core';
import { ChevronRightIcon } from '../icons';
import { CopyButton } from './CopyButton';
import { cn } from '../../lib/cn';
import { MessageContent } from '../conversation/MessageContent';
import { MessageViewSelect } from '../conversation/MessageViewSelect';
import { detectView, type MessageView } from '../conversation/messageView';

type RoleKey = 'user' | 'assistant' | 'system' | 'tool';

interface RoleStyle {
  accentText: string;
  accentBg: string;
  bodyBg: string;
  border: string;
  hover: string;
  label: MessageDescriptor;
}

const ROLES: Record<RoleKey, RoleStyle> = {
  user: {
    accentText: 'text-[var(--teal)]',
    accentBg: 'bg-[var(--teal)]',
    bodyBg: 'bg-[color-mix(in_srgb,var(--teal)_6%,transparent)]',
    border: 'border-[color-mix(in_srgb,var(--teal)_25%,transparent)]',
    hover: 'hover:bg-[color-mix(in_srgb,var(--teal)_5%,transparent)]',
    label: msg`USER`,
  },
  assistant: {
    accentText: 'text-[var(--accent-primary)]',
    accentBg: 'bg-[var(--accent-primary)]',
    bodyBg: 'bg-[var(--accent-subtle)]',
    border: 'border-[color-mix(in_srgb,var(--accent-primary)_25%,transparent)]',
    hover: 'hover:bg-[color-mix(in_srgb,var(--accent-primary)_5%,transparent)]',
    label: msg`ASSISTANT`,
  },
  system: {
    accentText: 'text-[var(--text-secondary)]',
    accentBg: 'bg-[var(--text-secondary)]',
    bodyBg: 'bg-[var(--bg-wash-hover)]',
    border: 'border-[var(--border-color)]',
    hover: 'hover:bg-[var(--bg-wash-hover)]',
    label: msg`SYSTEM`,
  },
  tool: {
    accentText: 'text-success',
    accentBg: 'bg-success',
    bodyBg: 'bg-success-subtle',
    border: 'border-[color-mix(in_srgb,var(--success)_25%,transparent)]',
    hover: 'hover:bg-success-subtle',
    label: msg`TOOL`,
  },
};

function roleKey(role: string): RoleKey {
  return role === 'user' || role === 'system' || role === 'tool' ? role : 'assistant';
}

interface Props {
  msg: { role: string; content: string };
  defaultOpen?: boolean;
  /**
   * Overrides the role-derived header label. A plain string is shown verbatim; a
   * `MessageDescriptor` is localized at render (e.g. "Expected").
   */
  label?: string | MessageDescriptor;
  /** Extra header controls (revealed on hover, like the copy button). */
  actions?: ReactNode;
  /** Mid-stream: forces the bubble open, renders raw text with a cursor, animates the border. */
  streaming?: boolean;
}

export function MessageBubble({ msg, defaultOpen = true, label, actions, streaming }: Props) {
  const { i18n, t } = useLingui();
  const role = ROLES[roleKey(msg.role)];
  const [open, setOpen] = useState(defaultOpen);

  const content = msg.content?.trim() ?? '';
  const [view, setView] = useState<MessageView>(() => detectView(content));
  if (!content && !streaming) return null;
  const isOpen = open || streaming;

  const oneLine = content.replace(/\s+/g, ' ');
  const preview = oneLine.length > 90 ? oneLine.slice(0, 90) + '…' : oneLine;
  const charCount = content.length;
  const isSystem = msg.role === 'system';

  return (
    <div
      className={cn('relative group rounded-lg overflow-hidden bg-card-2 border', role.border, streaming && 'streaming-border')}
    >
      {/* The hover wash sits on the header row, not on the toggle button: the button is only the
          left `flex-1` slice, so washing it alone lit a narrow strip inside the header instead of
          the header itself. `pointer-events-none` is not needed — the row is the hover target and
          the button stays the click target. */}
      <div className={cn('flex items-center gap-2 px-3 py-2.5 transition-colors duration-100', role.hover)}>
        <button
          type="button"
          aria-expanded={isOpen}
          onClick={() => setOpen(o => !o)}
          className="flex flex-1 min-w-0 items-center gap-2 text-left bg-transparent border-0 cursor-pointer rounded-sm focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[color-mix(in_srgb,var(--accent-primary)_60%,transparent)]"
        >
          <span
            aria-hidden
            className={cn('inline-flex shrink-0 transition-transform duration-150', role.accentText, isOpen && 'rotate-90')}
          >
            <ChevronRightIcon size={11} strokeWidth={2.5} />
          </span>
          <span aria-hidden className={cn('w-[5px] h-[5px] rounded-full shrink-0', role.accentBg)} />
          <span className={cn('font-mono text-caption font-bold tracking-[0.08em] shrink-0', role.accentText)}>
            {label === undefined ? i18n._(role.label) : typeof label === 'string' ? label : i18n._(label)}
          </span>
          {!isOpen && (
            <span className="text-body truncate min-w-0 text-secondary">
              {preview}
            </span>
          )}
        </button>
        <CopyButton
          text={content}
          label={t`Copy message`}
          className="shrink-0 opacity-0 group-hover:opacity-100 focus-visible:opacity-100 transition-opacity duration-[var(--motion-fast)]"
        />
        {actions}
        <span className="font-mono text-caption leading-none tracking-[0.06em] shrink-0 text-muted">
          <Trans>{charCount.toLocaleString()} chars</Trans>
        </span>
        {isOpen && !streaming && <MessageViewSelect value={view} onChange={setView} />}
      </div>

      {isOpen && (
        <div className="border-t border-t-border-subtle">
          <div className={cn('px-3.5 py-3', role.bodyBg)}>
            {streaming ? (
              <div className="text-reading leading-[1.65] whitespace-pre-wrap text-primary">
                {msg.content}
                <span
                  aria-hidden
                  className={cn(
                    'inline-block w-[8px] h-[15px] align-text-bottom ml-0.25',
                    'animate-[pulse-dot_0.9s_ease-in-out_infinite] motion-reduce:animate-none',
                    role.accentBg,
                  )}
                />
              </div>
            ) : (
              <MessageContent content={content} view={view} isSystem={isSystem} />
            )}
          </div>
        </div>
      )}
    </div>
  );
}
