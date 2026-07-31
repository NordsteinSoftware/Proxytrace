import type { ToolCallMessagePartComponent } from '@assistant-ui/react';
import { useLingui } from '@lingui/react/macro';
import { TestCaseProposalKind } from '../../../../api/models';
import { FlaskIcon } from '../../../../components/icons';
import { Badge } from '../../../../components/ui/Badge';
import { ListCard, LIST_CARD_MAX } from './ListCard';
import { ListCardRow } from './ListCardRow';
import { useArtifactResult } from '../../useArtifact';

/** Inline renderer for the `propose_test_cases` tool result. */
export const ProposedCasesToolUI: ToolCallMessagePartComponent = ({ result, status, isError }) => {
  const { t } = useLingui();
  // eslint-disable-next-line lingui/no-unlocalized-strings -- artifact kind token, not UI copy
  const { state, data } = useArtifactResult('test-case-proposals', result, status, isError);
  const proposals = data?.proposals ?? [];

  return (
    <ListCard
      state={state}
      icon={<FlaskIcon size={14} />}
      title={t`Proposed test cases`}
      count={proposals.length}
      shown={Math.min(proposals.length, LIST_CARD_MAX)}
      viewAllTo="/traces"
      pendingLabel={t`Reading the conversation…`}
      emptyLabel={t`No turn in this conversation is worth a test case.`}
      testId="tracey-proposed-cases"
    >
      {proposals.slice(0, LIST_CARD_MAX).map(proposal => (
        <ListCardRow
          key={`${proposal.agentCallId}-${proposal.kind}`}
          to={`/traces?focus=${proposal.agentCallId}`}
          title={proposal.title}
          right={
            <Badge
              label={proposal.kind === TestCaseProposalKind.Correction ? t`RED` : t`GREEN`}
              variant={proposal.kind === TestCaseProposalKind.Correction ? 'danger' : 'success'}
              size="sm"
            />
          }
        />
      ))}
    </ListCard>
  );
};
