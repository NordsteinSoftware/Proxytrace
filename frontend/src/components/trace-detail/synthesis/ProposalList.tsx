import type { AgentCallDto, TestCaseProposalDto } from '../../../api/models';
import type { ExpectedOutput } from '../../expected-output/expectedOutput';
import { ProposalRow } from './ProposalRow';
import { proposalKey } from './useProposalSelection';

interface Props {
  proposals: TestCaseProposalDto[];
  callById: Map<string, AgentCallDto>;
  checked: Set<string>;
  expectedFor: (proposal: TestCaseProposalDto) => ExpectedOutput;
  onToggle: (key: string) => void;
  onExpectedChange: (key: string, value: ExpectedOutput) => void;
  onFocus: (agentCallId: string) => void;
}

/** The ranked candidate list. Presentational — selection state lives in the panel. */
export function ProposalList({
  proposals, callById, checked, expectedFor, onToggle, onExpectedChange, onFocus,
}: Props) {
  return (
    <div className="flex flex-col gap-2" data-testid="synthesis-proposal-list">
      {proposals.map(proposal => {
        const key = proposalKey(proposal);
        return (
          <ProposalRow
            key={key}
            proposal={proposal}
            call={callById.get(proposal.agentCallId)}
            checked={checked.has(key)}
            expected={expectedFor(proposal)}
            onToggle={() => onToggle(key)}
            onExpectedChange={value => onExpectedChange(key, value)}
            onFocus={() => onFocus(proposal.agentCallId)}
          />
        );
      })}
    </div>
  );
}
