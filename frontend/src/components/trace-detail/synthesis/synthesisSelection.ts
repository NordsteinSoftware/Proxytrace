import {
  TestCaseProposalKind,
  TestCaseProposalRelevance,
  type AgentCallDto,
  type TestCaseProposalDto,
  type TestSuiteMessageDto,
} from '../../../api/models';
import {
  expectedFromResponse,
  toMessage,
  type ExpectedOutput,
} from '../../expected-output/expectedOutput';

/**
 * Whether a proposal arrives checked. High or Medium relevance AND no flags: a Low-relevance
 * candidate is offered but not urged, and a flagged one (an unpassable correction, an unknown tool)
 * is never pre-selected — the whole point of the flag is that it needs a human look first.
 */
export function isPreselected(proposal: TestCaseProposalDto): boolean {
  if (proposal.flags.length > 0) return false;
  return proposal.relevance === TestCaseProposalRelevance.High
    || proposal.relevance === TestCaseProposalRelevance.Medium;
}

/**
 * The editor state a proposal starts in. A Correction seeds the editor with the agent's proposed
 * answer; a Promotion seeds it with the response the source call actually recorded, which is what
 * the case will assert.
 */
export function expectedFromProposal(
  proposal: TestCaseProposalDto,
  call: AgentCallDto | undefined,
): ExpectedOutput {
  if (proposal.kind === TestCaseProposalKind.Correction && proposal.expectedOutput) {
    const { content, toolRequests } = proposal.expectedOutput;
    return toolRequests.length > 0
      ? { content: '', toolRequests: toolRequests.map(request => ({ ...request })) }
      : { content, toolRequests: null };
  }
  return expectedFromResponse(call?.response ?? null);
}

/**
 * The write payload for one approved proposal. A Promotion deliberately omits `expectedOutput` so
 * the server locks in the response the agent actually recorded — sending it back would be the same
 * assertion with an extra chance to diverge.
 */
export function toWrite(
  proposal: TestCaseProposalDto,
  expected: ExpectedOutput,
): { fromAgentCallId: string; expectedOutput?: TestSuiteMessageDto } {
  return proposal.kind === TestCaseProposalKind.Correction
    ? { fromAgentCallId: proposal.agentCallId, expectedOutput: toMessage(expected) }
    : { fromAgentCallId: proposal.agentCallId };
}
