import { useLingui } from '@lingui/react/macro';
import type { ScopeListItemDto } from '../../../api/models';
import { scopeLabel } from '../../../lib/scopes';
import { Select } from '../../../components/ui/Select';
import { ALL_SCOPES } from '../dashboardMeta';

interface Props {
  scopes: ScopeListItemDto[];
  scopeId: string | undefined;
  onChange: (scopeId: string | undefined) => void;
}

/** Narrows the whole dashboard to one scope (use-case group of agents). Hidden when there are none. */
export function DashboardScopeSelect({ scopes, scopeId, onChange }: Props) {
  const { t } = useLingui();
  if (scopes.length === 0) return null;
  return (
    <div className="w-[200px]">
      <label htmlFor="dashboard-scope-select" className="sr-only">{t({ message: 'Scope', context: 'use-case group of agents' })}</label>
      <Select
        id="dashboard-scope-select"
        inputSize="sm"
        value={scopeId ?? ALL_SCOPES}
        onValueChange={value => onChange(value === ALL_SCOPES ? undefined : value)}
        data-testid="dashboard-scope-select"
      >
        <option value={ALL_SCOPES}>{t`All scopes`}</option>
        {scopes.map(s => <option key={s.id} value={s.id}>{scopeLabel(s)}</option>)}
      </Select>
    </div>
  );
}
