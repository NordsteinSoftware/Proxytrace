// @vitest-environment jsdom
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { I18nProvider } from '@lingui/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { i18n } from '../../../i18n';
import type { ModelEndpointDto } from '../../../api/models';
import { QUERY_KEYS } from '../../../api/query-keys';
import { providersApi } from '../../../api/providers';
import { ModelsSection } from './ModelsSection';

vi.mock('../../../api/providers', () => ({ providersApi: { updateModelPricing: vi.fn() } }));
vi.mock('../../../hooks/useToast', () => ({ default: () => ({ show: vi.fn() }) }));
(globalThis as Record<string, unknown>).IS_REACT_ACT_ENVIRONMENT = true;

const model: ModelEndpointDto = {
  id: 'model-1', providerId: 'provider-1', providerName: 'Provider', modelName: 'Model',
  inputTokenCost: 2, outputTokenCost: 4, cachedInputTokenCost: 1, manualPricing: true,
  createdAt: '', updatedAt: '',
};
let root: Root;
let container: HTMLDivElement;
let client: QueryClient;

beforeEach(() => {
  vi.resetAllMocks();
  i18n.loadAndActivate({ locale: 'en', messages: {} });
  client = new QueryClient({ defaultOptions: { mutations: { retry: false } } });
  container = document.createElement('div');
  document.body.appendChild(container);
  root = createRoot(container);
  act(() => root.render(
    <I18nProvider i18n={i18n}><QueryClientProvider client={client}>
      <ModelsSection providerId={model.providerId} models={[model]} reloading={false} onReload={() => {}} />
    </QueryClientProvider></I18nProvider>,
  ));
});

afterEach(() => {
  act(() => root.unmount());
  container.remove();
  client.clear();
});

async function click(text: string) {
  const button = [...document.querySelectorAll('button')].find(b => b.textContent === text);
  expect(button).toBeDefined();
  await act(async () => { button!.click(); });
}

function input(key: string) {
  return document.getElementById(`price-${key}TokenCost`) as HTMLInputElement;
}

function fill(key: string, value: string) {
  act(() => {
    Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')!.set!.call(input(key), value);
    input(key).dispatchEvent(new Event('input', { bubbles: true }));
  });
}

async function settle() {
  await act(async () => { await new Promise(resolve => setTimeout(resolve, 10)); });
}

it('prepopulates all prices, cancels edits, and saves zero and unknown prices together', async () => {
  await click('Edit prices');
  expect(input('input').value).toBe('2');
  expect(input('output').value).toBe('4');
  expect(input('cachedInput').value).toBe('1');
  expect(document.querySelector('[role="dialog"]')?.textContent).toContain('EUR per 1M tokens');
  fill('input', '9');
  await click('Cancel');
  expect(providersApi.updateModelPricing).not.toHaveBeenCalled();
  expect(document.querySelector('[role="dialog"]')).toBeNull();

  await click('Edit prices');
  expect(input('input').value).toBe('2');
  fill('input', '0');
  fill('output', '');
  fill('cachedInput', '');
  vi.mocked(providersApi.updateModelPricing).mockResolvedValue(model);
  const invalidate = vi.spyOn(client, 'invalidateQueries');
  await click('Save');
  await settle();
  expect(providersApi.updateModelPricing).toHaveBeenCalledWith(model.providerId, model.id, {
    inputTokenCost: 0, outputTokenCost: null, cachedInputTokenCost: null, manualPricing: true,
  });
  expect(document.querySelector('[role="dialog"]')).toBeNull();
  expect(invalidate).toHaveBeenCalledWith({ queryKey: QUERY_KEYS.providersOverview });
  expect(invalidate).toHaveBeenCalledWith({ queryKey: QUERY_KEYS.modelEndpoints });
});

it('shows numeric, precision, and cached-price errors without saving', async () => {
  await click('Edit prices');
  for (const bad of ['-1', 'oops', 'Infinity', '1000000000000', '0.0000001']) {
    fill('output', bad);
    await click('Save');
    expect(document.querySelector('[role="alert"]')?.textContent).toContain('up to 6 decimal places');
  }
  fill('output', '4');
  fill('cachedInput', '3');
  await click('Save');
  expect(document.querySelector('[role="alert"]')?.textContent).toContain('cannot exceed');
  expect(providersApi.updateModelPricing).not.toHaveBeenCalled();
});

it('keeps edits visible after failed saves and allows retry', async () => {
  await click('Edit prices');
  fill('input', '5');
  vi.mocked(providersApi.updateModelPricing).mockRejectedValueOnce(new Error('Unavailable'));
  await click('Save');
  await settle();
  expect(document.querySelector('[role="alert"]')?.textContent).toContain('Could not save prices');
  expect(input('input').value).toBe('5');
  vi.mocked(providersApi.updateModelPricing).mockResolvedValue(model);
  await click('Save');
  await settle();
  expect(document.querySelector('[role="dialog"]')).toBeNull();
});

it('restores automatic pricing and reports failures visibly', async () => {
  expect(container.textContent).toContain('Manual');
  vi.mocked(providersApi.updateModelPricing).mockRejectedValueOnce(new Error('Unavailable'));
  await click('Use automatic pricing');
  await settle();
  expect(document.querySelector('[role="alert"]')?.textContent).toContain('Could not restore automatic pricing');
  vi.mocked(providersApi.updateModelPricing).mockResolvedValue({ ...model, manualPricing: false });
  const invalidate = vi.spyOn(client, 'invalidateQueries');
  await click('Use automatic pricing');
  expect(providersApi.updateModelPricing).toHaveBeenLastCalledWith(model.providerId, model.id, {
    inputTokenCost: null, outputTokenCost: null, cachedInputTokenCost: null, manualPricing: false,
  });
  expect(invalidate).toHaveBeenCalledWith({ queryKey: QUERY_KEYS.providersOverview });
  expect(invalidate).toHaveBeenCalledWith({ queryKey: QUERY_KEYS.modelEndpoints });
});
