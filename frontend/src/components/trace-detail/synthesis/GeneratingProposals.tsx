import { Trans } from '@lingui/react/macro';
import { Spinner } from '../../ui/Spinner';
import { SkeletonList } from '../../ui/Skeleton';
import { ElapsedStopwatch } from '../../ui/ElapsedStopwatch';

/**
 * The waiting state for a round of generation.
 *
 * A round is one blocking model call that reads the whole conversation, so it takes seconds rather
 * than milliseconds — long enough that bare skeleton rows read as "stuck" rather than "working".
 * The running clock is the part that carries: it is the only thing on screen that proves the panel
 * is still waiting on the model and not wedged.
 */
export function GeneratingProposals() {
  return (
    <div className="flex flex-col gap-2" data-testid="synthesize-generating">
      <div className="flex items-center gap-2">
        <Spinner size={12} />
        <span className="text-body-sm text-secondary">
          <Trans>Reading the conversation…</Trans>
        </span>
        <span className="ml-auto"><ElapsedStopwatch /></span>
      </div>
      <SkeletonList rows={3} />
    </div>
  );
}
