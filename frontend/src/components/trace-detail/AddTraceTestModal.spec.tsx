// @vitest-environment jsdom
/**
 * Spec for {@link AddTraceTestModal} — the trace → test-case hand-off. It pins the two write
 * shapes (append to the selected suite vs create-suite-and-add), the recorded-response default,
 * and the response-less trace, where keeping the recorded response is impossible so the
 * expected-output editor starts open and required. `Modal` portals to `document.body`.
 */
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { MemoryRouter } from 'react-router';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { setupI18n } from '@lingui/core';
import { I18nProvider } from '@lingui/react';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import type { AgentCallDto, TestSuiteListItemDto } from '../../api/models';
import { AddTraceTestModal } from './AddTraceTestModal';

const { addTestCase, createWithCases, toastShow } = vi.hoisted(() => ({
  addTestCase: vi.fn(),
  createWithCases: vi.fn(),
  toastShow: vi.fn(),
}));
const { navigate } = vi.hoisted(() => ({ navigate: vi.fn() }));
vi.mock('../../api/test-suites', () => ({ testSuitesApi: { addTestCase, createWithCases } }));
vi.mock('../../hooks/useToast', () => ({ default: () => ({ show: toastShow }) }));
vi.mock('react-router', async importOriginal => ({
  ...(await importOriginal<typeof import('react-router')>()),
  useNavigate: () => navigate,
}));

const i18n = setupI18n({ locale: 'en', messages: { en: {} } });

const trace: AgentCallDto = {
  id: 'call-1', agentId: 'agent-1', agentName: 'Support', model: 'model', provider: 'provider',
  request: [], response: { role: 'assistant', content: 'Hello there', toolRequests: [], toolCallId: null },
  tools: [], inputTokens: 0, outputTokens: 0, cachedInputTokens: 0, durationMs: 100,
  httpStatus: 200, finishReason: null, errorMessage: null, costEur: 0,
  createdAt: '2026-09-27T10:00:00Z', updatedAt: '2026-09-27T10:00:00Z',
  conversationId: null, sessionId: null, outlierFlags: 0,
  modelParameters: {
    temperature: null, topP: null, reasoningEffort: null, frequencyPenalty: null,
    presencePenalty: null, maxTokens: null, seed: null, stop: null, n: null,
  },
};

const suite = (id: string, name: string): TestSuiteListItemDto => ({
  id, name, agentId: 'agent-1', agentName: 'Support', evaluators: [], testCaseCount: 2,
  description: null, tags: [], totalRuns: 0, passRate: null, prevPassRate: null,
  passRateTrend: [], lastRunAt: null, lastRunGroupId: null,
  createdAt: '2026-09-01T10:00:00Z', updatedAt: '2026-09-01T10:00:00Z',
});

let root: Root;
let container: HTMLDivElement;
let queryClient: QueryClient;
const onClose = vi.fn();

function render(suites: TestSuiteListItemDto[], over: Partial<AgentCallDto> = {}) {
  act(() => root.render(
    <QueryClientProvider client={queryClient}>
      <I18nProvider i18n={i18n}>
        <MemoryRouter>
          <AddTraceTestModal trace={{ ...trace, ...over }} suites={suites} onClose={onClose} />
        </MemoryRouter>
      </I18nProvider>
    </QueryClientProvider>,
  ));
}

function submit(): HTMLButtonElement {
  return document.body.querySelector<HTMLButtonElement>('[data-testid="modal-submit"]')!;
}

function setValue(el: HTMLInputElement, value: string) {
  const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')!.set!;
  act(() => {
    setter.call(el, value);
    el.dispatchEvent(new Event('input', { bubbles: true }));
  });
}

beforeEach(() => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
  addTestCase.mockReset().mockResolvedValue({ id: 'suite-1', name: 'Refund Policy' });
  createWithCases.mockReset().mockResolvedValue({ id: 'suite-9', name: 'New suite' });
  toastShow.mockReset();
  navigate.mockReset();
  onClose.mockReset();
  queryClient = new QueryClient({ defaultOptions: { mutations: { retry: false } } });
  container = document.createElement('div');
  document.body.appendChild(container);
  root = createRoot(container);
});

afterEach(() => {
  act(() => root.unmount());
  container.remove();
  document.body.querySelectorAll('[data-testid="add-trace-test-modal"]').forEach(el => el.remove());
  vi.unstubAllGlobals();
});

it('appends to the selected suite with the recorded response untouched', async () => {
  render([suite('suite-1', 'Refund Policy')]);

  expect(submit().disabled).toBe(false);
  await act(async () => { submit().click(); });

  expect(addTestCase).toHaveBeenCalledWith('suite-1', 'call-1', undefined);
  expect(toastShow).toHaveBeenCalledWith(expect.stringContaining('Refund Policy'), 'success', expect.anything());
  expect(onClose).toHaveBeenCalled();
});

it('creates a suite with the trace as its first case when none exist', async () => {
  render([]);

  // Nothing to append to and no name yet — the action waits.
  expect(submit().disabled).toBe(true);
  setValue(document.body.querySelector<HTMLInputElement>('[data-testid="trace-new-suite-name"]')!, '  Regression suite ');
  expect(submit().disabled).toBe(false);

  await act(async () => { submit().click(); });
  expect(createWithCases).toHaveBeenCalledWith({
    name: 'Regression suite',
    agentId: 'agent-1',
    testCases: [{ fromAgentCallId: 'call-1', expectedOutput: undefined }],
  });
});

it('forces the expected-output editor when the trace has no response', () => {
  render([suite('suite-1', 'Refund Policy')], { response: null });

  const edit = document.body.querySelector<HTMLInputElement>('[data-testid="trace-edit-expected"]')!;
  expect(edit.checked).toBe(true);
  expect(edit.disabled).toBe(true);
  // Without a recorded response there is nothing valid to save until an expectation is written.
  expect(submit().disabled).toBe(true);
});
