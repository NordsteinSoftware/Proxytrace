import React from 'react';
import { cn } from '../../lib/cn';

export type BadgeVariant = 'neutral' | 'success' | 'warn' | 'danger' | 'accent' | 'tinted';
export type BadgeSize = 'sm' | 'md';

interface BadgeProps {
  label: React.ReactNode;
  variant?: BadgeVariant;
  color?: string;
  size?: BadgeSize;
  dot?: boolean;
  selected?: boolean;
  onClick?: () => void;
  className?: string;
  title?: string;
}

const VARIANT_TOKEN: Record<Exclude<BadgeVariant, 'tinted'>, { bg: string; fg: string; border: string }> = {
  neutral: { bg: 'var(--bg-card-2)', fg: 'var(--text-secondary)', border: 'var(--border-color)' },
  success: { bg: 'var(--success-subtle)', fg: 'var(--success)', border: 'color-mix(in srgb, var(--success) 32%, transparent)' },
  warn:    { bg: 'var(--warn-subtle)',    fg: 'var(--warn)',    border: 'color-mix(in srgb, var(--warn) 32%, transparent)' },
  danger:  { bg: 'var(--danger-subtle)',  fg: 'var(--danger)',  border: 'color-mix(in srgb, var(--danger) 32%, transparent)' },
  accent:  { bg: 'var(--accent-subtle)',  fg: 'var(--accent-hover)', border: 'color-mix(in srgb, var(--accent-primary) 32%, transparent)' },
};

const SIZE_CLS: Record<BadgeSize, string> = {
  sm: cn('px-2 py-0.5 text-caption gap-1'),
  md: cn('px-2.5 py-0.75 text-body-sm gap-1.5'),
};

export function Badge({
  label,
  variant = 'neutral',
  color,
  size = 'sm',
  dot = false,
  selected,
  onClick,
  className,
  title,
}: BadgeProps) {
  const tintColor = variant === 'tinted' && color ? color : undefined;
  const palette = tintColor
    ? {
        bg: `color-mix(in srgb, ${tintColor} 14%, transparent)`,
        fg: tintColor,
        border: `color-mix(in srgb, ${tintColor} 32%, transparent)`,
      }
    // eslint-disable-next-line lingui/no-unlocalized-strings -- BadgeVariant token, not UI copy
    : VARIANT_TOKEN[variant === 'tinted' ? 'neutral' : variant];

  const style: React.CSSProperties = {
    background: palette.bg,
    color: palette.fg,
    border: `1px solid ${palette.border}`,
  };

  if (selected) {
    style.outline = `2px solid ${palette.fg}`;
    style.outlineOffset = cn('1px');
  }
  if (onClick) style.cursor = 'pointer';

  return (
    <span
      onClick={onClick}
      title={title}
      className={cn(
        // `max-w-full` + the truncating label span keep a long label (a model id like
        // `deepseek/deepseek-v4-flash`) inside its column instead of spilling over the
        // neighbouring cells of a fixed-track row grid — the caller only has to give the
        // cell a width to fit into (`min-w-0`/`overflow-hidden`).
        'inline-flex items-center font-semibold whitespace-nowrap transition-opacity max-w-full min-w-0',
        SIZE_CLS[size],
        'rounded-none', // Signal Desk: chips are square tags
        className,
      )}
      style={style}
    >
      {dot && (
        <span
          aria-hidden
          className="w-[5px] h-[5px] rounded-full shrink-0"
          style={{ background: palette.fg }}
        />
      )}
      <span className="min-w-0 overflow-hidden text-ellipsis">{label}</span>
    </span>
  );
}
