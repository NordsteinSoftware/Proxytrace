import { Trans, useLingui } from '@lingui/react/macro';
import { Button } from '../../ui/Button';
import { Input } from '../../ui/Input';
import { ResetIcon } from '../../icons';

interface Props {
  value: string;
  onChange: (value: string) => void;
  onRegenerate: () => void;
  busy: boolean;
  roundsUsed: number;
  maxRounds: number;
}

/**
 * The refinement loop: a free-text request plus regenerate. The instruction becomes the next turn
 * of a real conversation, so the agent revises its previous answer rather than restarting — which
 * is why the round budget is finite and shown.
 */
export function InstructionBar({ value, onChange, onRegenerate, busy, roundsUsed, maxRounds }: Props) {
  const { t } = useLingui();
  const exhausted = roundsUsed >= maxRounds;

  return (
    <div className="flex items-center gap-2 pt-3 border-t border-hairline shrink-0">
      <Input
        value={value}
        onChange={event => onChange(event.target.value)}
        placeholder={t`e.g. test that issue_refund is called with order_id=91`}
        aria-label={t`Refine the proposed test cases`}
        disabled={busy || exhausted}
        data-testid="synthesis-instruction-input"
        className="flex-1 min-w-0"
      />
      <span className="text-caption text-muted whitespace-nowrap">
        <Trans>Round {roundsUsed} of {maxRounds}</Trans>
      </span>
      <Button
        variant="secondary"
        size="sm"
        onClick={onRegenerate}
        disabled={busy || exhausted}
        loading={busy}
        leftIcon={<ResetIcon size={12} />}
        title={exhausted ? t`Round limit reached — close and generate again.` : undefined}
        data-testid="synthesis-regenerate-btn"
      >
        <Trans>Refine</Trans>
      </Button>
    </div>
  );
}
