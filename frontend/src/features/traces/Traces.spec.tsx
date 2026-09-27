// @vitest-environment jsdom
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { setupI18n } from '@lingui/core';
import { I18nProvider } from '@lingui/react';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { TooltipProvider } from '../../components/ui/Tooltip';
import { EMPTY_ADVANCED_FILTERS } from './tracesMeta';
import Traces from './Traces';

vi.mock('../../hooks/useCurrentProject', () => ({ default: () => ({ currentProjectId: 'project' }) }));
vi.mock('../../hooks/useSelectedTrace', () => ({ useSelectedTrace: () => [null, vi.fn()] }));
vi.mock('./hooks/useFocusTrace', () => ({ useFocusTrace: () => {} }));
vi.mock('./hooks/useAutoDefaultRange', () => ({ useAutoDefaultRange: () => {} }));
vi.mock('./hooks/useTraceSseStream', () => ({ useTraceSseStream: () => ({ freshIds: new Set() }) }));
vi.mock('./hooks/useTraceSummary', () => ({ useTraceSummary: () => ({ summary: null }) }));
vi.mock('./hooks/useTraceHistogram', () => ({ useTraceHistogram: () => ({ buckets: [] }) }));
vi.mock('./hooks/useTraceToolNames', () => ({ useTraceToolNames: () => [] }));
vi.mock('./hooks/useRecentSessions', () => ({ useRecentSessions: () => ({ sessions: [], isLoading: false }) }));
vi.mock('../../components/charts/TraceTimeline', () => ({ TraceTimeline: () => null }));
vi.mock('./components/TraceTable', () => ({ TraceTable: () => null }));
vi.mock('./hooks/useTraceQueries', async importOriginal => ({
  ...(await importOriginal<typeof import('./hooks/useTraceQueries')>()),
  useTraceQueries: () => ({ traces: [], total: 0, allAgents: [], agentBreakdown: [] }),
}));

const i18n = setupI18n({ locale: 'en', messages: { en: {} } });
let root: Root;
let container: HTMLDivElement;
const range = { kind: 'preset', preset: '24h' };

beforeEach(() => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
  const storage = new Map<string, string>();
  vi.stubGlobal('localStorage', {
    getItem: (key: string) => storage.get(key) ?? null,
    setItem: (key: string, value: string) => storage.set(key, value),
    removeItem: (key: string) => storage.delete(key),
  });
  localStorage.setItem('traces.timeRange', JSON.stringify(range));
  localStorage.setItem('traces.search', JSON.stringify('timeout'));
  localStorage.setItem('traces.showSystem', 'true');
  localStorage.setItem('traces.filters.project', JSON.stringify({ ...EMPTY_ADVANCED_FILTERS, model: 'model' }));
  container = document.createElement('div');
  document.body.appendChild(container);
  root = createRoot(container);
  act(() => root.render(<I18nProvider i18n={i18n}><TooltipProvider><Traces /></TooltipProvider></I18nProvider>));
});

afterEach(() => {
  act(() => root.unmount());
  container.remove();
  vi.unstubAllGlobals();
});

function click(testId: string) {
  const button = container.querySelector<HTMLButtonElement>(`[data-testid="${testId}"]`);
  if (!button) throw new Error(`Missing ${testId}`);
  act(() => button.click());
}

it('clears filter chips while preserving search and time range, then explicitly resets the view', () => {
  click('traces-clear-filters');
  expect(JSON.parse(localStorage.getItem('traces.filters.project') ?? 'null')).toEqual(EMPTY_ADVANCED_FILTERS);
  expect(localStorage.getItem('traces.showSystem')).toBe('false');
  expect(container.querySelector('input')?.value).toBe('timeout');
  expect(JSON.parse(localStorage.getItem('traces.timeRange') ?? 'null')).toEqual(range);
  expect(container.querySelector('[data-testid="traces-clear-filters"]')).toBeNull();
  click('traces-reset-view');
  expect(container.querySelector('input')?.value).toBe('');
  expect(JSON.parse(localStorage.getItem('traces.timeRange') ?? 'null')).toEqual({ kind: 'all' });
  // Nothing left to reset → the action withdraws instead of sitting there as a no-op.
  expect(container.querySelector('[data-testid="traces-reset-view"]')).toBeNull();
});
