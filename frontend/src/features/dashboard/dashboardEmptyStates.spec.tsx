// @vitest-environment jsdom
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { MemoryRouter } from 'react-router';
import { setupI18n } from '@lingui/core';
import { I18nProvider } from '@lingui/react';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import type { SummaryDto } from '../../api/models';
import { LiveTraceStream } from './components/LiveTraceStream';
import { PassRateGauge } from './components/PassRateGauge';
import { StatTileGrid } from './components/StatTileGrid';

const i18n = setupI18n({ locale: 'en', messages: { en: {} } });
const emptySummary: SummaryDto = {
  totalCalls: 0, totalInputTokens: 0, totalOutputTokens: 0,
  totalCachedInputTokens: 0, avgLatencyMs: 0, overallPassRate: null,
};
let container: HTMLDivElement;
let root: Root;

function render(children: React.ReactNode) {
  act(() => root.render(<I18nProvider i18n={i18n}><MemoryRouter>{children}</MemoryRouter></I18nProvider>));
}

beforeEach(() => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
  vi.stubGlobal('matchMedia', () => ({ matches: true, addEventListener: () => {}, removeEventListener: () => {} }));
  container = document.createElement('div');
  document.body.appendChild(container);
  root = createRoot(container);
});

afterEach(() => {
  act(() => root.unmount());
  container.remove();
  vi.unstubAllGlobals();
});

it('shows missing pass-rate data without implying a failed run', () => {
  render(<><StatTileGrid summary={emptySummary} telemetry={undefined} trends={undefined} latencyStats={null} /><PassRateGauge summary={emptySummary} passRateTrend={[]} /></>);
  const tile = container.querySelector('[data-testid="stat-tile-pass-rate"]')!;
  expect(tile.querySelector('[data-testid="stat-tile-pass-rate-value"]')?.textContent).toBe('—');
  expect(tile.textContent).toContain('No runs in range');
  expect(tile.textContent).not.toContain('%');
  expect(container.querySelector('[data-testid="pass-rate-gauge"]')?.textContent).toContain('No runs in range');
});

it('widens an empty live feed to all time', () => {
  const onRangeChange = vi.fn();
  render(<LiveTraceStream traces={[]} isLoading={false} freshIds={new Set()} range="24h" onRangeChange={onRangeChange} />);
  expect(container.textContent).toContain('No traces in this range');
  const button = Array.from(container.querySelectorAll('button')).find(b => b.textContent?.includes('Show all time'))!;
  act(() => button.click());
  expect(onRangeChange).toHaveBeenCalledWith('all');
});
