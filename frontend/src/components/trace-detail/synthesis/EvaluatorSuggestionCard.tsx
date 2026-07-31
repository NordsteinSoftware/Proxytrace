import { Plural, Trans, useLingui } from '@lingui/react/macro';
import { EvaluatorSuggestionTarget, type EvaluatorSuggestionDto } from '../../../api/models';
import { Button } from '../../ui/Button';
import { Input } from '../../ui/Input';
import { SegmentedControl, type Segment } from '../../ui/SegmentedControl';
import { EYEBROW_CLS } from '../../ui/classes';

/** What the user decided to do with the agent's judge suggestion. `none` = declined. */
export type JudgeTarget = EvaluatorSuggestionTarget | 'none';

export interface JudgeChoice {
  target: JudgeTarget;
  newSuiteName: string;
}

interface Props {
  suggestion: EvaluatorSuggestionDto;
  destination: { caseCount: number; limitReached: boolean };
  licensed: boolean;
  choice: JudgeChoice;
  onChange: (choice: JudgeChoice) => void;
}

/**
 * The agent's proposal for how to SCORE these cases, approved separately from the cases themselves.
 *
 * A suite's evaluators apply to every case in it and a case passes only when EVERY attached
 * evaluator passes, so attaching a judge is never a local change. The card says that out loud
 * rather than letting the user discover it in the next run.
 */
export function EvaluatorSuggestionCard({ suggestion, destination, licensed, choice, onChange }: Props) {
  const { t } = useLingui();

  const segments: Segment<JudgeTarget>[] = [
    { value: 'none', label: t`No judge`, testId: 'synthesis-judge-none' },
    {
      value: EvaluatorSuggestionTarget.Attach,
      label: t`Add to this suite`,
      testId: 'synthesis-judge-attach',
    },
    // The Free tier caps MaxTestSuites at 1, so there is no room for a second suite — offer the
    // option only where it can actually succeed rather than letting the server 402 the click.
    ...(destination.limitReached
      ? []
      : [{
        value: EvaluatorSuggestionTarget.NewSuite,
        label: t`Put in a new suite`,
        testId: 'synthesis-judge-new-suite',
      } as Segment<JudgeTarget>]),
  ];

  return (
    <div
      className="flex flex-col gap-2 p-3 bg-card-2 shadow-[inset_0_0_0_1px_var(--border-color)]"
      data-testid="synthesis-evaluator-suggestion"
    >
      <span className={EYEBROW_CLS}><Trans>Scoring</Trans></span>
      <p className="text-body-sm text-secondary">{suggestion.reason}</p>
      <p className="text-body font-semibold text-primary">{suggestion.name}</p>

      {!licensed ? (
        <p className="text-body-sm text-muted">
          <Trans>
            Agentic evaluators are not included in your licence, so these cases will be scored by
            the suite's existing evaluators.
          </Trans>
        </p>
      ) : (
        <>
          <SegmentedControl
            value={choice.target}
            onChange={target => onChange({ ...choice, target })}
            segments={segments}
          />
          {choice.target === EvaluatorSuggestionTarget.Attach && (
            <p className="text-body-sm text-warn">
              <Plural
                value={destination.caseCount}
                one="This judge will also score the # case already in this suite."
                other="This judge will also score the # cases already in this suite."
              />
            </p>
          )}
          {choice.target === EvaluatorSuggestionTarget.NewSuite && (
            <Input
              value={choice.newSuiteName}
              onChange={event => onChange({ ...choice, newSuiteName: event.target.value })}
              placeholder={t`New suite name`}
              aria-label={t`New suite name`}
              data-testid="synthesis-new-suite-name"
            />
          )}
          {choice.target !== 'none' && (
            <Button
              variant="link"
              size="sm"
              onClick={() => onChange({ ...choice, target: 'none' })}
              className="self-start"
              data-testid="synthesis-judge-decline-btn"
            >
              <Trans>Add the cases without this judge</Trans>
            </Button>
          )}
        </>
      )}
    </div>
  );
}
