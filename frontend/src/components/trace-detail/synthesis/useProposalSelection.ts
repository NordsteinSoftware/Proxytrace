import { useState } from 'react';
import type { AgentCallDto, TestCaseProposalDto, TestCaseProposalSetDto } from '../../../api/models';
import type { ExpectedOutput } from '../../expected-output/expectedOutput';
import { expectedFromProposal, isPreselected, toWrite } from './synthesisSelection';

/** Stable identity for a proposal: one call can carry both a promotion and a correction. */
export const proposalKey = (proposal: TestCaseProposalDto): string =>
  `${proposal.agentCallId}:${proposal.kind}`;

/**
 * Which proposals are approved, and what each correction's expected output currently says.
 *
 * `seed` is called from the generate mutation's success handler rather than an effect: deriving
 * this from a server response is exactly the case BEST_PRACTICES §4.1 says belongs in the event
 * handler, not in `useEffect`.
 */
export function useProposalSelection(callById: Map<string, AgentCallDto>) {
  const [checked, setChecked] = useState<Set<string>>(new Set());
  const [edited, setEdited] = useState<Map<string, ExpectedOutput>>(new Map());

  function seed(set: TestCaseProposalSetDto) {
    setChecked(new Set(set.proposals.filter(isPreselected).map(proposalKey)));
    setEdited(new Map());
  }

  function toggle(key: string) {
    setChecked(previous => {
      const next = new Set(previous);
      if (next.has(key)) next.delete(key);
      else next.add(key);
      return next;
    });
  }

  function setExpected(key: string, value: ExpectedOutput) {
    setEdited(previous => new Map(previous).set(key, value));
  }

  /** The editor state for a proposal: the user's edit if any, otherwise its seeded default. */
  function expectedFor(proposal: TestCaseProposalDto): ExpectedOutput {
    return edited.get(proposalKey(proposal))
      ?? expectedFromProposal(proposal, callById.get(proposal.agentCallId));
  }

  /** The approved proposals as write payloads, in the order the agent ranked them. */
  function writes(proposals: TestCaseProposalDto[]) {
    return proposals
      .filter(proposal => checked.has(proposalKey(proposal)))
      .map(proposal => toWrite(proposal, expectedFor(proposal)));
  }

  /**
   * The proposal set with every edit folded back in — what a refinement round posts, so the agent
   * revises what the user is looking at rather than what it originally said.
   */
  function withEdits(set: TestCaseProposalSetDto): TestCaseProposalSetDto {
    return {
      ...set,
      proposals: set.proposals.map(proposal => {
        const edit = edited.get(proposalKey(proposal));
        if (!edit) return proposal;
        return {
          ...proposal,
          expectedOutput: {
            content: edit.toolRequests === null ? edit.content : '',
            toolRequests: edit.toolRequests ?? [],
          },
        };
      }),
    };
  }

  return { checked, seed, toggle, setExpected, expectedFor, writes, withEdits };
}
