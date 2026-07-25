import { Link } from 'react-router';
import { useLingui } from '@lingui/react/macro';
import { CrownIcon, SparklesIcon } from '../icons';
import { cn } from '../../lib/cn';
import { useLicense } from '../../hooks/useLicense';
import { tierBadge, type TierTone } from './licenseUtils';

// The tier chip sits next to the health ("Online") chip but must NOT read as its
// twin. The health chip owns the "tinted fill + live status dot" language; the
// tier chip instead leads with a tier icon (crown for Enterprise, sparkles for
// Free) and carries no status dot. Geometry matches the health chip so the two
// align, but fill + icon set them apart.
const CHIP_BASE = cn(
  'inline-flex items-center gap-1.5 px-2.5 py-1.5 rounded-none border text-body-sm font-semibold whitespace-nowrap shrink-0',
);

const TONE_CLS: Record<TierTone, string> = {
  // Enterprise, active — the premium marque: a flat cyan-tinted fill,
  // set apart from the health chip by fill and icon.
  premium: cn(
    'text-accent-hover',
    'bg-accent-subtle',
    'border-[color-mix(in_srgb,var(--accent-primary)_40%,transparent)]',
  ),
  // Enterprise, grace/expired — amber, flags a re-validation in flight.
  pending: cn(
    'text-warn',
    'bg-[color-mix(in_srgb,var(--warn)_13%,transparent)]',
    'border-[color-mix(in_srgb,var(--warn)_34%,transparent)]',
  ),
  // Free — a muted, dashed ghost chip that brightens toward the accent on hover,
  // reading as an "upgrade" affordance rather than a status.
  free: cn(
    'text-muted bg-transparent border-dashed border-hairline cursor-pointer transition-colors',
    'hover:text-accent-hover hover:bg-accent-subtle',
    'hover:border-[color-mix(in_srgb,var(--accent-primary)_40%,transparent)]',
  ),
};

/**
 * Tier chip shown beside the health chip in the top bar. Always visible: a
 * licensed install shows its crowned tier (cyan when active, amber while a
 * re-check is pending), and a Free install shows a muted, sparkle-marked chip
 * that links to the upgrade page so the current tier is always communicated.
 */
export function LicenseBadge() {
  const { t, i18n } = useLingui();
  const { data } = useLicense();
  if (!data) return null;

  const badge = tierBadge(data.status, data.tier);
  const label = i18n._(badge.label);
  // Free carries the "upgrade" sparkle; every licensed tier wears the crown.
  const Icon = badge.linkToUpgrade ? SparklesIcon : CrownIcon;

  const chip = (
    <span className={cn(CHIP_BASE, TONE_CLS[badge.tone])}>
      <Icon size={13} aria-hidden />
      {label}
    </span>
  );

  if (badge.linkToUpgrade) {
    return (
      <Link to="/upgrade" data-testid="license-badge" aria-label={t`${label} tier — upgrade`}>
        {chip}
      </Link>
    );
  }

  return (
    <span data-testid="license-badge" aria-label={t`${label} tier`}>
      {chip}
    </span>
  );
}
