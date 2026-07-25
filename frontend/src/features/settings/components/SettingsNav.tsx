import { NavLink } from 'react-router';
import { msg } from '@lingui/core/macro';
import { useLingui } from '@lingui/react/macro';
import type { MessageDescriptor } from '@lingui/core';
import { cn } from '../../../lib/cn';

interface NavLeaf {
  label: MessageDescriptor;
  to: string;
  testId: string;
}

interface NavSection {
  heading: MessageDescriptor;
  items: NavLeaf[];
}

// The whole settings area is admin-only (route-gated in App.tsx), so every visitor here is an
// admin — no per-item gating is needed. The split is purely conceptual: settings that belong to
// the active project vs. settings that span the whole workspace.
const SECTIONS: NavSection[] = [
  {
    heading: msg`Project`,
    items: [
      { label: msg`General`, to: '/settings/general', testId: 'settings-nav-general' },
      { label: msg`Members`, to: '/settings/members', testId: 'settings-nav-members' },
      { label: msg`Search indexing`, to: '/settings/search', testId: 'settings-nav-search' },
    ],
  },
  {
    heading: msg`Workspace`,
    items: [
      { label: msg`Projects`, to: '/settings/projects', testId: 'settings-nav-projects' },
      { label: msg`Providers`, to: '/settings/providers', testId: 'settings-nav-providers' },
      { label: msg`Users`, to: '/settings/users', testId: 'settings-nav-users' },
      { label: msg`License`, to: '/settings/license', testId: 'settings-nav-license' },
      { label: msg`Error log`, to: '/settings/error-log', testId: 'settings-nav-error-log' },
      { label: msg`Email notifications`, to: '/settings/email-notifications', testId: 'settings-nav-email-notifications' },
      { label: msg`Outlier detection`, to: '/settings/outlier-detection', testId: 'settings-nav-outlier-detection' },
      { label: msg`Audit log`, to: '/settings/audit-log', testId: 'settings-nav-audit-log' },
      { label: msg`Danger zone`, to: '/settings/danger', testId: 'settings-nav-danger' },
    ],
  },
];

export function SettingsNav() {
  const { i18n } = useLingui();
  return (
    // A plain container, not a <nav> landmark: the app shell's sidebar is the page's single
    // navigation landmark, and a second one would break the smoke test's getByRole('navigation').
    <div
      data-testid="settings-nav"
      className="w-[196px] shrink-0 overflow-y-auto flex flex-col gap-4 pr-1"
    >
      {SECTIONS.map(section => (
        <div key={section.items[0].testId} className="flex flex-col gap-0.5">
          <div className="px-2 pb-1 text-caption font-semibold tracking-[0.08em] text-secondary uppercase">
            {i18n._(section.heading)}
          </div>
          {section.items.map(item => (
            <NavLink
              key={item.to}
              to={item.to}
              data-testid={item.testId}
              className={({ isActive }) =>
                cn(
                  'flex items-center px-3 py-1.5 rounded-md text-title transition-colors cursor-pointer',
                  isActive
                    ? 'bg-[color-mix(in_srgb,_var(--accent-primary)_12%,_transparent)] text-primary font-semibold'
                    : 'text-secondary hover:text-primary hover:bg-[var(--bg-wash-hover)]',
                )
              }
            >
              {i18n._(item.label)}
            </NavLink>
          ))}
        </div>
      ))}
    </div>
  );
}
