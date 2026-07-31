import { describe, expect, it } from 'vitest';
import { isPreselected, expectedFromProposal, toWrite } from './synthesisSelection';
import {
  TestCaseProposalFlag,
  TestCaseProposalKind,
  TestCaseProposalRelevance,
  type AgentCallDto,
  type TestCaseProposalDto,
} from '../../../api/models';

function proposal(overrides: Partial<TestCaseProposalDto> = {}): TestCaseProposalDto {
  return {
    agentCallId: 'call-1',
    kind: TestCaseProposalKind.Promotion,
    title: 'Checks the order',
    rationale: 'because',
    relevance: TestCaseProposalRelevance.High,
    expectedOutput: null,
    flags: [],
    ...overrides,
  };
}

describe('isPreselected', () => {
  it('checks a high-relevance proposal with no flags', () => {
    expect(isPreselected(proposal())).toBe(true);
  });

  it('checks a medium-relevance proposal', () => {
    expect(isPreselected(proposal({ relevance: TestCaseProposalRelevance.Medium }))).toBe(true);
  });

  it('leaves a low-relevance proposal unchecked', () => {
    expect(isPreselected(proposal({ relevance: TestCaseProposalRelevance.Low }))).toBe(false);
  });

  it('leaves an unpassable correction unchecked however relevant it claims to be', () => {
    expect(isPreselected(proposal({ flags: [TestCaseProposalFlag.Unpassable] }))).toBe(false);
  });

  it('leaves an unknown-tool proposal unchecked', () => {
    expect(isPreselected(proposal({ flags: [TestCaseProposalFlag.UnknownTool] }))).toBe(false);
  });
});

describe('expectedFromProposal', () => {
  it('uses the proposal tool requests when it is a correction', () => {
    const value = expectedFromProposal(
      proposal({
        kind: TestCaseProposalKind.Correction,
        expectedOutput: { content: '', toolRequests: [{ name: 'issue_refund', arguments: '{"id":"91"}' }] },
      }),
      undefined,
    );

    expect(value.toolRequests).toEqual([{ name: 'issue_refund', arguments: '{"id":"91"}' }]);
    expect(value.content).toBe('');
  });

  it('uses the proposal text when the correction is prose', () => {
    const value = expectedFromProposal(
      proposal({
        kind: TestCaseProposalKind.Correction,
        expectedOutput: { content: 'I cannot refund that.', toolRequests: [] },
      }),
      undefined,
    );

    expect(value).toEqual({ content: 'I cannot refund that.', toolRequests: null });
  });

  it('falls back to the recorded response for a promotion', () => {
    // Only `response` matters here — the rest of the DTO is irrelevant to seeding the editor.
    const call = { response: { role: 'assistant', content: 'done', toolRequests: [] } } as unknown as AgentCallDto;

    expect(expectedFromProposal(proposal(), call)).toEqual({ content: 'done', toolRequests: null });
  });

  it('ignores a stray expected output on a promotion', () => {
    const call = { response: { role: 'assistant', content: 'done', toolRequests: [] } } as unknown as AgentCallDto;
    const stray = proposal({ expectedOutput: { content: 'nonsense', toolRequests: [] } });

    expect(expectedFromProposal(stray, call)).toEqual({ content: 'done', toolRequests: null });
  });
});

describe('toWrite', () => {
  it('omits the expected output for a promotion so the recorded response is locked in', () => {
    const write = toWrite(proposal(), { content: 'done', toolRequests: null });

    expect(write).toEqual({ fromAgentCallId: 'call-1' });
  });

  it('sends the edited expected output for a correction', () => {
    const write = toWrite(
      proposal({ kind: TestCaseProposalKind.Correction }),
      { content: 'I cannot refund that.', toolRequests: null },
    );

    expect(write.fromAgentCallId).toBe('call-1');
    expect(write.expectedOutput).toEqual({
      role: 'assistant',
      content: 'I cannot refund that.',
      toolRequests: null,
    });
  });
});
