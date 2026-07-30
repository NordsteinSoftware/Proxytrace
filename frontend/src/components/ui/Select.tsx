import React from 'react';
import * as DropdownMenu from '@radix-ui/react-dropdown-menu';
import { cn } from '../../lib/cn';
import { CheckIcon, ChevronDownIcon } from '../icons';
import { formInputCls } from './classes';
import { collectGroups } from './selectOptions';

type Size = 'sm' | 'md';

interface SelectProps {
  value: string;
  onValueChange: (value: string) => void;
  inputSize?: Size;
  invalid?: boolean;
  disabled?: boolean;
  /** Shown on the trigger when no option matches `value`. */
  placeholder?: string;
  autoFocus?: boolean;
  id?: string;
  className?: string;
  /**
   * `<option>` elements, optionally wrapped in `<optgroup label="…">` — kept as the call-site API;
   * parsed into the styled menu.
   */
  children: React.ReactNode;
  'data-testid'?: string;
}

const SIZE_CLS: Record<Size, string> = {
  sm: cn('px-2.5 py-1.5 text-body-sm'),
  md: cn('px-3 py-2 text-title'),
};

/**
 * Single-select control with a styled, dark-theme option list (Radix DropdownMenu-backed:
 * portalled, collision-aware, keyboard-navigable). Replaces the native `<select>` whose option
 * popup the OS rendered. Keeps the `<option>`-children API; emits the chosen value via
 * `onValueChange`.
 */
export function Select({
  value,
  onValueChange,
  // eslint-disable-next-line lingui/no-unlocalized-strings -- size variant token, not UI copy
  inputSize = 'md',
  invalid,
  disabled,
  placeholder,
  autoFocus,
  id,
  className,
  children,
  'data-testid': testId,
}: SelectProps) {
  const groups = collectGroups(children);
  const selected = groups.flatMap(g => g.options).find(o => o.value === value);

  return (
    <DropdownMenu.Root>
      <DropdownMenu.Trigger asChild disabled={disabled}>
        <button
          type="button"
          id={id}
          data-testid={testId}
          data-invalid={invalid || undefined}
          autoFocus={autoFocus}
          className={cn(
            formInputCls,
            SIZE_CLS[inputSize],
            'group inline-flex items-center justify-between gap-2 text-left cursor-pointer',
            !selected && 'text-muted',
            className,
          )}
        >
          <span className="truncate">{selected ? selected.label : placeholder ?? value}</span>
          <ChevronDownIcon
            size={12}
            className="shrink-0 text-muted transition-transform duration-150 group-data-[state=open]:rotate-180"
          />
        </button>
      </DropdownMenu.Trigger>
      <DropdownMenu.Portal>
        <DropdownMenu.Content
          align="start"
          sideOffset={6}
          className="z-[60] min-w-[var(--radix-dropdown-menu-trigger-width)] max-h-[280px] overflow-y-auto bg-card-2 py-1 shadow-[var(--shadow-float)] border border-border"
        >
          {groups.map((group, gi) => (
            <DropdownMenu.Group key={group.label ?? `group-${gi}`}>
              {group.label && (
                <DropdownMenu.Label className="px-2.5 pt-2 pb-1 text-caption font-medium uppercase tracking-wide text-secondary">
                  {group.label}
                </DropdownMenu.Label>
              )}
              {group.options.map((opt, i) => {
                const isSel = opt.value === value;
                return (
                  <DropdownMenu.Item
                    key={opt.value || `opt-${gi}-${i}`}
                    disabled={opt.disabled}
                    textValue={typeof opt.label === 'string' ? opt.label : opt.value}
                    data-testid={testId ? `${testId}-option-${opt.value}` : undefined}
                    onSelect={() => onValueChange(opt.value)}
                    className={cn(
                      'flex items-center gap-2 px-2.5 py-1.5 text-body text-left cursor-pointer outline-none',
                      'transition-colors duration-100 data-[highlighted]:bg-[var(--bg-wash-hover)]',
                      'data-[disabled]:opacity-50 data-[disabled]:cursor-not-allowed',
                      isSel ? 'text-primary' : 'text-secondary',
                    )}
                  >
                    <span className="flex-1 truncate">{opt.label}</span>
                    {isSel && <CheckIcon size={12} strokeWidth={2.5} className="text-accent shrink-0" />}
                  </DropdownMenu.Item>
                );
              })}
            </DropdownMenu.Group>
          ))}
        </DropdownMenu.Content>
      </DropdownMenu.Portal>
    </DropdownMenu.Root>
  );
}
