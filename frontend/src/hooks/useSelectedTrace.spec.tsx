// @vitest-environment jsdom
import { act, StrictMode, useState } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { createMemoryRouter, RouterProvider } from 'react-router';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { Button } from '../components/ui/Button';
import { QUERY_KEYS } from '../api/query-keys';
import { useSelectedTrace } from './useSelectedTrace';

vi.mock('../api/agent-calls', () => ({ agentCallsApi: { get: vi.fn() } }));

let container: HTMLDivElement;
let root: Root;
let client: QueryClient;
let router: ReturnType<typeof createMemoryRouter>;

function Host() {
  const [project, setProject] = useState('p1');
  const [trace, select] = useSelectedTrace(`traces.openTrace.${project}`);
  return <>
    <output>{trace?.id ?? 'closed'}</output>
    <Button onClick={() => select('a')}>Open</Button>
    <Button onClick={() => select(null)}>Close</Button>
    <Button onClick={() => setProject(project === 'p1' ? 'p2' : 'p1')}>Project</Button>
    <Button onClick={() => select('b', ['focus'])}>Focus</Button>
  </>;
}

beforeEach(() => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
  const storage = new Map<string, string>();
  vi.stubGlobal('localStorage', {
    getItem: (key: string) => storage.get(key) ?? null,
    setItem: (key: string, value: string) => storage.set(key, value),
  });
  client = new QueryClient({ defaultOptions: { queries: { staleTime: Infinity, retry: false } } });
  for (const id of ['a', 'b']) client.setQueryData(QUERY_KEYS.agentCall(id), { id });
  container = document.createElement('div');
  document.body.appendChild(container);
  root = createRoot(container);
});

afterEach(() => {
  act(() => root.unmount());
  router.dispose();
  client.clear();
  container.remove();
  vi.unstubAllGlobals();
});

async function render(path = '/traces') {
  router = createMemoryRouter([
    { path: '/traces', element: <Host /> },
    { path: '/tracey-ai', element: <div /> },
  ], { initialEntries: [path] });
  await act(async () => root.render(
    <StrictMode><QueryClientProvider client={client}><RouterProvider router={router} /></QueryClientProvider></StrictMode>,
  ));
}

const selected = () => container.querySelector('output')?.textContent;
const navigate = (path: string) => act(async () => { await router.navigate(path); });
const click = (label: string) => act(async () => {
  const button = [...container.querySelectorAll('button')].find(button => button.textContent === label);
  if (!button) throw new Error(`Missing ${label}`);
  button.click();
});

it('restores details after visiting Tracey and remembers an explicitly closed pane', async () => {
  await render();
  expect(selected()).toBe('closed');
  await click('Open');
  expect(selected()).toBe('a');
  await navigate('/tracey-ai');
  await navigate('/traces');
  expect(selected()).toBe('a');
  expect(router.state.location.search).toBe('?trace=a');
  await click('Close');
  expect(selected()).toBe('closed');
  await navigate('/tracey-ai');
  await navigate('/traces');
  expect(selected()).toBe('closed');
});

it('lets trace and focus deep links override remembered details', async () => {
  localStorage.setItem('traces.openTrace.p1', JSON.stringify('a'));
  await render('/traces?trace=b');
  expect(selected()).toBe('b');
  await navigate('/tracey-ai');
  await navigate('/traces');
  expect(selected()).toBe('b');
  await navigate('/tracey-ai');
  await navigate('/traces?focus=b');
  expect(selected()).toBe('closed');
  expect(router.state.location.search).toBe('?focus=b');
  await click('Focus');
  expect(selected()).toBe('b');
  expect(router.state.location.search).toBe('?trace=b');
});

it('restores details separately for each project', async () => {
  await render();
  await click('Open');
  await click('Project');
  expect(selected()).toBe('closed');
  await click('Focus');
  expect(selected()).toBe('b');
  await click('Project');
  expect(selected()).toBe('a');
});

it('ignores invalid storage and keeps URL selection working when storage is unavailable', async () => {
  localStorage.setItem('traces.openTrace.p1', '{broken');
  await render();
  expect(selected()).toBe('closed');
  vi.spyOn(localStorage, 'getItem').mockImplementation(() => { throw new Error('unavailable'); });
  vi.spyOn(localStorage, 'setItem').mockImplementation(() => { throw new Error('unavailable'); });
  await click('Open');
  expect(selected()).toBe('a');
  await click('Project');
  expect(selected()).toBe('closed');
  await click('Focus');
  expect(selected()).toBe('b');
  await click('Close');
  expect(selected()).toBe('closed');
});
