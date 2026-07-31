// @vitest-environment jsdom
/**
 * Spec for {@link SynthesizeTestsModal}. Two load-bearing assertions bracket the file: generation
 * fires exactly once on open (the header button *is* the generate action, and a StrictMode double
 * mount must not bill a second round), and the success toast carries a link to the suite the cases
 * landed in.
 *
 * `Modal` portals to `document.body`, so assertions query there rather than the render container.
 */
import { describe, it, beforeEach, afterEach, beforeAll, expect, vi } from 'vitest';
import { act, StrictMode } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { I18nProvider } from '@lingui/react';
import { i18n } from '../../i18n';
import type { ToastOptions } from '../../contexts/ToastContext';

const { agentCallsApi, testSuitesApi, evaluatorsApi, navigate, toastShow } = vi.hoisted(() => ({
  agentCallsApi: { listFull: vi.fn(), proposeTestCases: vi.fn() },
  testSuitesApi: { addTestCase: vi.fn(), updateEvaluators: vi.fn(), createWithCases: vi.fn() },
  evaluatorsApi: { create: vi.fn() },
  navigate: vi.fn(),
  toastShow: vi.fn(),
}));
vi.mock('../../api/agent-calls', () => ({ agentCallsApi }));
vi.mock('../../api/test-suites', () => ({ testSuitesApi }));
vi.mock('../../api/evaluators', () => ({ evaluatorsApi }));
// Only `useNavigate` is stubbed — the rest of react-router stays real so any child that reaches for
// it still behaves.
vi.mock('react-router', async importOriginal => ({
  ...(await importOriginal<typeof import('react-router')>()),
  useNavigate: () => navigate,
}));
vi.mock('../../hooks/useToast', () => ({ default: () => ({ show: toastShow }) }));
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

  /**
   * Mounting IS the generate action now, so this awaits the round it kicks off — including the
   * tick the panel defers its start by (see the mount effect: the deferral is what keeps
   * StrictMode's double mount from orphaning the request).
   */
  async function render() {
    await act(async () => {
      root.render(
        <I18nProvider i18n={i18n}>
          <QueryClientProvider client={queryClient}>
            <SynthesizeTestsModal trace={TRACE} suites={SUITES} onClose={() => {}} />
          </QueryClientProvider>
        </I18nProvider>,
      );
    });
    await flush();
  }

  /**
   * One turn lets the panel's deferred start actually fire; the second lets a response that was
   * already settled when it fired (every `mockResolvedValue`/`mockRejectedValue` here) propagate
   * into state. Turns, not durations — nothing is being waited *for*, only yielded to.
   */
  async function flush(turns = 2) {
    for (let i = 0; i < turns; i += 1) {
      await act(async () => { await new Promise(resolve => setTimeout(resolve, 0)); });
    }
  }

  function click(testId: string) {
    const element = document.body.querySelector(`[data-testid="${testId}"]`);
    if (!(element instanceof HTMLElement)) throw new Error(`no element ${testId}`);
    act(() => { element.click(); });
  }

  /** The options bag of the most recent toast, so a test can drive its action. */
  function lastToastOptions(): ToastOptions | undefined {
    return toastShow.mock.lastCall?.[2];
  }

  it('generates once on open — the header button is the generate action', async () => {
    agentCallsApi.proposeTestCases.mockResolvedValue(proposalSet());
    await render();

    expect(agentCallsApi.proposeTestCases).toHaveBeenCalledTimes(1);
    // The idle empty state is gone; the proposals it would have prompted for are already here.
    expect(document.body.querySelector('[data-testid="synthesize-generate-btn"]')).toBeNull();
    expect(document.body.querySelector('[data-testid="synthesis-proposal-list"]')).not.toBeNull();
  });

  it('says it is working while the round runs, rather than showing bare skeletons', async () => {
    // A round is one blocking model call measured in seconds. Skeleton rows alone read as a stuck
    // panel, so the wait names what it is doing and runs a clock.
    let settle: (value: TestCaseProposalSetDto) => void = () => {};
    agentCallsApi.proposeTestCases.mockReturnValue(new Promise(resolve => { settle = resolve; }));
    await render();

    expect(document.body.querySelector('[data-testid="synthesize-generating"]')).not.toBeNull();
    expect(document.body.textContent).toContain('Reading the conversation');

    await act(async () => { settle(proposalSet()); });
    expect(document.body.querySelector('[data-testid="synthesize-generating"]')).toBeNull();
  });

  it('offers a retry rather than a dead end when the generation fails', async () => {
    agentCallsApi.proposeTestCases.mockRejectedValue(new Error('upstream refused'));
    await render();

    expect(document.body.textContent).toContain('upstream refused');
    // The idle empty state doubles as the retry affordance.
    expect(document.body.querySelector('[data-testid="synthesize-generate-btn"]')).not.toBeNull();
  });

  it('pre-selects an unflagged proposal and leaves a flagged one unchecked', async () => {
    agentCallsApi.proposeTestCases.mockResolvedValue(proposalSet());
    await render();

    const promotion = document.body.querySelector<HTMLInputElement>(
      '[data-testid="synthesis-proposal-toggle-call-1-Promotion"]');
    const unpassable = document.body.querySelector<HTMLInputElement>(
      '[data-testid="synthesis-proposal-toggle-call-1-Correction"]');
    expect(promotion?.checked).toBe(true);
    expect(unpassable?.checked).toBe(false);
  });

  it('counts only the checked proposals in the submit label', async () => {
    agentCallsApi.proposeTestCases.mockResolvedValue(proposalSet());
    await render();

    const submit = document.body.querySelector('[data-testid="synthesize-submit-btn"]');
    expect(submit?.textContent).toContain('1');
  });

  it('writes one test case per checked proposal on submit', async () => {
    agentCallsApi.proposeTestCases.mockResolvedValue(proposalSet());
    testSuitesApi.addTestCase.mockResolvedValue({ id: 'suite-1', name: 'Refund suite' });
    await render();

    await act(async () => { click('synthesize-submit-btn'); });

    expect(testSuitesApi.addTestCase).toHaveBeenCalledTimes(1);
    // A promotion sends no expected output, so the server locks in the recorded response.
    expect(testSuitesApi.addTestCase).toHaveBeenCalledWith('suite-1', 'call-1', undefined);
  });

  it('links the success snackbar to the suite the cases landed in', async () => {
    agentCallsApi.proposeTestCases.mockResolvedValue(proposalSet());
    testSuitesApi.addTestCase.mockResolvedValue({ id: 'suite-1', name: 'Refund suite' });
    await render();

    await act(async () => { click('synthesize-submit-btn'); });

    expect(toastShow).toHaveBeenCalledWith(expect.stringContaining('1'), 'success', expect.anything());
    act(() => { lastToastOptions()?.action?.onClick(); });
    expect(navigate).toHaveBeenCalledWith('/suites?id=suite-1');
  });

  it('still offers the suite when only some writes landed', async () => {
    agentCallsApi.proposeTestCases.mockResolvedValue({
      ...proposalSet(),
      // Two unflagged proposals, so both are checked and the first can succeed before the second
      // fails — the state the partial-failure message exists for.
      proposals: proposalSet().proposals.map(p => ({ ...p, flags: [] })),
    });
    testSuitesApi.addTestCase
      .mockResolvedValueOnce({ id: 'suite-1', name: 'Refund suite' })
      .mockRejectedValueOnce(new Error('write two failed'));
    await render();

    await act(async () => { click('synthesize-submit-btn'); });

    expect(toastShow).toHaveBeenCalledWith(expect.stringContaining('1 of 2'), 'error', expect.anything());
    act(() => { lastToastOptions()?.action?.onClick(); });
    expect(navigate).toHaveBeenCalledWith('/suites?id=suite-1');
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
    await render();

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
    await render();

    // "Skip the judge" is the third option in the list — there is no longer a separate decline
    // link, because two controls for one decision is what made the card hard to read.
    await act(async () => { click('synthesis-judge-none'); });
    await act(async () => { click('synthesize-submit-btn'); });

    expect(evaluatorsApi.create).not.toHaveBeenCalled();
    expect(testSuitesApi.updateEvaluators).not.toHaveBeenCalled();
    expect(testSuitesApi.addTestCase).toHaveBeenCalledTimes(1);
  });

  it('states every outcome up front, including the one that redirects the cases', async () => {
    // The old card revealed the blast radius only after "Add to this suite" was selected, and never
    // said that "new suite" sends the cases somewhere other than the destination picked above.
    agentCallsApi.proposeTestCases.mockResolvedValue({
      ...proposalSet(),
      evaluatorSuggestion: {
        name: 'Refund policy judge',
        instructions: 'Does the refusal cite the 30-day window?',
        reason: 'Exact Match cannot judge prose.',
        target: EvaluatorSuggestionTarget.Attach,
      },
    });
    await render();

    const card = document.body.querySelector('[data-testid="synthesis-evaluator-suggestion"]');
    // All three consequences are readable without touching the control.
    expect(card?.textContent).toContain('also score the 2 cases already there');
    expect(card?.textContent).toContain('instead of Refund suite');
    expect(card?.textContent).toContain("Refund suite's current evaluators score the cases");
    // The destination is named rather than called "this suite".
    expect(card?.textContent).toContain('Add the judge to Refund suite');
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
    await render();

    await act(async () => { click('suite-option-suite-2'); });

    expect(capturedSignal?.aborted, 'the panel cancelled its own request').toBe(false);

    await act(async () => { settle?.(proposalSet()); });
    expect(document.body.querySelector('[data-testid="synthesis-proposal-list"]')).not.toBeNull();
  });

  it('shows the round it received even though StrictMode remounted the panel', async () => {
    // Regression: the first round was started inline from the mount effect, so StrictMode's double
    // mount tore the effects down mid-request — `useMutation`'s observer unsubscribed, and
    // `MutationObserver.onUnsubscribe` removed it from the mutation that was still in flight. There
    // is no matching re-attach on re-subscribe, and the ref guard that suppressed the second pass
    // suppressed exactly the call whose observer would have survived. The request then finished
    // normally (the mutation's own `onSuccess` still landed the proposals, so the instruction bar
    // appeared) while `isPending` stayed true forever — the panel sat on its loading state over a
    // response it already had, which read as "generation takes minutes".
    //
    // Only reproducible under StrictMode, hence the explicit wrapper: this is a development-mode
    // failure, which is why the production-build e2e run never saw it.
    let settle: ((value: TestCaseProposalSetDto) => void) | undefined;
    agentCallsApi.proposeTestCases.mockImplementation(
      () => new Promise<TestCaseProposalSetDto>(resolve => { settle = resolve; }));

    await act(async () => {
      root.render(
        <StrictMode>
          <I18nProvider i18n={i18n}>
            <QueryClientProvider client={queryClient}>
              <SynthesizeTestsModal trace={TRACE} suites={SUITES} onClose={() => {}} />
            </QueryClientProvider>
          </I18nProvider>
        </StrictMode>,
      );
    });
    await act(async () => { await new Promise(resolve => setTimeout(resolve, 0)); });
    await act(async () => { settle?.(proposalSet()); });

    expect(document.body.querySelector('[data-testid="synthesize-generating"]'),
      'still waiting on a response it already has').toBeNull();
    expect(document.body.querySelector('[data-testid="synthesis-proposal-list"]')).not.toBeNull();
    // The double mount must still cost exactly one round against the system endpoint.
    expect(agentCallsApi.proposeTestCases).toHaveBeenCalledTimes(1);
  });

  it('explains itself when the agent finds nothing worth testing', async () => {
    agentCallsApi.proposeTestCases.mockResolvedValue({
      summary: 'one trivial exchange', proposals: [], skipped: [], evaluatorSuggestion: null,
    });
    await render();

    expect(document.body.textContent).toContain('one trivial exchange');
    expect(document.body.querySelector('[data-testid="synthesis-proposal-list"]')).toBeNull();
  });
});
