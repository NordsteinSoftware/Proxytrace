import { Plural } from '@lingui/react/macro';
import type { SkippedTurnDto } from '../../../api/models';
import { Collapsible } from '../../ui/Collapsible';

/**
 * The turns the agent deliberately did not propose, with its reasons — what makes "only a subset
 * is relevant" auditable rather than opaque. Collapsed by default: it is context, not the answer.
 */
export function SkippedTurns({ skipped }: { skipped: SkippedTurnDto[] }) {
  if (skipped.length === 0) return null;

  return (
    <div data-testid="synthesis-skipped">
      <Collapsible
        title={
          <span className="text-body-sm text-secondary">
            <Plural value={skipped.length} one="# turn skipped" other="# turns skipped" />
          </span>
        }
        headerClassName="px-1 py-1.5 cursor-pointer"
      >
        <ul className="flex flex-col gap-1 px-4 pb-2">
          {skipped.map(turn => (
            <li key={turn.agentCallId} className="text-body-sm text-muted">{turn.reason}</li>
          ))}
        </ul>
      </Collapsible>
    </div>
  );
}
