// @vitest-environment jsdom
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { MemoryRouter } from 'react-router';
import { setupI18n } from '@lingui/core';
import { I18nProvider } from '@lingui/react';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { RunList } from './RunList';

const i18n = setupI18n({ locale: 'en', messages: { en: {} } });
const onChange = vi.fn();
let container: HTMLDivElement;
let root: Root;

function render(agentId: string) {
  act(() => root.render(
    <I18nProvider i18n={i18n}>
      <MemoryRouter>
        <RunList
          groups={[]}
          isLoading={false}
          selectedId={null}
          onSelect={() => {}}
          onDelete={() => {}}
          agentFilter={{ value: agentId, options: [], onChange }}
          showSystem={false}
          onToggleSystem={() => {}}
          hasMore={false}
          onLoadMore={() => {}}
          isLoadingMore={false}
        />
      </MemoryRouter>
    </I18nProvider>,
  ));
}

beforeEach(() => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
  onChange.mockClear();
  container = document.createElement('div');
  document.body.appendChild(container);
  root = createRoot(container);
});

afterEach(() => {
  act(() => root.unmount());
  container.remove();
  vi.unstubAllGlobals();
});

it('clears an empty agent filter and links to suites when the unfiltered list is empty', () => {
  render('agent-1');
  expect(container.textContent).toContain('No runs for this agent');
  const clear = Array.from(container.querySelectorAll('button')).find(button => button.textContent?.includes('Clear filter'))!;
  act(() => clear.click());
  expect(onChange).toHaveBeenCalledWith('');

  render('');
  expect(container.textContent).toContain('No test runs yet');
  expect(container.querySelector<HTMLAnchorElement>('a[href="/suites"]')).not.toBeNull();
});
