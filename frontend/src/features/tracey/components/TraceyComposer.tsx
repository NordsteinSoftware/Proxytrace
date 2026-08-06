import { useMemo, useState, type KeyboardEvent } from 'react';
import { ComposerPrimitive, ThreadPrimitive, useAui, useAuiState } from '@assistant-ui/react';
import { Trans, useLingui } from '@lingui/react/macro';
import { type I18n } from '@lingui/core';
import { QUICK_ACTIONS } from '../tracey-quick-actions';
import { TRACEY_TOOLS_META } from '../tracey-tools';
import { ArrowUpIcon, MessagePlusIcon, SparklesIcon, StopIcon } from '../../../components/icons';
import { IconButton } from '../../../components/ui/Button';
import { cn } from '../../../lib/cn';
import { FOCUS_RING_FIELD } from '../../../lib/constants';
import { SlashMenu, type SlashItem } from './SlashMenu';
import { ToolChips } from './ToolChips';

interface TraceyComposerProps {
  onNewConversation: () => void;
  /** Initial-view starter chips: shown only while the conversation is empty. */
  showStarters: boolean;
}

const ALL_ITEMS: SlashItem[] = [
  ...QUICK_ACTIONS.map((action): SlashItem => ({ kind: 'action', action })),
  ...TRACEY_TOOLS_META.map((t): SlashItem => ({ kind: 'tool', name: t.name, description: t.description })),
];

/** Shared footprint for the composer's primary control, so Send and Stop occupy the same slot. */
const COMPOSER_BTN_CLS = cn(
  'grid size-8 shrink-0 cursor-pointer place-items-center rounded-md transition-[background,color,opacity] duration-[var(--motion-base)] ease-[var(--ease-standard)] focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[color-mix(in_srgb,var(--accent-primary)_60%,transparent)] disabled:cursor-not-allowed disabled:opacity-40',
);
// Send: the cyan primary CTA (dark accent-ink on the flat cyan fill). Stop: neutral halt control
// (the cyan fill is reserved for the one primary action, and a halt reads better as a calm neutral
// square). The arrow nudges up on hover — a transform inside the fixed-size button, so it never
// shifts layout.
const SEND_BTN_CLS = cn('group bg-accent text-accent-ink hover:bg-accent-hover');
const STOP_BTN_CLS = cn('border border-border bg-card-2 text-primary hover:bg-card');

function matches(item: SlashItem, query: string, i18n: I18n): boolean {
  if (!query) return true;
  const haystack =
    item.kind === 'action'
      ? `${i18n._(item.action.label)} ${i18n._(item.action.hint)}`
      : `${item.name} ${item.description}`;
  return haystack.toLowerCase().includes(query);
}

