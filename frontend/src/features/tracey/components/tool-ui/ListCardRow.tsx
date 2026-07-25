import type { ReactNode } from 'react';
import { Link } from 'react-router';
import { ArrowUpRightIcon } from '../../../../components/icons';

interface ListCardRowProps {
  /** In-app route this row opens. */
  to: string;
  /** Per-entity accent for the leading dot (agentColor / modelColor / …). */
  color?: string;
  title: string;
  subtitle?: ReactNode;
  /** Right-aligned metadata (status badge, pass rate, timestamp, …). */
  right?: ReactNode;
}

/**
 * One linked row inside a {@link ListCard}: a leading entity dot, a title + subtitle, optional
 * right-aligned meta, and a hover-revealed navigate arrow. The whole row is an anchor, so it is
 * keyboard-focusable and supports open-in-new-tab.
 */
export function ListCardRow({ to, color, title, subtitle, right }: ListCardRowProps) {
  return (
    <Link
      to={to}
      className="group flex min-h-[44px] items-center gap-2.5 px-3 py-2 transition-colors duration-[var(--motion-fast)] hover:bg-[var(--bg-wash-hover)] focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-[color-mix(in_srgb,var(--accent-primary)_60%,transparent)]"
    >
      {color && (
        <span aria-hidden className="size-1.5 shrink-0 rounded-full" style={{ background: color }} />
      )}
      <div className="min-w-0 flex-1">
        <div className="truncate text-title text-primary">{title}</div>
        {subtitle && <div className="truncate text-body-sm text-muted">{subtitle}</div>}
      </div>
      {right && <div className="shrink-0 text-right">{right}</div>}
      <ArrowUpRightIcon
        size={13}
        aria-hidden
        className="shrink-0 text-muted opacity-0 transition-opacity duration-[var(--motion-fast)] group-hover:opacity-100 group-focus-visible:opacity-100"
      />
    </Link>
  );
}
