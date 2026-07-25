import { Link } from 'react-router';
import { useLingui } from '@lingui/react/macro';
import { LockIcon } from '../icons';

/**
 * A nav row whose feature is not licensed. Stays visible (it advertises the
 * Enterprise feature) but is dimmed, shows a lock, and routes to /upgrade
 * instead of the gated page.
 */
export function LockedNavItem({ label, code, collapsed }: {
  label: string;
  /** Mono two-letter page code rendered in place of an icon glyph (terminal-style rail). */
  code: string;
  collapsed: boolean;
}) {
  const { t } = useLingui();
  return (
    <Link
      to="/upgrade"
      title={collapsed ? t`${label} (Enterprise)` : t`Requires Enterprise`}
      data-testid={`nav-locked-${label.toLowerCase().replace(/\s+/g, '-')}`}
      className={`nav-item opacity-60${collapsed ? ' justify-center' : ''}`}
    >
      {/* Hidden from a11y: the visible label (or the `title` when collapsed) is the name. */}
      <span aria-hidden="true" className="font-mono text-caption w-[18px] shrink-0 text-center text-muted">
        {code}
      </span>
      {!collapsed && (
        <>
          <span className="flex-1 text-left">{label}</span>
          <LockIcon size={13} className="shrink-0 text-muted" />
        </>
      )}
    </Link>
  );
}
