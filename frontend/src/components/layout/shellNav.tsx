import { msg } from '@lingui/core/macro';
import type { MessageDescriptor } from '@lingui/core';
import type { LicenseFeature } from '../../api/license';
import { cn } from '../../lib/cn';

/** The rail's page-code vocabulary: one unique mono two-letter glyph per destination, rendered
 *  where an icon used to sit. Technical glyphs — never translated, never user copy. */
export type NavCode =
  | 'TY' | 'DB' | 'TR' | 'AN' | 'CO' | 'AG' | 'PG' | 'TS'
  | 'EV' | 'EP' | 'RN' | 'PR' | 'AU' | 'SE' | 'DC';

export interface NavEntry {
  label: MessageDescriptor;
  /** Mono two-letter page code the rail renders instead of an icon glyph. */
  code: NavCode;
  to: string;
  requiresFeature?: LicenseFeature;
  /** Only rendered for admin users (backend still enforces authorization). */
  adminOnly?: boolean;
}

export interface NavGroup {
  // Group labels are translated; resolve at render with i18n._(). Item labels stay English
  // (glossary terms — Traces, Agents, Proposals, …).
  label: MessageDescriptor | null;
  items: NavEntry[];
}

// Groups follow the product workflow: Monitor (watch live traffic) → Build (author agents) →
// Improve (the optimization loop, in loop order: suites → evaluators → runs → proposals).
// Tracey sits alone on top — the assistant is cross-cutting, not part of one workflow stage.
export const navGroups: NavGroup[] = [
  {
    label: null,
    items: [
      // eslint-disable-next-line lingui/no-unlocalized-strings -- LicenseFeature enum value, not UI copy
      { label: msg`Tracey AI`, code: 'TY', to: '/tracey-ai', requiresFeature: 'Tracey' },
    ],
  },
  {
    label: msg`Monitor`,
    items: [
      { label: msg`Dashboard`, code: 'DB', to: '/dashboard' },
      { label: msg`Traces`, code: 'TR', to: '/traces' },
      { label: msg`Anomalies`, code: 'AN', to: '/anomalies' },
      // No requiresFeature: the page (and the budget list) is free; only *changing* a budget is licensed.
      { label: msg`Costs`, code: 'CO', to: '/costs' },
    ],
  },
  {
    label: msg`Build`,
    items: [
      { label: msg`Agents`, code: 'AG', to: '/agents' },
      { label: msg`Agent Playground`, code: 'PG', to: '/playground' },
    ],
  },
  {
    label: msg`Improve`,
    items: [
      { label: msg`Test Suites`, code: 'TS', to: '/suites' },
      { label: msg`Evaluators`, code: 'EV', to: '/evaluators' },
      { label: msg`Evaluator Playground`, code: 'EP', to: '/evaluator-playground' },
      { label: msg`Test Runs`, code: 'RN', to: '/runs' },
      // eslint-disable-next-line lingui/no-unlocalized-strings -- LicenseFeature enum value, not UI copy
      { label: msg`Proposals`, code: 'PR', to: '/proposals', requiresFeature: 'OptimizationProposals' },
    ],
  },
];

// Utility destinations pinned to the sidebar footer, below the scrolling groups.
export const footerNavEntries: NavEntry[] = [
  { label: msg`Audit Log`, code: 'AU', to: '/audit-log' },
  { label: msg`Settings`, code: 'SE', to: '/settings', adminOnly: true },
];

/** Page code for the sidebar's Documentation link — not a route, so not a `NavEntry`. */
export const DOCS_NAV_CODE: NavCode = 'DC';

export const navItems: NavEntry[] = [...navGroups.flatMap(g => g.items), ...footerNavEntries];

export type HealthStatus = 'online' | 'offline' | 'connecting';

export const HEALTH_CHIP: Record<HealthStatus, string> = {
  online: cn('bg-success-subtle border-[color-mix(in_srgb,var(--success)_25%,transparent)] text-success'),
  offline: cn('bg-danger-subtle border-[color-mix(in_srgb,var(--danger)_25%,transparent)] text-danger'),
  connecting: cn('bg-warn-subtle border-[color-mix(in_srgb,var(--warn)_25%,transparent)] text-warn'),
};

export const HEALTH_DOT: Record<HealthStatus, string> = {
  online: cn('bg-success'),
  offline: cn('bg-danger'),
  connecting: cn('bg-warn'),
};

export const HEALTH_LABEL: Record<HealthStatus, MessageDescriptor> = {
  online: msg`Online`,
  offline: msg`Offline`,
  connecting: msg`Connecting…`,
};
