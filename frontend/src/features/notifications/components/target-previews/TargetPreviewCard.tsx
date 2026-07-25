import type { ReactNode } from 'react';
import { Link } from 'react-router';
import { Trans } from '@lingui/react/macro';
import { Card } from '../../../../components/ui/Card';
import { Button } from '../../../../components/ui/Button';
import { Skeleton } from '../../../../components/ui/Skeleton';
import { ChevronRightIcon } from '../../../../components/icons';
import { EYEBROW_CLS } from '../../../../components/ui/classes';

/**
 * Loading → the target is being fetched; missing → it could not be fetched (almost always a 404,
 * because the target was deleted — `TargetId` is a soft reference); ready → render children.
 */
export type TargetPreviewState = 'loading' | 'missing' | 'ready';

interface TargetPreviewCardProps {
  /** What kind of thing this notification is about, e.g. "Test run". */
  eyebrow: string;
  state: TargetPreviewState;
  title?: string;
  /** In-app route to the target. Hidden while loading and when the target is gone. */
  route?: string | null;
  ctaLabel?: string;
  /** Label/value rows — see {@link TargetPreviewRow}. */
  children?: ReactNode;
}

/**
 * Shared shell for every notification target preview: eyebrow, title, label/value rows and a CTA
 * to the target's own page, plus the two states each preview must handle — loading, and the target
 * no longer existing (a notification outlives what it points at).
 */
export function TargetPreviewCard({ eyebrow, state, title, route, ctaLabel, children }: TargetPreviewCardProps) {
  return (
    <Card elevation="flat" padding="md" className="flex flex-col gap-2.5">
      <span className={EYEBROW_CLS}>{eyebrow}</span>

      {state === 'loading' ? (
        <div data-testid="notification-target-loading" className="flex flex-col gap-2">
          <Skeleton height={16} width="60%" />
          <Skeleton height={12} width="40%" />
        </div>
      ) : state === 'missing' ? (
        <p data-testid="notification-target-missing" className="text-body text-muted">
          <Trans>This item could not be loaded — it may have been deleted since the notification was raised.</Trans>
        </p>
      ) : (
        <>
          {title && (
            <span data-testid="notification-target-title" className="text-h2 font-semibold text-primary break-words">
              {title}
            </span>
          )}
          {children && <div className="flex flex-col gap-1.5">{children}</div>}
          {route && ctaLabel && (
            <Button asChild variant="link" className="self-start px-0 text-body-sm">
              <Link to={route} data-testid="notification-target-cta" className="inline-flex items-center gap-1">
                {ctaLabel}
                <ChevronRightIcon size={11} />
              </Link>
            </Button>
          )}
        </>
      )}
    </Card>
  );
}

/** One label/value line inside a {@link TargetPreviewCard}. */
export function TargetPreviewRow({ label, value }: { label: string; value: ReactNode }) {
  return (
    <div className="flex items-baseline justify-between gap-3">
      <span className="text-body-sm text-secondary shrink-0">{label}</span>
      <span className="text-body text-primary min-w-0 text-right break-words">{value}</span>
    </div>
  );
}
