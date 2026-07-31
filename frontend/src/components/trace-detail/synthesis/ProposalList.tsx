import type { TestCaseProposalDto } from '../../../api/models';
import { ProposalRow } from './ProposalRow';
import { proposalKey, type ProposalSelection } from './useProposalSelection';

interface Props {
  proposals: TestCaseProposalDto[];
  selection: ProposalSelection;
  onFocusCall: (agentCallId: string) => void;
}

/** The ranked candidate list. Presentational — selection state lives in the panel's hook. */
export function ProposalList({ proposals, selection, onFocusCall }: Props) {
  return (
    <div className="flex flex-col gap-2" data-testid="synthesis-proposal-list">
      {proposals.map(proposal => {
        const key = proposalKey(proposal);
        return (
          <ProposalRow
            key={key}
            proposal={proposal}
            call={selection.callFor(proposal)}
            checked={selection.checked.has(key)}
            expected={selection.expectedFor(proposal)}
            onToggle={() => selection.toggle(key)}
            onExpectedChange={value => selection.setExpected(key, value)}
            onFocus={() => onFocusCall(proposal.agentCallId)}
          />
        );
      })}
    </div>
  );
}
