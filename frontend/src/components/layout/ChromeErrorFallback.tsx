import { Trans } from '@lingui/react/macro';
import { cn } from '../../lib/cn';

/**
 * Fallback for an app-chrome `ErrorBoundary` (the nav rail, the masthead). The default boundary
 * message is a full-height block, which would blow out a 48px bar — this fills the slot instead,
 * so a broken control degrades to a quiet strip while the rest of the app stays usable. It clears
 * itself on the next navigation (the boundary's `resetKeys`), so it carries no retry affordance.
 */
export function ChromeErrorFallback({ className }: { className?: string }) {
  return (
    <div
      data-testid="chrome-error-fallback"
      role="status"
      className={cn('flex items-center justify-center px-3 text-body-sm text-muted', className)}
    >
      <Trans>This part of the app didn't load.</Trans>
    </div>
  );
}
