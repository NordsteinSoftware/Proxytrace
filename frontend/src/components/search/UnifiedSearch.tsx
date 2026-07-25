import {
  forwardRef, useCallback, useImperativeHandle, useRef, useState,
} from 'react';
import { useNavigate } from 'react-router';
import { Trans, Plural, useLingui } from '@lingui/react/macro';
import type { SearchHit, SearchKind } from '../../api/search';
import { searchHitToHref } from '../../lib/search-routes';
import { SearchIcon, XIcon } from '../icons';
import { cn } from '../../lib/cn';
import { fieldFocusWithinCls, kbdCls } from '../ui/classes';
import { FOCUS_RING } from '../../lib/constants';
import { useSearchQuery } from './hooks/useSearchQuery';
import { useSearchInteraction } from './hooks/useSearchInteraction';
import { useGroupedHits } from './searchGrouping';
import { SearchResultList } from './SearchResultList';
import { SearchPreview } from './SearchPreview';

export interface UnifiedSearchHandle {
  focus: () => void;
}

interface Props {
  projectId: string;
  kinds?: SearchKind[];
  onSelect?: (hit: SearchHit) => void;
  placeholder?: string;
  autoFocus?: boolean;
  width?: 'auto' | 'fixed';
  showRecents?: boolean;
  recentLimit?: number;
  className?: string;
  showShortcut?: boolean;
}

