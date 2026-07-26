import { msg } from '@lingui/core/macro';
import type { MessageDescriptor } from '@lingui/core';
import type { UpgradeErrorType } from '../../api/client';
import type { LicenseFeature, LicenseSource, LicenseStatus, LicenseTier } from '../../api/license';

/** Human-readable label per license feature flag. */
export const FEATURE_LABELS: Record<LicenseFeature, MessageDescriptor> = {
  OptimizationProposals: msg`Optimization proposals`,
  AgenticEvaluators: msg`Agentic evaluators`,
  CustomEvaluators: msg`Custom evaluators`,
  SsoOidc: msg`SSO / OIDC sign-in`,
  AuditLog: msg`Audit log`,
  Tracey: msg`Tracey AI assistant`,
  ScheduledTestRuns: msg`Scheduled test runs`,
  CustomAnomalyDetectors: msg`Custom anomaly detectors`,
  CostControls: msg`Cost budgets & alerting`,
};

/**
 * Whole days remaining until `endsAt`, never negative. Returns 0 when the date
 * is missing, unparseable, or already in the past. `now` is injectable for tests.
 */
export function daysLeft(endsAt: string | null | undefined, now: number = Date.now()): number {
  if (!endsAt) return 0;
  const end = Date.parse(endsAt);
  if (Number.isNaN(end)) return 0;
  const diffMs = end - now;
  if (diffMs <= 0) return 0;
  return Math.ceil(diffMs / (24 * 60 * 60 * 1000));
}

/** Human-readable label per tier. */
const TIER_LABEL: Record<LicenseTier, MessageDescriptor> = { free: msg`Free`, enterprise: msg`Enterprise` };

/** Human-readable label per license status. */
export const STATUS_LABELS: Record<LicenseStatus, MessageDescriptor> = {
  free: msg`Free tier`,
  active: msg`Active`,
  grace: msg`Grace period`,
  expired: msg`Expired / revoked`,
  invalid: msg`Invalid`,
};

/**
 * Explains where the active license came from, for the settings License page.
 * Returns null when there is nothing worth explaining (no license at all).
 */
export function licenseSourceNote(source: LicenseSource): MessageDescriptor | null {
  switch (source) {
    case 'environment':
      return msg`This license was supplied via the PROXYTRACE_LICENSE environment variable. A key activated here is stored in the database and takes precedence over it.`;
    case 'stored':
      return msg`This license was activated from the UI and is stored in the database. It survives restarts and takes precedence over an environment-supplied license.`;
    case 'override':
      return msg`The license is fixed by this deployment (demo mode) and cannot be changed here.`;
    case 'none':
      return null;
  }
}

/**
 * Visual treatment of the tier chip. Deliberately distinct from the health
 * ("Online") chip so the two don't read as twins: `premium` is the cyan,
 * crowned Enterprise marque; `pending` flags a re-validation in progress;
 * `free` is a muted upgrade affordance.
 */
export type TierTone = 'premium' | 'pending' | 'free';

export interface TierBadge {
  label: MessageDescriptor;
  /** Drives the chip's styling and which icon it carries. */
  tone: TierTone;
  /** Whether the badge should link to the upgrade page (Free only). */
  linkToUpgrade: boolean;
}

/**
 * Maps the license status/tier to the topbar tier chip's label, visual tone,
 * and whether it doubles as an upgrade affordance. Free is always shown (muted,
 * links to upgrade); a licensed install is the cyan "premium" marque when
 * active, or "pending" (amber) while grace/expired re-validation is in flight.
 */
export function tierBadge(status: LicenseStatus, tier: LicenseTier): TierBadge {
  // An invalid configured license runs with Free entitlements — the chip mirrors that;
  // the dedicated invalid-license banner carries the warning.
  if (status === 'free' || status === 'invalid') {
    return { label: TIER_LABEL.free, tone: 'free', linkToUpgrade: true };
  }
  return {
    label: TIER_LABEL[tier],
    tone: status === 'active' ? 'premium' : 'pending',
    linkToUpgrade: false,
  };
}

export interface UpgradeCopy {
  title: MessageDescriptor;
  /** Fallback body when the server did not supply a specific message. */
  fallback: MessageDescriptor;
}

/**
 * The headline + fallback body for the upgrade dialog, keyed on the 402 error
 * type. The server's own message (when present) is shown verbatim; this only
 * frames it and covers the case where no message came back.
 */
export function upgradeCopy(errorType: UpgradeErrorType): UpgradeCopy {
  if (errorType === 'LicenseLimitExceeded') {
    return {
      title: msg`You've reached a Free-tier limit`,
      fallback: msg`This action exceeds a usage limit of your current license tier. Upgrade to Enterprise for unlimited projects, agents, users, and test suites.`,
    };
  }
  return {
    title: msg`This is an Enterprise feature`,
    fallback: msg`This feature is part of the Proxytrace Enterprise tier. Upgrade your license to unlock it.`,
  };
}
