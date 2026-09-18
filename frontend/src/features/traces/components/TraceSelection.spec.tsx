// @vitest-environment jsdom
import { act, useState } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { setupI18n } from '@lingui/core';
import { I18nProvider } from '@lingui/react';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import type { AgentCallListItemDto, AgentCallSummaryDto } from '../../../api/models';
import { TooltipProvider } from '../../../components/ui/Tooltip';
import { useTraceSelection } from '../hooks/useTraceSelection';
import { FlatTraceRow } from './FlatTraceRow';
import { ConversationGroupRow } from './ConversationGroupRow';
import { TraceSummary } from './TraceSummary';
import { TraceToolbar } from './TraceToolbar';

const i18n = setupI18n({ locale: 'en', messages: { en: {} } });
const onOpen = vi.fn();
const traces: AgentCallListItemDto[] = [100, 300, 500].map((durationMs, i) => ({
  id: String(i), agentId: null, agentName: null, model: 'gpt-4o', provider: 'openai',
  messagePreview: 'Hello', toolCount: 0, inputTokens: 100, outputTokens: 50,
  cachedInputTokens: 20, durationMs, httpStatus: i === 1 ? 300 : 200,
  finishReason: null, errorMessage: null, costEur: i === 0 ? null : i === 1 ? 0 : 0.02,
  createdAt: '2026-09-18T10:00:00Z', updatedAt: '2026-09-18T10:00:00Z',
  conversationId: i ? 'conversation' : null, sessionId: null, outlierFlags: 0,
}));
let container: HTMLDivElement;
let root: Root;
const allStats: AgentCallSummaryDto = {
  count: 3, inputTokens: 300, outputTokens: 150, cachedInputTokens: 60,
  totalCostEur: 0.02, avgLatencyMs: 300, latencyStdDevMs: Math.sqrt(80000 / 3), errorCount: 1,
};

function Host({ rows, scope }: { rows: AgentCallListItemDto[]; scope: string }) {
  const selected = useTraceSelection(rows, scope);
  const [expanded, setExpanded] = useState(true);
  return (
    <I18nProvider i18n={i18n}>
      <TooltipProvider>
        <output>{JSON.stringify(selected.summary)}</output>
        <TraceToolbar search="" timeRange={{ kind: 'all' }} onSearchChange={() => {}} onTimeRangeChange={() => {}}
          onClearSelection={selected.summary ? selected.clear : undefined} />
        <TraceSummary stats={selected.summary ?? allStats} selectionOnly={selected.summary !== null} />
        <FlatTraceRow trace={rows[0]} selected={false} fresh={false} onClick={onOpen} statsSelection={selected.selection} />
        <ConversationGroupRow group={{ type: 'conversation', conversationId: 'conversation', turns: rows.slice(1) }}
          expanded={expanded} onToggle={() => setExpanded(!expanded)} selectedId={null}
          freshIds={new Set()} onSelectTrace={onOpen} statsSelection={selected.selection} />
      </TooltipProvider>
    </I18nProvider>
  );
}

beforeEach(() => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
  onOpen.mockClear();
  container = document.createElement('div');
  document.body.appendChild(container);
  root = createRoot(container);
});

afterEach(() => {
  act(() => root.unmount());
  container.remove();
  vi.unstubAllGlobals();
});

function element<T extends Element>(selector: string): T {
  const found = container.querySelector<T>(selector);
  if (!found) throw new Error(`Missing ${selector}`);
  return found;
}
const checkbox = (id: string) => element<HTMLInputElement>(`[aria-label="Select trace ${id}"]`);
const groupCheckbox = () => element<HTMLInputElement>('[aria-label$="traces in this group"]');
const click = (node: HTMLElement) => act(() => node.click());
const render = (rows = traces, scope = 'project/filter/sort') => act(() => root.render(<Host rows={rows} scope={scope} />));
const summary = (): AgentCallSummaryDto | null => JSON.parse(element('output').textContent ?? 'null');

it('selects individual and grouped rows without opening details or collapsing the group, and clears stats', () => {
  render();
  expect(summary()).toBeNull();
  const clearSelection = element<HTMLButtonElement>('[data-testid="traces-clear-selection"]');
  expect(clearSelection.disabled).toBe(true);
  const controlCount = container.querySelectorAll('button').length;
  expect(element('[data-testid="trace-summary-count"]').textContent).toContain('matching filters');
  click(checkbox('0'));
  expect(clearSelection.disabled).toBe(false);
  expect(container.querySelectorAll('button')).toHaveLength(controlCount);
  expect(element('section').children).toHaveLength(1);
  expect(container.querySelector('[role="status"]')).toBeNull();
  expect(summary()).toMatchObject({ count: 1, totalCostEur: null, avgLatencyMs: 100, latencyStdDevMs: 0 });
  expect(element('[data-testid="trace-summary-count"]').textContent).toContain('selected traces');
  click(checkbox('1'));
  expect(groupCheckbox().indeterminate).toBe(true);
  expect(groupCheckbox().getAttribute('aria-checked')).toBe('mixed');
  expect(summary()).toEqual({ count: 2, inputTokens: 200, outputTokens: 100, cachedInputTokens: 40,
    totalCostEur: 0, avgLatencyMs: 200, latencyStdDevMs: 100, errorCount: 1 });
  click(groupCheckbox());
  expect(summary()).toMatchObject({ count: 3, totalCostEur: 0.02, avgLatencyMs: 300 });
  expect(checkbox('1').checked && checkbox('2').checked).toBe(true);
  expect(groupCheckbox().indeterminate).toBe(false);
  expect(onOpen).not.toHaveBeenCalled();
  click(element('[data-testid="trace-row-0"]'));
  expect(onOpen).toHaveBeenCalledOnce();
  click(element('[data-testid="conversation-group-row-conversation"]'));
  expect(container.querySelector('[data-testid="conversation-turn-1"]')).toBeNull();
  expect(summary()?.count).toBe(3);
  click(groupCheckbox());
  expect(summary()?.count).toBe(1);
  click(clearSelection);
  expect(summary()).toBeNull();
  expect(clearSelection.disabled).toBe(true);
  expect(checkbox('0').checked).toBe(false);
  expect(element('[data-testid="trace-summary-count"]').textContent).toContain('matching filters');
});

it('keeps selected IDs across new pages and live updates, and resets on query context changes', () => {
  render();
  click(checkbox('0'));
  const updated = [{ ...traces[0], durationMs: 200, costEur: 0.01 }, ...traces.slice(1), { ...traces[2], id: '3' }];
  render(updated);
  expect(summary()).toMatchObject({ count: 1, avgLatencyMs: 200, totalCostEur: 0.01 });
  expect(checkbox('0').checked).toBe(true);
  expect(checkbox('3').checked).toBe(false);
  render(updated, 'other-project/filter/sort');
  expect(summary()).toBeNull();
  expect(checkbox('0').checked).toBe(false);
  render(updated);
  expect(summary()).toBeNull();
});
