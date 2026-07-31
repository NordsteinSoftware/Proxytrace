// @vitest-environment jsdom
/**
 * Spec for {@link SynthesizeTestsModal}. The load-bearing assertion is the first one: opening the
 * panel must NOT call the synthesis endpoint. Generation spends tokens on the project's system
 * endpoint, so an auto-run on open would bill the user for opening a modal.
 *
 * `Modal` portals to `document.body`, so assertions query there rather than the render container.
 */
import { describe, it, beforeEach, afterEach, beforeAll, expect, vi } from 'vitest';
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { I18nProvider } from '@lingui/react';
import { i18n } from '../../i18n';

const { agentCallsApi, testSuitesApi } = vi.hoisted(() => ({
  agentCallsApi: { listFull: vi.fn(), proposeTestCases: vi.fn() },
  testSuitesApi: { addTestCase: vi.fn() },
}));
vi.mock('../../api/agent-calls', () => ({ agentCallsApi }));
vi.mock('../../api/test-suites', () => ({ testSuitesApi }));

import { SynthesizeTestsModal } from './SynthesizeTestsModal';
import {
  TestCaseProposalFlag,
  TestCaseProposalKind,
  TestCaseProposalRelevance,
  type AgentCallDto,
  type TestCaseProposalSetDto,
  type TestSuiteListItemDto,
} from '../../api/models';

(globalThis as Record<string, unknown>).IS_REACT_ACT_ENVIRONMENT = true;

const TRACE = {
  id: 'call-1',
  conversationId: null,
  request: [],
  response: { role: 'assistant', content: 'done', toolRequests: [] },
  tools: [],
} as unknown as AgentCallDto;

const SUITES = [
  { id: 'suite-1', name: 'Refund suite', testCaseCount: 2, evaluators: [] },
] as unknown as TestSuiteListItemDto[];

function proposalSet(): TestCaseProposalSetDto {
  return {
    summary: 'a refund conversation',
    proposals: [
      {
        agentCallId: 'call-1',
        kind: TestCaseProposalKind.Promotion,
        title: 'Checks the order',
        rationale: 'because',
        relevance: TestCaseProposalRelevance.High,
        expectedOutput: null,
        flags: [],
      },
      {
        agentCallId: 'call-1',
        kind: TestCaseProposalKind.Correction,
        title: 'Should refuse',
        rationale: 'the window has passed',
        relevance: TestCaseProposalRelevance.High,
        expectedOutput: { content: 'I cannot refund that.', toolRequests: [] },
        flags: [TestCaseProposalFlag.Unpassable],
      },
    ],
    skipped: [{ agentCallId: 'call-1', reason: 'closing summary' }],
    evaluatorSuggestion: null,
  };
}

describe('SynthesizeTestsModal', () => {
  let container: HTMLDivElement;
  let root: Root;
  let queryClient: QueryClient;

  beforeAll(() => { i18n.loadAndActivate({ locale: 'en', messages: {} }); });

  beforeEach(() => {
    vi.clearAllMocks();
    queryClient = new QueryClient({
      defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
    });
    container = document.createElement('div');
    document.body.appendChild(container);
    root = createRoot(container);
  });

  afterEach(() => {
    act(() => root.unmount());
    container.remove();
    queryClient.clear();
  });

  function render() {
    act(() => {
      root.render(
        <I18nProvider i18n={i18n}>
          <QueryClientProvider client={queryClient}>
            <SynthesizeTestsModal trace={TRACE} suites={SUITES} onClose={() => {}} />
          </QueryClientProvider>
        </I18nProvider>,
      );
    });
  }

  function click(testId: string) {
    const element = document.body.querySelector(`[data-testid="${testId}"]`);
    if (!(element instanceof HTMLElement)) throw new Error(`no element ${testId}`);
    act(() => { element.click(); });
  }

  it('does not generate on open — opening a modal must not spend tokens', () => {
    render();

    expect(agentCallsApi.proposeTestCases).not.toHaveBeenCalled();
    expect(document.body.querySelector('[data-testid="synthesize-generate-btn"]')).not.toBeNull();
  });

  it('pre-selects an unflagged proposal and leaves a flagged one unchecked', async () => {
    agentCallsApi.proposeTestCases.mockResolvedValue(proposalSet());
    render();

    await act(async () => { click('synthesize-generate-btn'); });

    const promotion = document.body.querySelector<HTMLInputElement>(
      '[data-testid="synthesis-proposal-toggle-call-1-Promotion"]');
    const unpassable = document.body.querySelector<HTMLInputElement>(
      '[data-testid="synthesis-proposal-toggle-call-1-Correction"]');
    expect(promotion?.checked).toBe(true);
    expect(unpassable?.checked).toBe(false);
  });

  it('counts only the checked proposals in the submit label', async () => {
    agentCallsApi.proposeTestCases.mockResolvedValue(proposalSet());
    render();

    await act(async () => { click('synthesize-generate-btn'); });

    const submit = document.body.querySelector('[data-testid="synthesize-submit-btn"]');
    expect(submit?.textContent).toContain('1');
  });

  it('writes one test case per checked proposal on submit', async () => {
    agentCallsApi.proposeTestCases.mockResolvedValue(proposalSet());
    testSuitesApi.addTestCase.mockResolvedValue({ id: 'suite-1', name: 'Refund suite' });
    render();

    await act(async () => { click('synthesize-generate-btn'); });
    await act(async () => { click('synthesize-submit-btn'); });

    expect(testSuitesApi.addTestCase).toHaveBeenCalledTimes(1);
    // A promotion sends no expected output, so the server locks in the recorded response.
    expect(testSuitesApi.addTestCase).toHaveBeenCalledWith('suite-1', 'call-1', undefined);
  });

  it('explains itself when the agent finds nothing worth testing', async () => {
    agentCallsApi.proposeTestCases.mockResolvedValue({
      summary: 'one trivial exchange', proposals: [], skipped: [], evaluatorSuggestion: null,
    });
    render();

    await act(async () => { click('synthesize-generate-btn'); });

    expect(document.body.textContent).toContain('one trivial exchange');
    expect(document.body.querySelector('[data-testid="synthesis-proposal-list"]')).toBeNull();
  });
});
