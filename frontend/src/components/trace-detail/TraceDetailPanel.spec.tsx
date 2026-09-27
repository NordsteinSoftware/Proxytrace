// @vitest-environment jsdom
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { MemoryRouter } from 'react-router';
import { setupI18n } from '@lingui/core';
import { I18nProvider } from '@lingui/react';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import type { AgentCallDto } from '../../api/models';
import { TraceDetailPanel } from './TraceDetailPanel';

vi.mock('./useAgentSuites', () => ({ useAgentSuites: () => ({ data: { items: [] } }) }));
vi.mock('../../hooks/useLicense', () => ({ useFeature: () => true }));
vi.mock('../../features/tracey/tracey-chat-context', () => ({
  useTraceyChatContext: () => ({ available: false, askTracey: vi.fn() }),
}));
vi.mock('./TraceAnomalyBanner', () => ({ TraceAnomalyBanner: () => null }));
vi.mock('./SynthesizeTestsModal', () => ({ SynthesizeTestsModal: () => null }));
vi.mock('./TraceMessagesTab', () => ({ TraceMessagesTab: () => null }));

const i18n = setupI18n({ locale: 'en', messages: { en: {} } });
const trace: AgentCallDto = {
  id: 'trace-a', agentId: null, agentName: null, model: 'model', provider: 'provider',
  request: [], response: null, tools: [], inputTokens: 0, outputTokens: 0,
  cachedInputTokens: 0, durationMs: 100, httpStatus: 200, costEur: 0,
  createdAt: '2026-09-27T10:00:00Z', updatedAt: '2026-09-27T10:00:00Z',
  finishReason: null, errorMessage: null, conversationId: null, sessionId: null, outlierFlags: 0,
  modelParameters: {
    temperature: null, topP: null, reasoningEffort: null, frequencyPenalty: null,
    presencePenalty: null, maxTokens: null, seed: null, stop: null, n: null,
  },
};
let root: Root;
let container: HTMLDivElement;

beforeEach(() => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
  const storage = new Map<string, string>();
  vi.stubGlobal('localStorage', {
    getItem: (key: string) => storage.get(key) ?? null,
    setItem: (key: string, value: string) => storage.set(key, value),
  });
  container = document.createElement('div');
  document.body.appendChild(container);
  root = createRoot(container);
});

afterEach(() => {
  act(() => root.unmount());
  container.remove();
  vi.unstubAllGlobals();
});

const render = (overrides: Partial<AgentCallDto> = {}) => act(() => root.render(
  <I18nProvider i18n={i18n}><MemoryRouter>
    <TraceDetailPanel trace={{ ...trace, ...overrides }} onClose={() => {}} />
  </MemoryRouter></I18nProvider>,
));

it.each([
  [200, 'OK'], [204, 'OK'], [301, 'Redirect'], [400, 'Client error'],
  [401, 'Authentication failed'], [403, 'Access denied'], [404, 'Client error'],
  [429, 'Rate limited'], [500, 'Server error'], [503, 'Server error'],
  [100, 'Informational'], [0, 'Unknown status'],
])('labels HTTP %i accurately', (httpStatus, label) => {
  render({ httpStatus });
  expect(document.querySelector('[data-testid="trace-detail-status"]')?.textContent).toBe(`${httpStatus} ${label}`);
});

it.each(['tools', 'raw-json', 'metadata'])('keeps the %s tab across traces and loading unmounts', tab => {
  render();
  const button = document.querySelector<HTMLButtonElement>(`[data-testid="trace-tab-${tab}"]`);
  if (!button) throw new Error('Missing tab');
  act(() => button.dispatchEvent(new MouseEvent('mousedown', { bubbles: true, button: 0 })));
  expect(document.querySelector(`[data-testid="trace-${tab}-tab"]`)).not.toBeNull();
  render({ id: 'trace-b' });
  expect(document.querySelector(`[data-testid="trace-${tab}-tab"]`)).not.toBeNull();
  act(() => root.render(null));
  render({ id: 'trace-c' });
  expect(document.querySelector(`[data-testid="trace-${tab}-tab"]`)).not.toBeNull();
});

it.each(['null', '"invalid"', '["Tools"]', '"__proto__"'])('ignores invalid saved tabs: %s', raw => {
  localStorage.setItem('traces.detailTab', raw);
  render();
  expect(document.querySelector('[data-testid="trace-messages-tab"]')).not.toBeNull();
});
