import type { ReactNode } from 'react';
import { Plural, Trans, useLingui } from '@lingui/react/macro';
import { EvaluatorSuggestionTarget, type EvaluatorSuggestionDto } from '../../../api/models';
import { Badge } from '../../ui/Badge';
import { Input } from '../../ui/Input';
import { Radio, RadioGroup } from '../../ui/Radio';
import { EYEBROW_CLS } from '../../ui/classes';
import { cn } from '../../../lib/cn';

/** What the user decided to do with the agent's judge suggestion. `none` = declined. */
export type JudgeTarget = EvaluatorSuggestionTarget | 'none';

/** The declined answer, named so it reads as a value rather than as copy at the call site. */
const NO_JUDGE: JudgeTarget = 'none';

export interface JudgeChoice {
  target: JudgeTarget;
  newSuiteName: string;
}

interface Props {
  suggestion: EvaluatorSuggestionDto;
  destination: { name: string; caseCount: number; limitReached: boolean };
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
 *
 * The three answers are a radio list rather than a segmented control, and each carries its
 * consequence permanently. A segmented control reads as "interchangeable views of one thing", which
 * these are not: one of them widens what the destination suite grades, and one quietly sends the
 * cases somewhere other than the suite picked at the top of the panel. Showing every consequence at
 * once is what lets the choice be compared instead of discovered — the old card revealed the blast
 * radius only *after* the option was selected, and never mentioned the redirect at all.
 */
export function EvaluatorSuggestionCard({ suggestion, destination, licensed, choice, onChange }: Props) {
  const { t } = useLingui();

  return (
    <div
      className="flex flex-col gap-2 p-3 bg-card-2 shadow-[inset_0_0_0_1px_var(--border-color)]"
      data-testid="synthesis-evaluator-suggestion"
    >
      <span className={EYEBROW_CLS}><Trans>Scoring</Trans></span>
      <p className="text-body-sm text-secondary">{suggestion.reason}</p>
      <p className="text-h2 font-semibold text-primary">{suggestion.name}</p>

      {!licensed ? (
        <p className="text-body-sm text-secondary">
          <Trans>
            Agentic evaluators are not included in your licence, so these cases will be scored by
            the suite's existing evaluators.
          </Trans>
        </p>
      ) : (
        <RadioGroup
          name="synthesis-judge"
          ariaLabel={t`What to do with this judge`}
          value={choice.target}
          onChange={target => onChange({ ...choice, target: target as JudgeTarget })}
        >
          <JudgeOption
            value={EvaluatorSuggestionTarget.Attach}
            testId="synthesis-judge-attach"
            recommended={suggestion.target === EvaluatorSuggestionTarget.Attach}
            title={t`Add the judge to ${destination.name}`}
            consequence={
              <Plural
                value={destination.caseCount}
                one="It will also score the # case already there."
                other="It will also score the # cases already there."
              />
            }
            caution
          />

          {/* The Free tier caps MaxTestSuites at 1, so there is no room for a second suite — offer
              the option only where it can actually succeed rather than letting the server 402. */}
          {!destination.limitReached && (
            <JudgeOption
              value={EvaluatorSuggestionTarget.NewSuite}
              testId="synthesis-judge-new-suite"
              recommended={suggestion.target === EvaluatorSuggestionTarget.NewSuite}
              title={t`Put the cases in a new suite`}
              consequence={t`They go there instead of ${destination.name}, with the judge attached.`}
            />
          )}
          {choice.target === EvaluatorSuggestionTarget.NewSuite && (
            <div className="pl-6">
              <Input
                value={choice.newSuiteName}
                onChange={event => onChange({ ...choice, newSuiteName: event.target.value })}
                placeholder={t`New suite name`}
                aria-label={t`New suite name`}
                data-testid="synthesis-new-suite-name"
              />
            </div>
          )}

          <JudgeOption
            value={NO_JUDGE}
            testId="synthesis-judge-none"
            title={t`Skip the judge`}
            consequence={t`${destination.name}'s current evaluators score the cases.`}
          />
        </RadioGroup>
      )}
    </div>
  );
}

/**
 * One answer: what it does, and what it costs. The consequence is never hidden behind selection —
 * that is the whole point of the list.
 */
function JudgeOption({ value, testId, title, consequence, caution, recommended }: {
  value: string;
  testId: string;
  title: string;
  consequence: ReactNode;
  /** Widens what an existing suite grades — the one outcome that reaches beyond these cases. */
  caution?: boolean;
  recommended?: boolean;
}) {
  return (
    <Radio
      value={value}
      testId={testId}
      align="start"
      label={
        <span className="flex flex-col gap-0.5">
          <span className="flex items-center gap-2 flex-wrap">
            <span className="text-title text-primary">{title}</span>
            {recommended && (
              <Badge label={<Trans>Recommended</Trans>} variant="accent" size="sm" />
            )}
          </span>
          <span className={cn('text-body-sm', caution ? 'text-warn' : 'text-secondary')}>
            {consequence}
          </span>
        </span>
      }
    />
  );
}
