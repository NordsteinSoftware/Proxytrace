import { Trans, useLingui } from '@lingui/react/macro';
import type { TestCaseProposalSetDto } from '../../../api/models';
import { Button } from '../../ui/Button';
import { EmptyState } from '../../ui/EmptyState';
import { SkeletonList } from '../../ui/Skeleton';
import { SparklesIcon } from '../../icons';
import { ProposalList } from './ProposalList';
import { SkippedTurns } from './SkippedTurns';
import { EvaluatorSuggestionCard, type JudgeChoice } from './EvaluatorSuggestionCard';
import type { ProposalSelection } from './useProposalSelection';

export interface JudgePaneProps {
  choice: JudgeChoice;
  onChange: (choice: JudgeChoice) => void;
  licensed: boolean;
  destination: { caseCount: number; limitReached: boolean };
}

interface Props {
  /** Null until the first generation lands — which is also the idle state. */
  proposals: TestCaseProposalSetDto | null;
  busy: boolean;
  errorMessage: string | null;
  onGenerate: () => void;
  selection: ProposalSelection;
  judge: JudgePaneProps;
  onFocusCall: (agentCallId: string) => void;
}

/** The candidate column: idle, loading, error, empty, or the ranked list plus its scoring card. */
export function ProposalsPane({
  proposals, busy, errorMessage, onGenerate, selection, judge, onFocusCall,
}: Props) {
  const { t } = useLingui();

  return (
    <div className="flex-1 min-h-0 overflow-y-auto flex flex-col gap-2">
      {errorMessage && <p className="text-body-sm text-danger">{errorMessage}</p>}
      {busy && <SkeletonList rows={3} />}

      {!busy && !proposals && (
        <EmptyState
          title={t`Nothing generated yet`}
          description={t`Reads this trace's whole conversation and proposes the test cases worth building.`}
          action={
            <Button
              variant="primary"
              onClick={onGenerate}
              leftIcon={<SparklesIcon size={13} />}
              data-testid="synthesize-generate-btn"
            >
              <Trans>Generate</Trans>
            </Button>
          }
        />
      )}

      {!busy && proposals && proposals.proposals.length === 0 && (
        <EmptyState title={t`No turn here is worth a test`} description={proposals.summary || undefined} />
      )}

      {!busy && proposals && proposals.proposals.length > 0 && (
        <ProposalList
          proposals={proposals.proposals}
          selection={selection}
          onFocusCall={onFocusCall}
        />
      )}

      {!busy && proposals?.evaluatorSuggestion && (
        <EvaluatorSuggestionCard
          suggestion={proposals.evaluatorSuggestion}
          destination={judge.destination}
          licensed={judge.licensed}
          choice={judge.choice}
          onChange={judge.onChange}
        />
      )}

      {!busy && proposals && <SkippedTurns skipped={proposals.skipped} />}
    </div>
  );
}
