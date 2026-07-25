// @vitest-environment jsdom
/**
 * Unit spec for {@link ErrorBoundary}. Pins the two properties the app chrome depends on: a throw
 * is contained rather than unmounting the tree, and a caught error clears when `resetKeys` changes
 * (navigation) — without that, wrapping the topbar would leave the bell, search and account menu
 * dead for the rest of the session instead of for one page.
 */
import { describe, it, beforeEach, afterEach, expect } from 'vitest';
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { i18n } from '../i18n';
import { I18nProvider } from '@lingui/react';
import { ErrorBoundary } from './ErrorBoundary';

(globalThis as Record<string, unknown>).IS_REACT_ACT_ENVIRONMENT = true;

function Boom({ shouldThrow }: { shouldThrow: boolean }) {
  if (shouldThrow) throw new Error('kaboom');
  return <div data-testid="ok">fine</div>;
}

describe('ErrorBoundary', () => {
  let container: HTMLDivElement;
  let root: Root;
  // React logs caught render errors; silence them so the spec output stays readable.
  const consoleError = console.error;

  beforeEach(() => {
    i18n.loadAndActivate({ locale: 'en', messages: {} });
    console.error = () => {};
    container = document.createElement('div');
    document.body.appendChild(container);
    root = createRoot(container);
  });

  afterEach(() => {
    act(() => root.unmount());
    container.remove();
    console.error = consoleError;
  });

  function render(node: React.ReactNode) {
    act(() => root.render(<I18nProvider i18n={i18n}>{node}</I18nProvider>));
  }

  it('contains a throwing child instead of tearing down the tree', () => {
    render(
      <ErrorBoundary fallback={<div data-testid="fallback" />}>
        <Boom shouldThrow />
      </ErrorBoundary>,
    );

    expect(container.querySelector('[data-testid="fallback"]')).not.toBeNull();
    // The container still has content — the root did not unmount (that is the white-screen).
    expect(container.innerHTML).not.toBe('');
  });

  it('renders the default message when no fallback is given', () => {
    render(
      <ErrorBoundary>
        <Boom shouldThrow />
      </ErrorBoundary>,
    );

    expect(container.textContent).toContain('Something went wrong');
    expect(container.textContent).toContain('kaboom');
  });

  it('clears the error when resetKeys change, so navigation recovers', () => {
    render(
      <ErrorBoundary resetKeys={['route-a']} fallback={<div data-testid="fallback" />}>
        <Boom shouldThrow />
      </ErrorBoundary>,
    );
    expect(container.querySelector('[data-testid="fallback"]')).not.toBeNull();

    // Same key + healthy child: the boundary is still latched, as React requires.
    render(
      <ErrorBoundary resetKeys={['route-a']} fallback={<div data-testid="fallback" />}>
        <Boom shouldThrow={false} />
      </ErrorBoundary>,
    );
    expect(container.querySelector('[data-testid="ok"]')).toBeNull();

    // New key (a navigation) clears it and the children render again.
    render(
      <ErrorBoundary resetKeys={['route-b']} fallback={<div data-testid="fallback" />}>
        <Boom shouldThrow={false} />
      </ErrorBoundary>,
    );
    expect(container.querySelector('[data-testid="ok"]')).not.toBeNull();
    expect(container.querySelector('[data-testid="fallback"]')).toBeNull();
  });

  it('leaves healthy children mounted when resetKeys change', () => {
    render(
      <ErrorBoundary resetKeys={['route-a']}>
        <Boom shouldThrow={false} />
      </ErrorBoundary>,
    );
    const first = container.querySelector('[data-testid="ok"]');

    render(
      <ErrorBoundary resetKeys={['route-b']}>
        <Boom shouldThrow={false} />
      </ErrorBoundary>,
    );

    // Same DOM node — a reset must not remount a working subtree on every navigation.
    expect(container.querySelector('[data-testid="ok"]')).toBe(first);
  });
});
