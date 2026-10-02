import { Trans, useLingui } from '@lingui/react/macro';
import type { ScopeListItemDto } from '../../api/models';
import { SwitchPill } from '../../components/ui/SwitchPill';
import { AgentScopeFilter } from './AgentScopeFilter';
import type { AgentScopeFilter as ScopeFilter } from './agentScopes';

interface Props {
  scopes: ScopeListItemDto[];
  scope: { filter: ScopeFilter; onFilterChange: (f: ScopeFilter) => void; grouped: boolean; onGroupedChange: (g: boolean) => void };
  showSystem: boolean;
  /** Omitted when the project has no system agents — the toggle is then hidden. */
  onToggleSystem?: () => void;
}

/** The agents rail's filter band: scope controls (when the project has scopes) + system-agents toggle. */
export function AgentRailFilters({ scopes, scope, showSystem, onToggleSystem }: Props) {
  const { t } = useLingui();
  return (
    <div className="flex flex-col gap-2">
      {scopes.length > 0 && (
        <AgentScopeFilter
          scopes={scopes}
          filter={scope.filter}
          onFilterChange={scope.onFilterChange}
          grouped={scope.grouped}
          onGroupedChange={scope.onGroupedChange}
        />
      )}
      {onToggleSystem && (
        <SwitchPill
          checked={showSystem}
          onChange={onToggleSystem}
          title={showSystem ? t`Hide system agents` : t`Show system agents`}
          label={<Trans>System Agents</Trans>}
        />
      )}
    </div>
  );
}
