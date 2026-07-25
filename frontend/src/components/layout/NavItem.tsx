import { NavLink } from 'react-router';
import { cn } from '../../lib/cn';

interface NavItemProps {
  label: string;
  /** Mono two-letter page code rendered in place of an icon glyph (terminal-style rail). */
  code: string;
  to: string;
  badge?: string;
  badgeAccent?: boolean;
  collapsed: boolean;
}

export function NavItem({ label, code, to, badge, badgeAccent, collapsed }: NavItemProps) {
  return (
    <NavLink
      to={to}
      title={collapsed ? label : undefined}
      className={({ isActive }) => `nav-item${isActive ? ' nav-active' : ''}${collapsed ? ' justify-center' : ''}`}
    >
      {({ isActive }) => (
        <>
          {isActive && (
            <span className="absolute left-0 top-2 bottom-2 w-[2px] bg-accent" />
          )}
          {/* Hidden from a11y: the visible label (or the `title` when collapsed) is the name. */}
          <span
            aria-hidden="true"
            className={cn(
              'font-mono text-caption w-[18px] shrink-0 text-center',
              isActive ? 'text-accent' : 'text-muted',
            )}
          >
            {code}
          </span>
          {!collapsed && (
            <>
              <span className="flex-1 text-left">{label}</span>
              {badge && (
                <span
                  className={cn(
                    'text-caption font-semibold px-1.5 py-0.5 rounded-none min-w-[18px] text-center',
                    badgeAccent ? 'bg-accent text-accent-ink' : 'bg-card-2 text-secondary',
                  )}
                >
                  {badge}
                </span>
              )}
            </>
          )}
        </>
      )}
    </NavLink>
  );
}
