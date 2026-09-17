import { forwardRef } from 'react';
import type { InputHTMLAttributes, ReactNode } from 'react';
import { cn } from '../../lib/cn';
import { CheckIcon } from '../icons';

interface CheckboxProps extends Omit<InputHTMLAttributes<HTMLInputElement>, 'type' | 'size'> {
  label?: ReactNode;
  invalid?: boolean;
}

/**
 * Accessible checkbox: a real (visually-hidden) `<input type="checkbox">` overlaid
 * by a token-styled box. Toggles via the wrapping label.
 */
export const Checkbox = forwardRef<HTMLInputElement, CheckboxProps>(function Checkbox(
  { label, invalid, className, disabled, ...rest },
  ref,
) {
  return (
    <label
      className={cn(
        'inline-flex items-center gap-2 select-none',
        disabled ? 'cursor-not-allowed opacity-50' : 'cursor-pointer',
        className,
      )}
    >
      <span className="relative inline-flex h-4 w-4 shrink-0">
        <input
          ref={ref}
          type="checkbox"
          disabled={disabled}
          data-invalid={invalid || undefined}
          className="peer absolute inset-0 z-10 m-0 cursor-pointer opacity-0 disabled:cursor-not-allowed"
          {...rest}
        />
        <span
          className={cn(
            'pointer-events-none absolute inset-0 rounded-sm border border-border-control bg-card-2',
            'transition-colors duration-[var(--motion-fast)] ease-[var(--ease-standard)]',
            'peer-checked:border-transparent peer-checked:bg-accent',
            'peer-focus-visible:ring-2 peer-focus-visible:ring-[color-mix(in_srgb,var(--accent-primary)_60%,transparent)]',
            'peer-data-[invalid=true]:border-danger',
          )}
        />
        <CheckIcon
          size={11}
          strokeWidth={3}
          className="pointer-events-none absolute inset-0 m-auto text-accent-ink opacity-0 transition-opacity duration-[var(--motion-fast)] peer-checked:opacity-100"
        />
      </span>
      {label != null && <span className="text-title text-secondary">{label}</span>}
    </label>
  );
});
