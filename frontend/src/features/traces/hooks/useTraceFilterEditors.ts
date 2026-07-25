import { useLingui } from '@lingui/react/macro';
import { agentColor } from '../../../lib/colors';
import type { AgentListItemDto } from '../../../api/models';
import type { TraceAdvancedFilters } from '../tracesMeta';
import { ANOMALY_OPTION_LABELS, rangeChipValue, type TraceFilterFieldKey } from '../traceFilterFields';
import type { EditorSpec } from '../components/FilterValueEditor';
import { useTraceToolNames } from './useTraceToolNames';
import { useRecentSessions } from './useRecentSessions';

/**
 * Shared value-editor + chip-value logic for the composable trace filter bar. Both the "+ Filter"
 * picker (`TraceFilterPicker`, drilling into a field to set its value) and the active chips
 * (`TraceFilterBar`, editing an existing value) map a field to the same {@link EditorSpec} — this
 * hook is the single source so the two stay in lockstep. `apply` is passed per call because each
 * surface closes its own popover.
 */
export function useTraceFilterEditors(agents: AgentListItemDto[], filters: TraceAdvancedFilters) {
  const { t, i18n } = useLingui();
  // Scope the tool picker to the selected agent (when set) so it only lists that agent's tools.
  const toolNames = useTraceToolNames(filters.agent || undefined);
  // Recent sessions feed the session picker; the chip resolves an id back to its external key.
  const { sessions } = useRecentSessions();

  const editorSpec = (
    field: TraceFilterFieldKey,
    apply: (patch: Partial<TraceAdvancedFilters>) => void,
  ): EditorSpec => {
    switch (field) {
      case 'agent': return {
        kind: 'options',
        value: filters.agent,
        emptyText: t`No agents yet`,
        options: agents.map(a => ({ key: a.id, label: a.name, accent: agentColor(a.id) })),
        onApply: key => apply({ agent: key }),
      };
      case 'session': return {
        kind: 'options',
        value: filters.session,
        emptyText: t`No sessions yet`,
        options: sessions.map(s => ({ key: s.id, label: s.externalKey })),
        onApply: key => apply({ session: key }),
      };
      case 'anomaly': return {
        kind: 'options',
        value: filters.anomaly,
        emptyText: '',
        options: (Object.keys(ANOMALY_OPTION_LABELS) as (keyof typeof ANOMALY_OPTION_LABELS)[])
          .map(key => ({ key, label: i18n._(ANOMALY_OPTION_LABELS[key]) })),
        onApply: key => apply({ anomaly: key as TraceAdvancedFilters['anomaly'] }),
      };
      case 'tool': return {
        kind: 'options',
        value: filters.tool,
        emptyText: t`No tool calls recorded`,
        options: toolNames.map(name => ({ key: name, label: name })),
        onApply: key => apply({ tool: key }),
      };
      case 'model': return {
        kind: 'text',
        value: filters.model,
        placeholder: t`Model name contains…`,
        onApply: value => apply({ model: value }),
      };
      case 'status': return {
        kind: 'options',
        value: filters.statusClass,
        emptyText: '',
        options: [{ key: '2', label: '2xx' }, { key: '4', label: '4xx' }, { key: '5', label: '5xx' }],
        onApply: key => apply({ statusClass: key as TraceAdvancedFilters['statusClass'] }),
      };
      case 'tokens': return {
        kind: 'range',
        min: filters.minTokens,
        max: filters.maxTokens,
        onApply: (min, max) => apply({ minTokens: min, maxTokens: max }),
      };
      case 'latency': return {
        kind: 'range',
        min: filters.minLatencyMs,
        max: filters.maxLatencyMs,
        unit: 'ms',
        onApply: (min, max) => apply({ minLatencyMs: min, maxLatencyMs: max }),
      };
    }
  };

  const chipValue = (field: TraceFilterFieldKey): string => {
    switch (field) {
      case 'agent': return agents.find(a => a.id === filters.agent)?.name ?? filters.agent;
      case 'session': return sessions.find(s => s.id === filters.session)?.externalKey ?? filters.session;
      case 'anomaly': return filters.anomaly === '' ? '' : i18n._(ANOMALY_OPTION_LABELS[filters.anomaly]);
      case 'tool': return filters.tool;
      case 'model': return filters.model;
      case 'status': return `${filters.statusClass}xx`;
      case 'tokens': return rangeChipValue(filters.minTokens, filters.maxTokens);
      case 'latency': return rangeChipValue(filters.minLatencyMs, filters.maxLatencyMs, ' ms');
    }
  };

  return { editorSpec, chipValue };
}
