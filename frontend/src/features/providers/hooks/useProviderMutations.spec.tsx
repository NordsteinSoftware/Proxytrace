// @vitest-environment jsdom
import { act, useEffect } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { I18nProvider } from '@lingui/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest';
import { ModelProviderKind, type ProviderDto } from '../../../api/models';
import { i18n } from '../../../i18n';
import { ProviderConnectionTestError } from '../../../lib/providerConnection';

const mocks = vi.hoisted(() => ({
  testConnection: vi.fn(),
  update: vi.fn(),
  getUpstreamKey: vi.fn(),
  toast: vi.fn(),
}));

vi.mock('../../../api/setup', () => ({
  setupApi: { testConnection: mocks.testConnection },
}));
vi.mock('../../../api/providers', () => ({
  providersApi: { update: mocks.update, getUpstreamKey: mocks.getUpstreamKey },
}));
vi.mock('../../../hooks/useToast', () => ({
  default: () => ({ show: mocks.toast }),
}));

import { useRevealUpstreamKey, useRotateUpstreamKey } from './useProviderMutations';

(globalThis as Record<string, unknown>).IS_REACT_ACT_ENVIRONMENT = true;

const provider: ProviderDto = {
  id: 'provider-1',
  name: 'OpenAI',
  endpoint: 'https://api.openai.com/v1',
  upstreamApiKeyPreview: 'sk-••••••••••••-old',
  kind: ModelProviderKind.OpenAi,
  createdAt: '2026-01-01T00:00:00Z',
  updatedAt: '2026-01-01T00:00:00Z',
};

let latest: ReturnType<typeof useRotateUpstreamKey> | null = null;
let root: Root;
let container: HTMLDivElement;
let queryClient: QueryClient;

let latestReveal: ReturnType<typeof useRevealUpstreamKey> | null = null;

function Host() {
  const rotation = useRotateUpstreamKey(provider);
  const reveal = useRevealUpstreamKey(provider.id);
  useEffect(() => { latest = rotation; latestReveal = reveal; });
  return null;
}

function reveal(): ReturnType<typeof useRevealUpstreamKey> {
  if (latestReveal === null) throw new Error('Reveal hook has not rendered');
  return latestReveal;
}

function rotation(): ReturnType<typeof useRotateUpstreamKey> {
  if (latest === null) throw new Error('Rotation hook has not rendered');
  return latest;
}

beforeAll(() => i18n.loadAndActivate({ locale: 'en', messages: {} }));

beforeEach(() => {
  vi.clearAllMocks();
  mocks.update.mockResolvedValue({ ...provider, upstreamApiKeyPreview: 'sk-••••••••••••-new' });
  latest = null;
  queryClient = new QueryClient({ defaultOptions: { mutations: { retry: false } } });
  container = document.createElement('div');
  document.body.appendChild(container);
  root = createRoot(container);
  act(() => {
    root.render(
      <I18nProvider i18n={i18n}>
        <QueryClientProvider client={queryClient}>
          <Host />
        </QueryClientProvider>
      </I18nProvider>,
    );
  });
});

afterEach(() => {
  act(() => root.unmount());
  container.remove();
  queryClient.clear();
});

describe('useRotateUpstreamKey', () => {
  it('does not save when provider verification rejects the candidate key', async () => {
    mocks.testConnection.mockResolvedValue({
      success: false,
      errorCode: 'Unauthorized',
      modelCount: 0,
      error: null,
      errorId: null,
    });
    const invalidate = vi.spyOn(queryClient, 'invalidateQueries');
    let caught: unknown;

    await act(async () => {
      try {
        await rotation().mutateAsync('sk-new');
      } catch (error) {
        caught = error;
      }
    });

    expect(mocks.testConnection).toHaveBeenCalledWith({
      providerName: provider.name,
      providerEndpoint: provider.endpoint,
      providerUpstreamApiKey: 'sk-new',
      providerKind: provider.kind,
    });
    expect(caught).toBeInstanceOf(ProviderConnectionTestError);
    expect((caught as ProviderConnectionTestError).errorCode).toBe('Unauthorized');
    expect(mocks.update).not.toHaveBeenCalled();
    expect(invalidate).not.toHaveBeenCalled();
    expect(mocks.toast).not.toHaveBeenCalled();
  });

  it('saves only after successful verification and preserves the other provider fields', async () => {
    mocks.testConnection.mockResolvedValue({
      success: true,
      errorCode: null,
      modelCount: 2,
      error: null,
      errorId: null,
    });
    const invalidate = vi.spyOn(queryClient, 'invalidateQueries');
    let result: { provider: ProviderDto; modelCount: number } | undefined;

    await act(async () => {
      result = await rotation().mutateAsync('sk-new');
    });

    expect(mocks.testConnection.mock.invocationCallOrder[0]).toBeLessThan(mocks.update.mock.invocationCallOrder[0]);
    expect(mocks.update).toHaveBeenCalledWith(provider.id, {
      name: provider.name,
      endpoint: provider.endpoint,
      upstreamApiKey: 'sk-new',
      kind: provider.kind,
    });
    expect(result?.modelCount).toBe(2);
    expect(invalidate).toHaveBeenCalled();
    expect(mocks.toast).toHaveBeenCalledWith('Upstream API key updated.', 'success');
  });

  it('fetches the cleartext key only when reveal is invoked, never on render', async () => {
    // The key used to arrive with the providers page itself. It must now cost an explicit,
    // server-audited request — so simply rendering the hook must not ask for it.
    expect(mocks.getUpstreamKey).not.toHaveBeenCalled();

    mocks.getUpstreamKey.mockResolvedValue({ upstreamApiKey: 'sk-secret' });
    let revealed: string | undefined;
    await act(async () => {
      revealed = await reveal().mutateAsync();
    });

    expect(mocks.getUpstreamKey).toHaveBeenCalledExactlyOnceWith(provider.id);
    expect(revealed).toBe('sk-secret');
  });

  it('keeps the revealed key out of the query cache', async () => {
    // A secret in the Query cache outlives the moment the user asked for it.
    mocks.getUpstreamKey.mockResolvedValue({ upstreamApiKey: 'sk-secret' });
    await act(async () => { await reveal().mutateAsync(); });

    const cached = JSON.stringify(queryClient.getQueryCache().getAll().map(q => q.state.data));
    expect(cached).not.toContain('sk-secret');
  });

  it('saves when verification succeeds with zero reported models', async () => {
    mocks.testConnection.mockResolvedValue({
      success: true,
      errorCode: null,
      modelCount: 0,
      error: null,
      errorId: null,
    });
    let result: { provider: ProviderDto; modelCount: number } | undefined;

    await act(async () => {
      result = await rotation().mutateAsync('sk-new');
    });

    expect(mocks.update).toHaveBeenCalledOnce();
    expect(result?.modelCount).toBe(0);
  });
});
