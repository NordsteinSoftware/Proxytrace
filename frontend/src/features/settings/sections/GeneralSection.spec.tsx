// @vitest-environment jsdom
import { act, type ReactNode } from 'react';
import { createRoot } from 'react-dom/client';
import { setupI18n } from '@lingui/core';
import { I18nProvider } from '@lingui/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter } from 'react-router';
import { expect, it, vi } from 'vitest';
import { GeneralSection } from './GeneralSection';

const { update, project } = vi.hoisted(() => ({
  update: vi.fn(),
  project: { id: 'p1', name: 'Translogica', systemEndpointId: 'e1', defaultUpstreamProviderId: null as string | null,
    members: [], createdAt: '2026-09-22', updatedAt: '2026-09-22' },
}));
vi.mock('../../../api/projects', () => ({ projectsApi: { updateDefaultUpstreamProvider: update } }));
vi.mock('../../../hooks/useCurrentProject', () => ({ default: () => ({ currentProjectId: 'p1' }) }));
vi.mock('../../../hooks/useModelEndpoints', () => ({ default: () => ({ data: [] }) }));
vi.mock('../../providers/hooks/useProviderQueries', () => ({
  useProvidersOverview: () => ({ data: { providers: [{ provider: { id: 'upstream', name: 'Upstream' } }] } }),
}));
vi.mock('../hooks/useProjects', async importOriginal => ({
  ...await importOriginal<typeof import('../hooks/useProjects')>(),
  useProject: () => ({ data: project }),
}));
// Exercise the settings/API connection independently of the shared dropdown implementation.
vi.mock('../../../components/ui/Select', () => ({
  Select: ({ value, onValueChange, children, id }: {
    value: string; onValueChange: (value: string) => void; children: ReactNode; id?: string;
  // eslint-disable-next-line no-restricted-syntax -- native test double for the shared Select
  }) => <select id={id} value={value} onChange={e => onValueChange(e.target.value)}>{children}</select>,
}));

it('edits, cancels, saves, and clears the project default', async () => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const i18n = setupI18n({ locale: 'en', messages: { en: {} } });
  const container = document.createElement('div');
  const root = createRoot(container);
  update.mockImplementation(async (_id: string, providerId: string | null) => {
    project.defaultUpstreamProviderId = providerId;
    return project;
  });
  const render = () => root.render(
    <I18nProvider i18n={i18n}><QueryClientProvider client={client}><MemoryRouter>
      <GeneralSection />
    </MemoryRouter></QueryClientProvider></I18nProvider>,
  );
  try {
    const click = async (label: string) => {
      await act(async () => container.querySelector<HTMLButtonElement>(`[aria-label="${label}"]`)!.click());
    };
    const choose = async (value: string) => {
      await act(async () => {
        const select = container.querySelector<HTMLSelectElement>('#default-upstream-provider')!;
        select.value = value;
        select.dispatchEvent(new Event('change', { bubbles: true }));
      });
    };
    await act(async () => render());
    expect(container.querySelector('#default-upstream-provider')).toBeNull();
    await click('Edit default upstream provider');
    expect(container.querySelector<HTMLSelectElement>('#default-upstream-provider')!.value).toBe('');
    expect(container.querySelector<HTMLButtonElement>('[aria-label="Save default upstream provider"]')!.disabled).toBe(true);
    await choose('upstream');
    expect(update).not.toHaveBeenCalled();
    await click('Cancel');
    expect(container.querySelector('#default-upstream-provider')).toBeNull();
    expect(update).not.toHaveBeenCalled();

    await click('Edit default upstream provider');
    expect(container.querySelector<HTMLSelectElement>('#default-upstream-provider')!.value).toBe('');
    await choose('upstream');
    await click('Save default upstream provider');
    expect(update).toHaveBeenLastCalledWith('p1', 'upstream');
    await act(async () => render());
    expect(container.querySelector('#default-upstream-provider')).toBeNull();
    expect(container.textContent).toContain('Upstream');

    await click('Edit default upstream provider');
    expect(container.querySelector<HTMLSelectElement>('#default-upstream-provider')!.value).toBe('upstream');
    await choose('');
    await click('Save default upstream provider');
    expect(update).toHaveBeenLastCalledWith('p1', null);
  } finally {
    act(() => root.unmount());
    client.clear();
    vi.unstubAllGlobals();
  }
});