export const UnifiedSearch = forwardRef<UnifiedSearchHandle, Props>(function UnifiedSearch({
  projectId,
  kinds,
  onSelect,
  placeholder,
  autoFocus = false,
  // eslint-disable-next-line lingui/no-unlocalized-strings -- layout variant token, not UI copy
  width = 'fixed',
  showRecents = true,
  recentLimit = 6,
  className = '',
  showShortcut = true,
}, ref) {
  const { t } = useLingui();
  const [activeIndex, setActiveIndex] = useState(0);
  const navigate = useNavigate();
  const inputRef = useRef<HTMLInputElement>(null);

  const handleClose = useCallback(() => {
    inputRef.current?.blur();
  }, []);

  const { raw, setRaw, debounced, open, setOpen, wrapperRef } =
    useSearchInteraction({ onClose: handleClose });

  useImperativeHandle(ref, () => ({
    focus: () => inputRef.current?.focus(),
  }), []);

  const { hits, groupOrder, isRecentMode, fetching, recentErrored } = useSearchQuery({
    projectId,
    debounced,
    kinds,
    showRecents,
    recentLimit,
    open,
  });

  const { grouped, flat, groupOffsets } = useGroupedHits(hits, groupOrder);
  const activeHit = flat[activeIndex];
  const searchEnabled = debounced.length >= 2;

  function close() {
    setOpen(false);
    inputRef.current?.blur();
  }

  function commit(hit: SearchHit) {
    if (onSelect) {
      onSelect(hit);
    } else {
      navigate(searchHitToHref(hit));
    }
    setRaw('');
    close();
  }

  function onKeyDown(e: React.KeyboardEvent<HTMLInputElement>) {
    if (e.key === 'ArrowDown') {
      e.preventDefault();
      setOpen(true);
      setActiveIndex(i => Math.min(i + 1, Math.max(flat.length - 1, 0)));
    } else if (e.key === 'ArrowUp') {
      e.preventDefault();
      setActiveIndex(i => Math.max(i - 1, 0));
    } else if (e.key === 'Enter') {
      const hit = flat[activeIndex];
      if (hit) commit(hit);
    } else if (e.key === 'Escape') {
      e.preventDefault();
      if (raw) setRaw('');
      else close();
    }
  }

  const dropdownWidthCls = width === 'fixed'
    ? cn('left-1/2 -translate-x-1/2 w-[80vw]')
    : cn('left-0 right-0');

  const wrapperWidthCls = width === 'fixed' ? cn('flex-1 max-w-[720px] mx-auto') : cn('w-full');
  const inputBgCls = cn('bg-card-2');

  return (
    <div ref={wrapperRef} className={cn('relative', wrapperWidthCls, className)}>
      {/* A real 1px border rather than the inset shadow this used to draw: an ad-hoc shadow is a
          DESIGN §2.5 anti-pattern, and it was the only reason the search bar needed a focus recipe
          of its own. It now wears the same one every other framed field does. */}
      <div className={cn(
        'flex items-center gap-2 px-3 py-1.75 text-title border border-border-subtle rounded-md',
        'transition-[border-color,box-shadow] duration-[var(--motion-fast)] ease-[var(--ease-standard)]',
        inputBgCls,
        fieldFocusWithinCls,
      )}>
        <SearchIcon size={14} />
        <input
          ref={inputRef}
          value={raw}
          autoFocus={autoFocus}
          data-testid="search-input"
          onChange={e => { setRaw(e.target.value); setOpen(true); }}
          onFocus={() => setOpen(true)}
          onKeyDown={onKeyDown}
          placeholder={placeholder ?? t`Search traces, agents, suites…`}
          className="flex-1 bg-transparent outline-none text-title placeholder:text-muted text-primary"
        />
        {raw ? (
          <button
            type="button"
            // Clearing lives on onClick so Enter/Space work; onMouseDown only stops the input
            // from blurring before the click lands (which is why it was here in the first place).
            onMouseDown={e => e.preventDefault()}
            onClick={() => { setRaw(''); inputRef.current?.focus(); }}
            data-testid="search-clear-btn"
            className={cn('cursor-pointer p-0.5 text-muted hover:text-primary', FOCUS_RING)}
            aria-label={t`Clear search`}
          >
            <XIcon size={12} />
          </button>
        ) : showShortcut ? (
          <span className="flex gap-0.75">
            <kbd className={kbdCls}>⌘</kbd>
            {/* eslint-disable-next-line lingui/no-unlocalized-strings -- keyboard key label, not UI copy */}
            <kbd className={kbdCls}>K</kbd>
          </span>
        ) : null}
      </div>

      {open && (
        <div className={cn(
          'absolute top-[calc(100%+8px)]',
          dropdownWidthCls,
          'bg-surface-2 border border-border',
          'shadow-[var(--shadow-float)]',
          'z-[100] overflow-hidden',
        )}>
          {!isRecentMode && !searchEnabled && (
            <div className="px-4 py-6 text-body-sm text-muted"><Trans>Type at least 2 characters.</Trans></div>
          )}
          {!isRecentMode && searchEnabled && fetching && hits.length === 0 && (
            <div className="px-4 py-6 text-body-sm text-muted flex items-center gap-2">
              <span className="size-[6px] rounded-full bg-accent pulse-dot" />
              <Trans>Searching…</Trans>
            </div>
          )}
          {!isRecentMode && searchEnabled && !fetching && hits.length === 0 && (
            <div className="px-4 py-6 text-body-sm text-muted"><Trans>No matches for &ldquo;{debounced}&rdquo;.</Trans></div>
          )}
          {isRecentMode && fetching && hits.length === 0 && !recentErrored && (
            <div className="px-4 py-6 text-body-sm text-muted"><Trans>Loading recent…</Trans></div>
          )}
          {isRecentMode && !fetching && !recentErrored && hits.length === 0 && (
            <div className="px-4 py-6 text-body-sm text-muted"><Trans>No recent items.</Trans></div>
          )}
          {isRecentMode && recentErrored && (
            <div className="px-4 py-6 text-body-sm text-muted">
              <Trans>Type at least 2 characters to search.</Trans>
            </div>
          )}

          {hits.length > 0 && (
            <div className="grid grid-cols-[minmax(0,1fr)_minmax(0,1fr)] h-[60vh] min-h-[280px]">
              <div data-testid="search-results" className="min-h-0 overflow-y-auto py-2 border-r border-border-subtle">
                <SearchResultList
                  groupOrder={groupOrder}
                  grouped={grouped}
                  groupOffsets={groupOffsets}
                  activeIndex={activeIndex}
                  isRecentMode={isRecentMode}
                  onSelect={onSelect}
                  onHover={setActiveIndex}
                  onLinkClick={() => { setRaw(''); close(); }}
                  onCommit={commit}
                />
              </div>

              <div className="min-h-0 overflow-y-auto p-4">
                {activeHit ? (
                  <SearchPreview key={`${activeHit.kind}-${activeHit.entityId}`} hit={activeHit} />
                ) : (
                  <div className="text-body-sm text-muted"><Trans>Hover or arrow to preview.</Trans></div>
                )}
              </div>
            </div>
          )}

          <div className="border-t border-border px-4 py-2 flex items-center gap-4 text-body-sm text-muted bg-card-2">
            <span className="flex items-center gap-1.5"><kbd className={kbdCls}>↑↓</kbd> <Trans>navigate</Trans></span>
            <span className="flex items-center gap-1.5"><kbd className={kbdCls}>↵</kbd> {onSelect ? <Trans>pick</Trans> : <Trans>open</Trans>}</span>
            {/* eslint-disable-next-line lingui/no-unlocalized-strings -- keyboard key label, not UI copy */}
            <span className="flex items-center gap-1.5"><kbd className={kbdCls}>esc</kbd> <Trans>close</Trans></span>
            <span className="ml-auto">{hits.length > 0 ? (isRecentMode ? <Trans>{hits.length} recent</Trans> : <Plural value={hits.length} one="# result" other="# results" />) : ''}</span>
          </div>
        </div>
      )}
    </div>
  );
});
