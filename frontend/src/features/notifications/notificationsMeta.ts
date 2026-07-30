import { msg } from '@lingui/core/macro';
import type { MessageDescriptor } from '@lingui/core';
import { NotificationKind, NotificationSeverity, NotificationStatus, NotificationTargetKind } from '../../api/models';
import type { BadgeVariant } from '../../components/ui/Badge';

const SEVERITY_VARIANT: Record<NotificationSeverity, BadgeVariant> = {
  [NotificationSeverity.Info]: 'accent',
  [NotificationSeverity.Warning]: 'warn',
  [NotificationSeverity.Critical]: 'danger',
};

const SEVERITY_LABEL: Record<NotificationSeverity, MessageDescriptor> = {
  [NotificationSeverity.Info]: msg`Info`,
  [NotificationSeverity.Warning]: msg`Warning`,
  [NotificationSeverity.Critical]: msg`Critical`,
};

const KIND_LABEL: Record<NotificationKind, MessageDescriptor> = {
  [NotificationKind.Anomaly]: msg`Anomaly`,
  [NotificationKind.ProposalReady]: msg`Proposal ready`,
  [NotificationKind.TraceQuotaReached]: msg`Trace limit reached`,
  [NotificationKind.CostBudget]: msg`Cost budget`,
};

const STATUS_LABEL: Record<NotificationStatus, MessageDescriptor> = {
  [NotificationStatus.Unread]: msg`Unread`,
  [NotificationStatus.Read]: msg`Read`,
  [NotificationStatus.Dismissed]: msg`Dismissed`,
};

/**
 * Deep-link route per target kind. Deliberately **partial**: `NotificationTargetKind` is a backend
 * enum, so a value this build does not know about must degrade to "no link" rather than throw while
 * rendering a row — the notifications menu lives in the topbar, outside every route error boundary.
 */
const TARGET_ROUTE: Partial<Record<NotificationTargetKind, (id: string) => string>> = {
  [NotificationTargetKind.TestRunGroup]: id => `/runs?id=${encodeURIComponent(id)}`,
  [NotificationTargetKind.Agent]: id => `/agents?id=${encodeURIComponent(id)}`,
  [NotificationTargetKind.OptimizationProposal]: id => `/proposals?id=${encodeURIComponent(id)}`,
  [NotificationTargetKind.AgentCall]: id => `/traces?focus=${encodeURIComponent(id)}`,
};

export const severityBadgeVariant = (severity: NotificationSeverity): BadgeVariant =>
  SEVERITY_VARIANT[severity];

export const severityLabel = (severity: NotificationSeverity): MessageDescriptor =>
  SEVERITY_LABEL[severity];

export const kindLabel = (kind: NotificationKind): MessageDescriptor => KIND_LABEL[kind];

export const statusLabel = (status: NotificationStatus): MessageDescriptor => STATUS_LABEL[status];

/** Internal deep-link route for a notification's target, or null when it has none. */
export function targetRoute(kind: NotificationTargetKind | null, id: string | null): string | null {
  if (!kind || !id) return null;
  return TARGET_ROUTE[kind]?.(id) ?? null;
}
