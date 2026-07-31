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

const { agentCallsApi, testSuitesApi, evaluatorsApi } = vi.hoisted(() => ({
  agentCallsApi: { listFull: vi.fn(), proposeTestCases: vi.fn() },
  testSuitesApi: { addTestCase: vi.fn(), updateEvaluators: vi.fn(), createWithCases: vi.fn() },
  evaluatorsApi: { create: vi.fn() },
}));
vi.mock('../../api/agent-calls', () => ({ agentCallsApi }));
vi.mock('../../api/test-suites', () => ({ testSuitesApi }));
vi.mock('../../api/evaluators', () => ({ evaluatorsApi }));
// The panel reads the current project (for evaluator creation) and the licence (for the judge
// card). Both are app-wide context; stub them rather than mounting the whole provider tree.
vi.mock('../../hooks/useCurrentProject', () => ({
  default: () => ({ currentProjectId: 'project-1' }),
}));
vi.mock('../../hooks/useLicense', () => ({
  useFeature: () => true,
  useLicense: () => ({ data: { limits: { MaxTestSuites: 100 } } }),
}));

import { SynthesizeTestsModal } from './SynthesizeTestsModal';
import {
  EvaluatorSuggestionTarget,
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
  {
    id: 'suite-1',
    name: 'Refund suite',
    testCaseCount: 2,
    evaluators: [{ id: 'eval-existing', kind: 'ExactMatch' }],
  },
  // A second suite so a test can switch destination — which re-renders the panel mid-request.
  { id: 'suite-2', name: 'Escalation suite', testCaseCount: 0, evaluators: [] },
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

  it('attaching the suggested judge widens the suite evaluator set rather than replacing it', async () => {
    agentCallsApi.proposeTestCases.mockResolvedValue({
      ...proposalSet(),
      evaluatorSuggestion: {
        name: 'Refund policy judge',
        instructions: 'Does the refusal cite the 30-day window?',
        reason: 'Exact Match cannot judge prose.',
        target: EvaluatorSuggestionTarget.Attach,
      },
    });
    evaluatorsApi.create.mockResolvedValue({ id: 'judge-1' });
    testSuitesApi.addTestCase.mockResolvedValue({ id: 'suite-1', name: 'Refund suite' });
    testSuitesApi.updateEvaluators.mockResolvedValue({ id: 'suite-1' });
    render();

    await act(async () => { click('synthesize-generate-btn'); });
    await act(async () => { click('synthesize-submit-btn'); });

    // The existing evaluator survives: updateEvaluators REPLACES the set, so the current ids must
    // be sent alongside the new judge.
    expect(testSuitesApi.updateEvaluators).toHaveBeenCalledWith('suite-1', ['eval-existing', 'judge-1']);
  });

  it('declining the judge writes the cases and touches no evaluator', async () => {
    agentCallsApi.proposeTestCases.mockResolvedValue({
      ...proposalSet(),
      evaluatorSuggestion: {
        name: 'Refund policy judge',
        instructions: 'Does the refusal cite the 30-day window?',
        reason: 'Exact Match cannot judge prose.',
        target: EvaluatorSuggestionTarget.Attach,
      },
    });
    testSuitesApi.addTestCase.mockResolvedValue({ id: 'suite-1', name: 'Refund suite' });
    render();

    await act(async () => { click('synthesize-generate-btn'); });
    await act(async () => { click('synthesis-judge-decline-btn'); });
    await act(async () => { click('synthesize-submit-btn'); });

    expect(evaluatorsApi.create).not.toHaveBeenCalled();
    expect(testSuitesApi.updateEvaluators).not.toHaveBeenCalled();
    expect(testSuitesApi.addTestCase).toHaveBeenCalledTimes(1);
  });

  it('survives a re-render while a generation is in flight', async () => {
    // Regression (caught by e2e): `abort` was a fresh closure every render, so the panel's
    // `useEffect(() => abort, [abort])` saw a changed dependency on EVERY re-render and ran its
    // cleanup — cancelling the request that was still in flight. Any state change during
    // generation triggered it (in the browser, the conversation query settling); switching the
    // destination suite is the same thing with no timing to arrange. The other tests here resolve
    // the mock synchronously, which closes the window entirely.
    let capturedSignal: AbortSignal | undefined;
    let settle: ((value: TestCaseProposalSetDto) => void) | undefined;
    agentCallsApi.proposeTestCases.mockImplementation(
      (_id: string, _body: unknown, options?: { signal?: AbortSignal }) => {
        capturedSignal = options?.signal;
        return new Promise<TestCaseProposalSetDto>(resolve => { settle = resolve; });
      },
    );
    render();

    await act(async () => { click('synthesize-generate-btn'); });
    await act(async () => { click('promote-suite-option-suite-2'); });

    expect(capturedSignal?.aborted, 'the panel cancelled its own request').toBe(false);

    await act(async () => { settle?.(proposalSet()); });
    expect(document.body.querySelector('[data-testid="synthesis-proposal-list"]')).not.toBeNull();
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