export function TraceyComposer({ onNewConversation, showStarters }: TraceyComposerProps) {
  const { t, i18n } = useLingui();
  const composer = useAui().composer;
  const text = useAuiState(s => s.composer.text);
  // While a turn streams/runs tools the composer frame carries the animated streaming ring
  // (DESIGN.md §8) so the "Tracey is working" signal lives right where the user's attention is.
  const isRunning = useAuiState(s => s.thread.isRunning);
  // The selection is tagged with the list ("key") it belongs to. When the menu (re)opens or the
  // query changes, that key changes and the highlight derives back to the first entry — so
  // keyboard nav always starts from a known position without an effect.
  const [sel, setSel] = useState<{ key: string; index: number }>({ key: '', index: 0 });
  // The exact text the menu was dismissed at; typing anything else re-opens it (no effects needed).
  const [dismissedAt, setDismissedAt] = useState<string | null>(null);

  const open = text.startsWith('/') && text !== dismissedAt;
  const query = open ? text.slice(1).toLowerCase() : '';
  const items = useMemo(() => (open ? ALL_ITEMS.filter(i => matches(i, query, i18n)) : []), [open, query, i18n]);
  const selKey = open ? query : '';
  const activeIndex = items.length
    ? (sel.key === selKey ? Math.min(Math.max(sel.index, 0), items.length - 1) : 0)
    : 0;
  const setActive = (index: number) => setSel({ key: selKey, index });

  function selectItem(item: SlashItem) {
    // A quick-action fills its full prompt; a tool inserts a `/tool_name` slash command.
    const next = item.kind === 'action' ? item.action.prompt : `/${item.name} `;
    composer.setText(next);
    // Park the menu closed at exactly this text so it doesn't immediately re-open on a `/…` value.
    setDismissedAt(next);
  }

  function onKeyDown(e: KeyboardEvent<HTMLTextAreaElement>) {
    if (!open || items.length === 0) {
      if (e.key === 'Escape' && open) {
        e.preventDefault();
        setDismissedAt(text);
      }
      return;
    }
    if (e.key === 'ArrowDown') {
      e.preventDefault();
      setActive((activeIndex + 1) % items.length);
    } else if (e.key === 'ArrowUp') {
      e.preventDefault();
      setActive((activeIndex - 1 + items.length) % items.length);
    } else if (e.key === 'Enter' && !e.shiftKey) {
      e.preventDefault();
      selectItem(items[activeIndex]);
    } else if (e.key === 'Escape') {
      e.preventDefault();
      setDismissedAt(text);
    }
  }

  return (
    <div className="mx-auto flex w-full max-w-3xl flex-col gap-2 px-4">
      {showStarters && (
        <div className="flex flex-col items-center gap-5 pb-2 animate-[fade-up_var(--motion-slow)_var(--ease-standard)]">
          <div className="flex flex-col items-center gap-3 text-center">
            {/* The identity halo (Tracey tier) replaces the static ring — see DESIGN.md §8.2. */}
            <div className="tracey-halo flex size-14 items-center justify-center rounded-xl bg-accent-subtle text-accent">
              <SparklesIcon size={26} />
            </div>
            <div className="tracey-gradient-text text-display font-semibold leading-tight">
              <Trans>How can I help?</Trans>
            </div>
            <div className="max-w-md text-chat text-secondary">
              <Trans>
                Ask about your agents, suites, runs, or proposals — or have me run a suite, review a
                proposal, or plot your data.
              </Trans>
            </div>
          </div>
          <div className="flex w-full justify-center">
            <ToolChips />
          </div>
        </div>
      )}
      <div className="relative">
        {open && (
          <SlashMenu
            items={items}
            activeIndex={activeIndex}
            onSelect={selectItem}
            onHover={setActive}
          />
        )}
        <ComposerPrimitive.Root
          className={cn(
            'flex flex-col gap-2 rounded-xl border border-border bg-card px-3 py-2.5 transition-[border-color,box-shadow] duration-[var(--motion-base)] ease-[var(--ease-standard)] focus-within:border-[color-mix(in_srgb,var(--accent-primary)_40%,transparent)]',
            // The frame is the field — the textarea is deliberately borderless — so the composer's
            // focus ring lives here rather than on the textarea (DESIGN §7). It is scoped to the
            // text control: plain `focus-within:` would also light the whole frame when the Send /
            // Stop button is focused, and those ring themselves. The 40% border tint stays as the
            // softer "a control in the composer has focus" cue.
            FOCUS_RING_FIELD,
            isRunning && 'streaming-border',
          )}
        >
          <ComposerPrimitive.Input
            autoFocus
            onKeyDown={onKeyDown}
            // eslint-disable-next-line lingui/no-unlocalized-strings -- ARIA role token, not UI copy
            aria-haspopup="listbox"
            aria-expanded={open}
            // eslint-disable-next-line lingui/no-unlocalized-strings -- DOM element id, not UI copy
            aria-controls={open ? 'tracey-slash-menu' : undefined}
            placeholder={t`Ask Tracey…  (/ for tools)`}
            className="max-h-48 min-h-16 w-full resize-none bg-transparent px-1 pt-1 text-chat text-primary outline-none placeholder:text-muted"
          />
          <div className="flex items-center justify-end gap-2">
            <div className="flex items-center gap-1">
              <IconButton
                onClick={onNewConversation}
                aria-label={t`New conversation`}
                title={t`New conversation`}
                data-testid="tracey-new-conversation-composer"
              >
                <MessagePlusIcon size={16} />
              </IconButton>
              {/* While a turn streams/runs tools the send button becomes a Stop button.
                  ComposerPrimitive.Cancel cancels the run, which aborts the AI SDK signal we
                  forward into streamText + every tool's execute — so the upstream LLM call (and
                  any in-flight await_actions poll) tears down, and the server's RequestAborted
                  cancellation token fires on the proxied call. */}
              <ThreadPrimitive.If running={false}>
                <ComposerPrimitive.Send
                  aria-label={t`Send`}
                  title={t`Send`}
                  data-testid="tracey-send-btn"
                  className={cn(COMPOSER_BTN_CLS, SEND_BTN_CLS)}
                >
                  <ArrowUpIcon
                    size={16}
                    className="transition-transform duration-[var(--motion-base)] ease-[var(--ease-standard)] group-hover:-translate-y-0.5 motion-reduce:transition-none motion-reduce:group-hover:translate-y-0"
                  />
                </ComposerPrimitive.Send>
              </ThreadPrimitive.If>
              <ThreadPrimitive.If running>
                <ComposerPrimitive.Cancel
                  aria-label={t`Stop`}
                  title={t`Stop generating`}
                  data-testid="tracey-stop-btn"
                  className={cn(COMPOSER_BTN_CLS, STOP_BTN_CLS)}
                >
                  <StopIcon size={13} />
                </ComposerPrimitive.Cancel>
              </ThreadPrimitive.If>
            </div>
          </div>
        </ComposerPrimitive.Root>
      </div>
    </div>
  );
}
