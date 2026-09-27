import { useState } from 'react';
import { Button } from '../../../components/ui/Button';
import { FilterChip } from '../../../components/ui/FilterChip';
import { Popover } from '../../../components/ui/Popover';
import { agentColor } from '../../../lib/colors';
import { Trans, useLingui } from '@lingui/react/macro';
import type { AgentListItemDto } from '../../../api/models';
import type { TraceAdvancedFilters } from '../tracesMeta';
import {
  TRACE_FILTER_FIELDS, clearFieldPatch, isFieldActive, type TraceFilterFieldKey,
} from '../traceFilterFields';
import { useTraceFilterEditors } from '../hooks/useTraceFilterEditors';
import { FilterValueEditor } from './FilterValueEditor';

interface Props {
  agents: AgentListItemDto[];
  filters: TraceAdvancedFilters;
  onChange: (patch: Partial<TraceAdvancedFilters>) => void;
  onClearAll: () => void;
  showSystem: boolean;
  onShowSystemChange: (value: boolean) => void;
}

/**
 * The chip row under the toolbar: one removable, click-to-edit chip per active filter (plus a
 * **System traces** chip when that view toggle is on). The "+ Filter" picker itself lives on the
 * toolbar row ({@link TraceFilterPicker}); value editors come from {@link FilterValueEditor} and
 * the field registry from `traceFilterFields.ts`. Renders nothing when no filter is active.
 */
export function TraceFilterBar({ agents, filters, onChange, onClearAll, showSystem, onShowSystemChange }: Props) {
  const { t, i18n } = useLingui();
  const { editorSpec, chipValue } = useTraceFilterEditors(agents, filters);
  // One popover open at a time: an active chip's editor, or the system chip's remove panel.
  const [open, setOpen] = useState<TraceFilterFieldKey | 'system' | null>(null);

  const apply = (patch: Partial<TraceAdvancedFilters>) => {
    onChange(patch);
    setOpen(null);
  };

  const activeFields = TRACE_FILTER_FIELDS.filter(f => isFieldActive(f.key, filters));
  if (activeFields.length === 0 && !showSystem) return null;

  const systemLabel = t`System traces`;

  const editorPanel = (field: TraceFilterFieldKey) => (
    <div className="w-64">
      <FilterValueEditor key={field} spec={editorSpec(field, apply)} />
      <div className="border-t border-hairline px-2 py-1.5">
        <Button
          variant="link"
          size="sm"
          className="text-body-sm"
          data-testid="traces-filter-remove"
          onClick={() => apply(clearFieldPatch(field))}
        >
          <Trans>Remove filter</Trans>
        </Button>
      </div>
    </div>
  );

  const systemPanel = (
    <div className="w-56">
      <p className="px-3.5 py-3 text-body-sm text-muted">
        <Trans>Including traces from system agents.</Trans>
      </p>
      <div className="border-t border-hairline px-2 py-1.5">
        <Button
          variant="link"
          size="sm"
          className="text-body-sm"
          data-testid="traces-filter-remove-system"
          onClick={() => {
            onShowSystemChange(false);
            setOpen(null);
          }}
        >
          <Trans>Remove filter</Trans>
        </Button>
      </div>
    </div>
  );

  return (
    <div className="flex items-center gap-2 flex-wrap">
      {activeFields.map(f => (
        <Popover
          key={f.key}
          open={open === f.key}
          onOpenChange={o => setOpen(o ? f.key : null)}
          trigger={
            <span data-testid={`traces-filter-chip-${f.key}`}>
              <FilterChip
                label={`${i18n._(f.label)}:`}
                value={chipValue(f.key)}
                active
                accent={f.key === 'agent' && filters.agent ? agentColor(filters.agent) : undefined}
              />
            </span>
          }
        >
          {editorPanel(f.key)}
        </Popover>
      ))}

      {showSystem && (
        <Popover
          open={open === 'system'}
          // eslint-disable-next-line lingui/no-unlocalized-strings -- state token, not UI copy
          onOpenChange={o => setOpen(o ? 'system' : null)}
          trigger={
            <span data-testid="traces-filter-chip-system">
              <FilterChip label={`${systemLabel}:`} value={t`Shown`} active />
            </span>
          }
        >
          {systemPanel}
        </Popover>
      )}

      <Button
        variant="link"
        size="sm"
        className="text-body-sm"
        data-testid="traces-clear-filters"
        onClick={onClearAll}
      >
        <Trans>Clear filters</Trans>
      </Button>
    </div>
  );
}
