import { useLingui } from '@lingui/react/macro';
import { Checkbox } from '../../../components/ui/Checkbox';
import type { AgentCallListItemDto } from '../../../api/models';
import type { TraceStatsSelection } from '../hooks/useTraceSelection';

export function TraceSelectionCheckbox({ traces, selection }: {
  traces: AgentCallListItemDto[];
  selection: TraceStatsSelection;
}) {
  const { t } = useLingui();
  const checked = traces.every(trace => selection.ids.has(trace.id));
  const mixed = !checked && traces.some(trace => selection.ids.has(trace.id));
  return (
    <span className="inline-flex shrink-0" onClick={event => event.stopPropagation()}>
      <Checkbox
        aria-label={traces.length === 1 ? t`Select trace ${traces[0].id}` : t`Select ${traces.length} traces in this group`}
        // eslint-disable-next-line lingui/no-unlocalized-strings -- ARIA tristate token, not UI copy
        aria-checked={mixed ? 'mixed' : checked}
        checked={checked}
        ref={input => { if (input) input.indeterminate = mixed; }}
        onChange={event => selection.onChange(traces, event.target.checked)}
      />
    </span>
  );
}
