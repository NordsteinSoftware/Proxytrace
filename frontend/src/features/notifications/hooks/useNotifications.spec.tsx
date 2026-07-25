// @vitest-environment jsdom
/**
 * Regression spec for the topbar white-screen. The app's `QueryClient` defaults carry
 * `throwOnError: true` (`app/queryClient.ts`), and the notifications bell renders in the masthead —
 * outside the router `Outlet`, so a route-level boundary structurally cannot catch it. A rethrown
 * inbox error therefore used to unmount the whole React root: a blank page on every route until a
 * reload. {@link useNotifications} must settle into `isError` instead.
 */
import { describe, it, vi, beforeEach, afterEach, expect } from 'vitest';
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';

const list = vi.fn();
vi.mock('../../../api/notifications', () => ({ notificationsApi: { list: () => list() } }));

import { useNotifications } from './useNotifications';

(globalThis as Record<string, unknown>).IS_REACT_ACT_ENVIRONMENT = true;

/** The production defaults, verbatim from `app/queryClient.ts` — the point of the spec. */
function productionClient() {
  return new QueryClient({
    defaultOptions: { queries: { retry: false, staleTime: 30_000, throwOnError: true } },
  });
}

describe('useNotifications', () => {
  let container: HTMLDivElement;
  let root: Root;
  const consoleError = console.error;

  beforeEach(() => {
    list.mockReset();
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

  function Bell() {
    const { isError, data } = useNotifications('p1');
    return <div data-testid="bell">{isError ? 'error' : `count:${data?.length ?? 0}`}</div>;
  }

  /** Renders the bell and flushes until the query settles (or the budget runs out). */
  async function renderBell(settled: string) {
    const client = productionClient();
    await act(async () => {
      root.render(<QueryClientProvider client={client}>{<Bell />}</QueryClientProvider>);
    });
    for (let i = 0; i < 50 && container.textContent !== settled; i++) {
      await act(async () => { await new Promise(resolve => setTimeout(resolve, 0)); });
    }
  }

  it('does not throw out of render when the inbox request fails', async () => {
    list.mockRejectedValue(Object.assign(new Error('500 Internal Server Error'), { status: 500 }));

    await renderBell('error');

    // Rendered, not unmounted — an empty container is the white-screen this guards against.
    expect(container.querySelector('[data-testid="bell"]')).not.toBeNull();
    expect(container.textContent).toBe('error');
  });

  it('renders the notifications when the request succeeds', async () => {
    list.mockResolvedValue([{ id: 'n1' }, { id: 'n2' }]);

    await renderBell('count:2');

    expect(container.textContent).toBe('count:2');
  });
});
