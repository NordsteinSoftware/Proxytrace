// @vitest-environment jsdom
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { setupI18n } from '@lingui/core';
import { I18nProvider } from '@lingui/react';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { TooltipProvider } from '../../../components/ui/Tooltip';
import { useTraceColumnWidths } from '../hooks/useTraceColumnWidths';
import { COL_MIN_WIDTHS, GRID_TEMPLATE, GRID_TEMPLATE_NARROW } from '../tracesMeta';
import { TraceTableHeader } from './TraceTableHeader';

const i18n = setupI18n({ locale: 'en', messages: { en: {} } });
const onSortChange = vi.fn();
let container: HTMLDivElement;
let root: Root;

function Host() {
  const { style, resizeColumn } = useTraceColumnWidths();
  return (
    <I18nProvider i18n={i18n}>
      <TooltipProvider>
        <div data-testid="layout" style={style}>
          <TraceTableHeader sort={{ field: 'time', desc: true }} onSortChange={onSortChange}
            position={{ first: 0, last: 0, total: 0, pendingRefresh: false }} onColumnResize={resizeColumn} />
        </div>
      </TooltipProvider>
    </I18nProvider>
  );
}

beforeEach(() => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
  const storage = new Map<string, string>();
  vi.stubGlobal('localStorage', {
    getItem: (key: string) => storage.get(key) ?? null,
    setItem: (key: string, value: string) => { storage.set(key, value); },
  });
  onSortChange.mockClear();
  container = document.createElement('div');
  document.body.appendChild(container);
  root = createRoot(container);
});

afterEach(() => {
  act(() => root.unmount());
  container.remove();
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
});

const render = () => act(() => root.render(<Host />));
const layout = () => container.querySelector<HTMLDivElement>('[data-testid="layout"]')!.style;
const handle = (label: string) => container.querySelector<HTMLButtonElement>(`[aria-label="Resize ${label} column"]`)!;
const saved = () => JSON.parse(localStorage.getItem('traces.columnWidths')!);

it('drags columns without sorting, persists the layout across remounts, and supports keyboard resizing', () => {
  render();
  const resize = handle('Tokens');
  const cells = Array.from(resize.parentElement!.parentElement!.children);
  const widths = COL_MIN_WIDTHS.map(width => width + 40);
  cells.forEach((cell, i) => vi.spyOn(cell, 'getBoundingClientRect').mockReturnValue({ width: widths[i] } as DOMRect));
  resize.setPointerCapture = vi.fn();
  resize.hasPointerCapture = () => true;
  resize.releasePointerCapture = vi.fn();
  const pointer = (type: string, clientX: number) => act(() => {
    resize.dispatchEvent(new MouseEvent(type, { bubbles: true, button: 0, clientX }));
  });

  pointer('pointerdown', 200);
  pointer('pointermove', 260);
  pointer('pointerup', 260);
  pointer('pointermove', 300);
  act(() => resize.click());
  expect(saved()).toEqual(widths.map((width, i) => i === 5 ? width + 60 : width));
  expect(onSortChange).not.toHaveBeenCalled();
  expect(layout().getPropertyValue('--trace-grid')).toContain(`${widths[5] + 60}px`);
  const beforeReload = layout().cssText;
  act(() => root.unmount());
  root = createRoot(container);
  render();
  expect(layout().cssText).toBe(beforeReload);

  const message = handle('Message');
  vi.spyOn(message.parentElement!, 'getBoundingClientRect').mockReturnValue({ width: 210 } as DOMRect);
  act(() => message.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight', bubbles: true })));
  expect(saved()[0]).toBe(220);
  expect(layout().getPropertyValue('--trace-grid-narrow')).toMatch(/^220px /);
  act(() => container.querySelector<HTMLButtonElement>('[data-testid="traces-sort-tokens"]')!.click());
  expect(onSortChange).toHaveBeenCalledWith('tokens');
});

it('falls back for malformed storage and keeps resizing usable when storage writes fail', () => {
  localStorage.setItem('traces.columnWidths', '{broken');
  render();
  expect(layout().getPropertyValue('--trace-grid')).toBe(GRID_TEMPLATE);
  expect(layout().getPropertyValue('--trace-grid-narrow')).toBe(GRID_TEMPLATE_NARROW);
  const message = handle('Message');
  vi.spyOn(message.parentElement!, 'getBoundingClientRect').mockReturnValue({ width: 170 } as DOMRect);
  vi.spyOn(localStorage, 'setItem').mockImplementation(() => { throw new Error('unavailable'); });
  act(() => message.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowLeft', bubbles: true })));
  expect(layout().getPropertyValue('--trace-grid')).toMatch(/^170px /);
});

it.each(['null', '{}', '["wide", -20, 99999, null]'])('validates saved widths: %s', raw => {
  localStorage.setItem('traces.columnWidths', raw);
  render();
  expect(layout().getPropertyValue('--trace-grid')).not.toMatch(/wide|-20|99999|NaN/);
});
