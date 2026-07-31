// @vitest-environment jsdom
/**
 * Regression spec for #490 — the Costs page crashing for every non-admin member.
 *
 * The providers overview this hook reads is `[Authorize(Roles = Admin)]`, but reading the Costs
 * page is free for any project member. Firing it unconditionally earned a member a 403, and the
 * app's `QueryClient` defaults carry `throwOnError: true` (`app/queryClient.ts`), so the rejection
 * was rethrown during render and the route's boundary replaced the whole page.
 *
 * Two independent guarantees, one per failure mode: a non-admin must not send the request at all,
 * and a request that fails anyway must settle in place instead of throwing out of render.
 */
import { describe, it, vi, beforeEach, afterEach, expect } from 'vitest';
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';

const overview = vi.fn();
vi.mock('../../../api/providers', () => ({ providersApi: { overview: () => overview() } }));
vi.mock('../../../hooks/useCurrentProject', () => ({
  default: () => ({ currentProjectId: 'p1' }),
}));

import { useProjectApiKeys } from './useProjectApiKeys';

(globalThis as Record<string, unknown>).IS_REACT_ACT_ENVIRONMENT = true;

/** The production defaults, verbatim from `app/queryClient.ts` — the point of the spec. */
function productionClient() {
  return new QueryClient({
    defaultOptions: { queries: { retry: false, staleTime: 30_000, throwOnError: true } },
  });
}

describe('useProjectApiKeys', () => {
  let container: HTMLDivElement;
  let root: Root;
  const consoleError = console.error;

  beforeEach(() => {
    overview.mockReset();
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

  function ScopePicker({ isAdmin }: { isAdmin: boolean }) {
    const { apiKeys } = useProjectApiKeys(isAdmin);
    return <div data-testid="picker">{`keys:${apiKeys.length}`}</div>;
  }

  /**
   * Renders the picker and flushes a fixed number of turns. Deliberately *not* "loop until the
   * text settles": an empty key list is also the loading state, so such a loop would exit before
   * the request even resolved and assert nothing.
   */
  async function renderPicker(isAdmin: boolean) {
    const client = productionClient();
    await act(async () => {
      root.render(
        <QueryClientProvider client={client}><ScopePicker isAdmin={isAdmin} /></QueryClientProvider>,
      );
    });
    for (let i = 0; i < 20; i++) {
      await act(async () => { await new Promise(resolve => setTimeout(resolve, 0)); });
    }
  }

  it('never asks the Admin-only endpoint on behalf of a non-admin member', async () => {
    overview.mockRejectedValue(Object.assign(new Error('403 Forbidden'), { status: 403 }));

    await renderPicker(false);

    // The 403 that took the page down was earned by asking at all.
    expect(overview).not.toHaveBeenCalled();
    expect(container.textContent).toBe('keys:0');
  });

  it('does not throw out of render when the request fails for an admin', async () => {
    overview.mockRejectedValue(Object.assign(new Error('500 Internal Server Error'), { status: 500 }));

    await renderPicker(true);

    // Rendered, not unmounted — an empty container is the error boundary eating the page.
    expect(overview).toHaveBeenCalled();
    expect(container.querySelector('[data-testid="picker"]')).not.toBeNull();
    expect(container.textContent).toBe('keys:0');
  });

  it('returns the current project\'s keys for an admin', async () => {
    overview.mockResolvedValue({
      providers: [
        { keys: [{ id: 'k1', projectId: 'p1' }, { id: 'k2', projectId: 'other' }] },
        { keys: [{ id: 'k3', projectId: 'p1' }] },
      ],
      projects: [],
    });

    await renderPicker(true);

    expect(container.textContent).toBe('keys:2');
  });
});
