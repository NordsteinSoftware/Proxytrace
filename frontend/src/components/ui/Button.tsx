import React from 'react';
import { Slot } from '@radix-ui/react-slot';
import { cn } from '../../lib/cn';
import { FOCUS_RING } from '../../lib/constants';
import { Spinner } from './Spinner';

export type ButtonVariant =
  | 'primary'
  | 'secondary'
  | 'ghost'
  | 'danger'
  | 'dangerOutline'
  | 'success'
  | 'link';
export type ButtonSize = 'sm' | 'md' | 'lg';

interface ButtonProps extends React.ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: ButtonVariant;
  size?: ButtonSize;
  loading?: boolean;
  leftIcon?: React.ReactNode;
  rightIcon?: React.ReactNode;
  fullWidth?: boolean;
  /** Render the single child element as the button (e.g. an `<a>` / router `<Link>`) with button styling. */
  asChild?: boolean;
}

const VARIANT_CLS: Record<ButtonVariant, string> = {
  primary: cn(
    'bg-accent text-accent-ink hover:bg-[var(--accent-hover)] active:bg-[var(--accent-press)] disabled:opacity-40 disabled:cursor-not-allowed',
  ),
  secondary: cn(
    'bg-card-2 text-secondary border border-border-control hover:text-primary hover:bg-[var(--bg-wash-active)] disabled:opacity-40 disabled:cursor-not-allowed',
  ),
  ghost: cn(
    'text-secondary hover:text-primary hover:bg-[var(--bg-wash-hover)] border border-transparent disabled:opacity-40 disabled:cursor-not-allowed',
  ),
  danger: cn(
    'bg-danger text-danger-ink hover:opacity-90 disabled:opacity-40 disabled:cursor-not-allowed',
  ),
  dangerOutline: cn(
    'bg-transparent border border-[color-mix(in_srgb,var(--danger)_30%,transparent)] text-danger hover:bg-danger-subtle disabled:opacity-40 disabled:cursor-not-allowed',
  ),
  success: cn(
    'bg-success text-success-ink hover:opacity-92 disabled:opacity-40 disabled:cursor-not-allowed',
  ),
  link: cn(
    'bg-transparent text-accent hover:text-accent-hover hover:underline shadow-none gap-1 disabled:opacity-40 disabled:cursor-not-allowed',
  ),
};

const SIZE_CLS: Record<ButtonSize, string> = {
  sm: cn('px-2.5 py-1.5 text-body-sm gap-1.5 rounded-md'),
  md: cn('px-3.5 py-2 text-title gap-2 rounded-md'),
  lg: cn('px-5 py-2.5 text-title gap-2 rounded-md'),
};

/** Variants that mutate server state — tagged `data-write` so kiosk mode can disable them. */
const WRITE_VARIANTS: ButtonVariant[] = ['primary', 'danger', 'dangerOutline', 'success'];

const BASE_CLS = cn(
  'inline-flex items-center justify-center font-semibold whitespace-nowrap select-none',
  'transition-[background,color,opacity,box-shadow] duration-[var(--motion-base)] ease-[var(--ease-standard)]',
  FOCUS_RING,
  'focus-visible:ring-offset-0',
);

export const Button = React.forwardRef<HTMLButtonElement, ButtonProps>(function Button(
  {
    variant = 'primary',
    size = 'md',
    loading = false,
    leftIcon,
    rightIcon,
    fullWidth,
    asChild = false,
    disabled,
    className,
    children,
    type = 'button',
    ...rest
  },
  ref,
) {
  const isWriteVariant = WRITE_VARIANTS.includes(variant);
  const classes = cn(
    BASE_CLS,
    variant === 'link' ? undefined : SIZE_CLS[size],
    VARIANT_CLS[variant],
    fullWidth && 'w-full',
    className,
  );

  // asChild renders the consumer's single child element (anchor / router Link) with button styling.
  if (asChild) {
    return (
      <Slot data-write={isWriteVariant || undefined} className={classes} {...rest}>
        {children}
      </Slot>
    );
  }

  const isDisabled = disabled || loading;
  const spinnerSize = size === 'sm' ? 12 : 16;
  return (
    <button
      ref={ref}
      type={type}
      disabled={isDisabled}
      aria-busy={loading || undefined}
      data-write={isWriteVariant || undefined}
      className={classes}
      {...rest}
    >
      {loading ? <Spinner size={spinnerSize} /> : leftIcon}
      {children}
      {!loading && rightIcon}
    </button>
  );
});

interface IconButtonProps extends React.ButtonHTMLAttributes<HTMLButtonElement> {
  danger?: boolean;
  size?: 'sm' | 'md';
}

export const IconButton = React.forwardRef<HTMLButtonElement, IconButtonProps>(function IconButton(
  { danger, size = 'md', className, type = 'button', ...rest },
  ref,
) {
  return (
    <button
      ref={ref}
      type={type}
      data-write={danger || undefined}
      // `.btn-icon` is plain CSS in index.css with no focus rule; the ring is applied here as a
      // Tailwind utility instead so a call site can still override it (an unlayered index.css rule
      // would beat every utility). Every icon-only button in the product inherits it.
      className={cn('btn-icon', FOCUS_RING, size === 'sm' && 'btn-icon-sm', danger && 'btn-icon-danger', className)}
      {...rest}
    />
  );
});
