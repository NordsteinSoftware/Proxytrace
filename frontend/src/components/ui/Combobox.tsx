import { useMemo, useState } from 'react';
import type { ReactNode } from 'react';
import * as Popover from '@radix-ui/react-popover';
import { Trans, useLingui } from '@lingui/react/macro';
import { cn } from '../../lib/cn';
import { Input } from './Input';
import { ChevronDownIcon, SearchIcon } from '../icons';
import { formInputCls } from './classes';

interface ComboboxProps<T> {
  value: string | null;
  onChange: (value: string) => void;
  items: readonly T[];
  itemKey: (item: T) => string;
  itemLabel: (item: T) => string;
  renderItem?: (item: T) => ReactNode;
  placeholder?: string;
  searchPlaceholder?: string;
  invalid?: boolean;
  disabled?: boolean;
  inputSize?: 'sm' | 'md';
  'aria-label'?: string;
  'data-testid'?: string;
}

/**
 * Searchable select (Radix Popover-backed). Trigger shows the selected label;
 * opening reveals a search box + filtered option list. For free-text fields use
 * a plain `Input`; for short static lists use the native `Select`.
 */
export function Combobox<T>({
  value,
  onChange,
  items,
  itemKey,
  itemLabel,
  renderItem,
  placeholder,
  searchPlaceholder,
  invalid,
  disabled,
  // eslint-disable-next-line lingui/no-unlocalized-strings -- size variant token, not UI copy
  inputSize = 'md',
  'aria-label': ariaLabel,
  'data-testid': testId,
}: ComboboxProps<T>) {
  const { t } = useLingui();
  const placeholderText = placeholder ?? t`Select…`;
  const searchPlaceholderText = searchPlaceholder ?? t`Search…`;
  const [open, setOpen] = useState(false);
  const [query, setQuery] = useState('');

  const selected = useMemo(() => items.find(i => itemKey(i) === value) ?? null, [items, value, itemKey]);
  const matches = useMemo(() => {
    const q = query.trim().toLowerCase();
    return q ? items.filter(i => itemLabel(i).toLowerCase().includes(q)) : items;
  }, [items, query, itemLabel]);

  const select = (item: T) => {
    onChange(itemKey(item));
    setOpen(false);
    setQuery('');
  };

  const sizeCls = inputSize === 'sm' ? cn('px-2.5 py-1.5 text-body-sm') : cn('px-3 py-2 text-title');

  return (
    <Popover.Root open={open} onOpenChange={setOpen}>
      <Popover.Trigger asChild>
        <button
          type="button"
          disabled={disabled}
          aria-label={ariaLabel}
          data-testid={testId}
          data-invalid={invalid || undefined}
          className={cn(formInputCls, sizeCls, 'flex items-center justify-between gap-2 text-left cursor-pointer')}
        >
          <span className={cn('truncate', !selected && 'text-muted')}>
            {selected ? itemLabel(selected) : placeholderText}
          </span>
          <ChevronDownIcon size={13} className="shrink-0 text-muted" />
        </button>
      </Popover.Trigger>
      <Popover.Portal>
        <Popover.Content
          align="start"
          sideOffset={6}
          className={cn(
            'z-[80] w-[var(--radix-popover-trigger-width)] rounded-lg border border-hairline bg-surface-2 p-1',
            'shadow-[var(--shadow-float)]',
          )}
        >
          <div className="relative mb-1">
            <SearchIcon size={13} className="absolute left-2.5 top-1/2 -translate-y-1/2 text-muted pointer-events-none" />
            <Input
              // eslint-disable-next-line lingui/no-unlocalized-strings -- size variant token, not UI copy
              inputSize="sm"
              autoFocus
              value={query}
              onChange={e => setQuery(e.target.value)}
              placeholder={searchPlaceholderText}
              className="pl-8"
            />
          </div>
          <div role="listbox" className="max-h-[240px] overflow-y-auto">
            {matches.length === 0 ? (
              <div className="px-2.5 py-2 text-body-sm text-muted"><Trans>No matches.</Trans></div>
            ) : (
              matches.map(item => {
                const key = itemKey(item);
                const isSel = key === value;
                return (
                  <button
                    key={key}
                    type="button"
                    role="option"
                    aria-selected={isSel}
                    data-testid={testId ? `${testId}-option-${key}` : undefined}
                    onClick={() => select(item)}
                    className={cn(
                      'w-full flex items-center gap-2 px-2.5 py-2 text-body text-left rounded-md cursor-pointer',
                      'transition-colors duration-100 hover:bg-[var(--bg-wash-hover)]',
                      isSel ? 'text-primary' : 'text-secondary',
                    )}
                  >
                    {renderItem ? renderItem(item) : <span className="truncate">{itemLabel(item)}</span>}
                  </button>
                );
              })
            )}
          </div>
        </Popover.Content>
      </Popover.Portal>
    </Popover.Root>
  );
}
