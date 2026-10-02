import { Trans, useLingui } from '@lingui/react/macro';
import type { ScopeListItemDto } from '../../api/models';
import { scopeLabel } from '../../lib/scopes';
import { Select } from '../../components/ui/Select';
import { SwitchPill } from '../../components/ui/SwitchPill';
import { parseScopeFilterValue, scopeFilterValue, type AgentScopeFilter as Filter } from './agentScopes';

interface Props {
  scopes: ScopeListItemDto[];
  filter: Filter;
  onFilterChange: (filter: Filter) => void;
  grouped: boolean;
  onGroupedChange: (grouped: boolean) => void;
}

/**
 * Agents-rail scope controls: narrow to one scope (or to agents in none), or section the list by
 * scope. Rendered only when the project has scopes.
 */
export function AgentScopeFilter({ scopes, filter, onFilterChange, grouped, onGroupedChange }: Props) {
  const { t } = useLingui();
  return (
    <div className="flex flex-col gap-2" data-testid="agent-scope-filter">
      <label htmlFor="agent-scope-select" className="sr-only">{t({ message: 'Scope', context: 'use-case group of agents' })}</label>
      <Select
        id="agent-scope-select"
        inputSize="sm"
        value={scopeFilterValue(filter)}
        onValueChange={value => onFilterChange(parseScopeFilterValue(value))}
        data-testid="agent-scope-select"
      >
        <option value={scopeFilterValue({ kind: 'all' })}>{t`All scopes`}</option>
        {scopes.map(s => <option key={s.id} value={s.id}>{scopeLabel(s)}</option>)}
        <option value={scopeFilterValue({ kind: 'unscoped' })}>{t`No scope`}</option>
      </Select>
      {filter.kind === 'all' && (
        <SwitchPill
          checked={grouped}
          onChange={onGroupedChange}
          label={<Trans>Group by scope</Trans>}
          data-testid="agent-group-by-scope"
        />
      )}
    </div>
  );
}
