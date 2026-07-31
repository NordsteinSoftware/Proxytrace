// @vitest-environment jsdom
/**
 * Spec for the toast's action affordance — the bit that turns a "done" message into a snackbar you
 * can act on. The dwell time is the subtle part: a plain toast is a receipt and 3s is plenty, but
 * one carrying a link has to survive being read *and* decided on.
 */
import { describe, it, beforeEach, afterEach, beforeAll, expect, vi } from 'vitest';
import { act, useEffect, useRef } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { I18nProvider } from '@lingui/react';
import { i18n } from '../../i18n';
import { ToastProvider } from './Toast';
import useToast from '../../hooks/useToast';
import type { ToastOptions, ToastItem } from '../../contexts/ToastContext';

(globalThis as Record<string, unknown>).IS_REACT_ACT_ENVIRONMENT = true;

/** Fires one toast on mount, so a test can describe it declaratively. */
function Emit({ message, type, options }: {
  message: string;
  type: ToastItem['type'];
  options?: ToastOptions;
}) {
  const { show } = useToast();
  const fired = useRef(false);
  useEffect(() => {
    if (fired.current) return;
    fired.current = true;
    show(message, type, options);
  }, [show, message, type, options]);
  return null;
}

describe('Toast', () => {
  let container: HTMLDivElement;
  let root: Root;

  beforeAll(() => { i18n.loadAndActivate({ locale: 'en', messages: {} }); });

  beforeEach(() => {
    vi.useFakeTimers();
    container = document.createElement('div');
    document.body.appendChild(container);
    root = createRoot(container);
  });

  afterEach(() => {
    act(() => root.unmount());
    container.remove();
    vi.useRealTimers();
  });

  function render(node: React.ReactNode) {
    act(() => {
      root.render(<I18nProvider i18n={i18n}>{node}</I18nProvider>);
    });
  }

  function actionButton() {
    return container.querySelector<HTMLElement>('[data-testid="toast-action-btn"]');
  }

  it('renders no action link and stays click-through without one', () => {
    render(<ToastProvider><Emit message="Saved" type="success" /></ToastProvider>);

    expect(container.textContent).toContain('Saved');
    expect(actionButton()).toBeNull();
    const toast = container.querySelector<HTMLElement>('[data-testid="toast"]');
    // A plain toast must not intercept clicks on whatever it is covering.
    expect(toast?.className).not.toContain('pointer-events-auto');
  });

  it('runs the action and dismisses itself on click', () => {
    const onClick = vi.fn();
    render(
      <ToastProvider>
        <Emit message="Added 2 test cases" type="success" options={{ action: { label: 'View suite', onClick } }} />
      </ToastProvider>,
    );

    const button = actionButton();
    expect(button?.textContent).toContain('View suite');
    expect(container.querySelector<HTMLElement>('[data-testid="toast"]')?.className)
      .toContain('pointer-events-auto');

    act(() => { button?.click(); });

    expect(onClick).toHaveBeenCalledTimes(1);
    expect(container.textContent).not.toContain('Added 2 test cases');
  });

  it('outlives the plain dwell so the action can be read and taken', () => {
    render(
      <ToastProvider>
        <Emit message="Added 2 test cases" type="success" options={{ action: { label: 'View suite', onClick: vi.fn() } }} />
      </ToastProvider>,
    );

    // Past the 3s a plain toast gets — an actionable one is still there.
    act(() => { vi.advanceTimersByTime(4000); });
    expect(actionButton()).not.toBeNull();

    act(() => { vi.advanceTimersByTime(5000); });
    expect(actionButton()).toBeNull();
  });

  it('auto-dismisses a plain toast on the short dwell', () => {
    render(<ToastProvider><Emit message="Saved" type="success" /></ToastProvider>);

    act(() => { vi.advanceTimersByTime(3500); });
    expect(container.textContent).not.toContain('Saved');
  });
});
