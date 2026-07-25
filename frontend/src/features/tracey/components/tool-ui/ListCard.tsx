import type { ReactNode } from 'react';
import { Link } from 'react-router';
import { Trans } from '@lingui/react/macro';
import { Card } from '../../../../components/ui/Card';
import { Badge } from '../../../../components/ui/Badge';
import { Skeleton } from '../../../../components/ui/Skeleton';
import { ArrowUpRightIcon } from '../../../../components/icons';
import { ToolUIFrame } from './ToolUIFrame';
import type { ToolUIState } from './tool-ui-state';

/** Max rows a list card renders inline before deferring the rest to its "View all" route. */
export const LIST_CARD_MAX = 6;

interface ListCardProps {
  state: ToolUIState;
  icon: ReactNode;
  /** Plural noun for the collection, e.g. "Agents". */
  title: string;
  count: number;
  /** Route the "View all" header link opens. */
  viewAllTo: string;
  pendingLabel: string;
  emptyLabel: string;
  testId: string;
  /** Rendered rows (already capped by the caller); the footer notes any overflow. */
  children: ReactNode;
  /** How many rows `children` actually contains, to drive the "+N more" footer. */
  shown: number;
}

/**
 * Shared chrome for a list tool result (agents, runs, proposals, …): a titled card with a count
 * badge and a "View all" link, a hairline-divided row list, and an overflow footer. Pending and
 * error states defer to {@link ToolUIFrame} so every tool UI behaves alike.
 */
export function ListCard({
  state,
  icon,
  title,
  count,
  viewAllTo,
  pendingLabel,
  emptyLabel,
  testId,
  children,
  shown,
}: ListCardProps) {
  if (state === 'error') {
    return <ToolUIFrame state="error" testId={testId} />;
  }
  if (state === 'pending') {
    return <ListCardSkeleton icon={icon} testId={testId} pendingLabel={pendingLabel} />;
  }
  const hidden = count - shown;
  return (
    <Card elevation="flat" padding="none" className="fade-up my-1" data-testid={testId}>
      <div className="flex items-center gap-2 border-b border-hairline px-3 py-2.5">
        <span className="shrink-0 text-muted">{icon}</span>
        <span className="text-h2 font-semibold leading-tight text-primary">{title}</span>
        <Badge label={String(count)} variant="neutral" size="sm" />
        {count > 0 && (
          <Link
            to={viewAllTo}
            className="ml-auto inline-flex items-center gap-1 rounded-sm text-body-sm text-muted transition-colors hover:text-primary focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[color-mix(in_srgb,var(--accent-primary)_60%,transparent)]"
          >
            <Trans>View all</Trans>
            <ArrowUpRightIcon size={13} />
          </Link>
        )}
      </div>

      {count === 0 ? (
        <div className="px-3 py-4 text-body-sm text-muted">{emptyLabel}</div>
      ) : (
        <div className="divide-y divide-border-subtle">{children}</div>
      )}

      {hidden > 0 && (
        <Link
          to={viewAllTo}
          className="block border-t border-hairline px-3 py-2 text-center text-body-sm text-muted transition-colors hover:text-primary focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[color-mix(in_srgb,var(--accent-primary)_60%,transparent)]"
        >
          <Trans>+{hidden} more</Trans>
        </Link>
      )}
    </Card>
  );
}

/** Loading placeholder matching {@link ListCard}'s header + row layout. */
function ListCardSkeleton({
  icon,
  testId,
  pendingLabel,
}: {
  icon: ReactNode;
  testId: string;
  pendingLabel: string;
}) {
  return (
    <Card
      elevation="flat"
      padding="none"
      className="streaming-border my-1"
      data-testid={testId}
      aria-busy={true}
      aria-label={pendingLabel}
    >
      <div className="flex items-center gap-2 border-b border-hairline px-3 py-2.5">
        <span className="shrink-0 text-muted">{icon}</span>
        <Skeleton width={88} height={14} />
      </div>
      <div className="divide-y divide-border-subtle">
        {Array.from({ length: 4 }, (_, i) => (
          <div key={i} className="flex min-h-[44px] items-center gap-2.5 px-3 py-2">
            <Skeleton variant="circle" width={6} height={6} />
            <div className="flex flex-1 flex-col gap-1">
              <Skeleton width="48%" height={11} />
              <Skeleton width="30%" height={9} />
            </div>
            <Skeleton width={40} height={16} />
          </div>
        ))}
      </div>
    </Card>
  );
}
