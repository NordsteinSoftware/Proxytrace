import { useState, type ReactNode } from 'react';
import { Trans, Plural, useLingui } from '@lingui/react/macro';
import type { AgentListItemDto } from '../../api/models';
import { agentColor } from '../../lib/colors';
import { selectionRowStyle, selectionBarStyle, SELECTION_ROW_INACTIVE } from '../../lib/selectionRow';
import { cn } from '../../lib/cn';
import { fmtRelative } from '../../lib/format';
import { ListRail } from '../../components/ui/ListRail';
import { RowButton } from '../../components/ui/RowButton';
import { EmptyState } from '../../components/ui/EmptyState';
import { EYEBROW_CLS } from '../../components/ui/classes';
import { scopeLabel } from '../../lib/scopes';
import type { AgentScopeGroup } from './agentScopes';

interface Props {
  agents: AgentListItemDto[];
  /** When set, the rail is sectioned by scope (an agent may appear in several sections). */
  groups: AgentScopeGroup[] | null;
  selectedId: string | null;
  onSelect: (id: string) => void;
  isLoading: boolean;
  /** The rail's filter band (system-agents toggle, scope controls). */
  filter?: ReactNode;
}

function matches(a: AgentListItemDto, q: string): boolean {
  return a.name.toLowerCase().includes(q)
    || a.projectName.toLowerCase().includes(q)
    || a.endpointName.toLowerCase().includes(q);
}

export function AgentList({ agents, groups, selectedId, onSelect, isLoading, filter }: Props) {
  const { t } = useLingui();
  const [search, setSearch] = useState('');

  const q = search.trim().toLowerCase();
  const filtered = q ? agents.filter(a => matches(a, q)) : agents;
  const sections = groups?.map(g => ({ ...g, agents: q ? g.agents.filter(a => matches(a, q)) : g.agents }))
    .filter(g => g.agents.length > 0);

  return (
    <ListRail
      listTestId="agent-list"
      title={t`Agents`}
      count={agents.length}
      search={{ value: search, onChange: setSearch, placeholder: t`Search agents…` }}
      filter={filter}
      loading={isLoading}
      isEmpty={filtered.length === 0}
      empty={<EmptyState title={search ? t`No matches` : t`No agents yet`} description={search ? t`Clear the search to see all agents.` : undefined} />}
    >
      {sections ? (
        <div className="flex flex-col gap-3" data-testid="agent-scope-groups">
          {sections.map(g => (
            <section key={g.scope?.id ?? 'unscoped'} className="flex flex-col gap-1.5">
              <h4 className={EYEBROW_CLS} data-testid={`agent-scope-group-${g.scope?.id ?? 'unscoped'}`}>
                {g.scope ? scopeLabel(g.scope) : <Trans>No scope</Trans>}
              </h4>
              {g.agents.map(a => (
                <AgentRow key={a.id} agent={a} selected={selectedId === a.id} onClick={() => onSelect(a.id)} />
              ))}
            </section>
          ))}
        </div>
      ) : (
        <div className="flex flex-col gap-1.5">
          {filtered.map(a => (
            <AgentRow
              key={a.id}
              agent={a}
              selected={selectedId === a.id}
              onClick={() => onSelect(a.id)}
            />
          ))}
        </div>
      )}
    </ListRail>
  );
}

function AgentRow({ agent, selected, onClick }: { agent: AgentListItemDto; selected: boolean; onClick: () => void }) {
  const c = agentColor(agent.id);
  const initial = agent.name[0]?.toUpperCase() ?? '?';

  return (
    <RowButton
      onClick={onClick}
      data-testid={`agent-card-${agent.id}`}
      className={cn(
        'rounded-lg relative overflow-hidden transition-[box-shadow,background-color] duration-150 px-3 py-2.5 pl-3.5',
        !selected && SELECTION_ROW_INACTIVE,
      )}
      style={selected ? selectionRowStyle(c) : undefined}
    >
      {selected && (
        <div aria-hidden className="absolute left-0 top-0 bottom-0 w-[3px]" style={selectionBarStyle(c)} />
      )}
      <div className="flex items-center gap-2.5 min-w-0">
        <div
          className="flex items-center justify-center shrink-0 w-[30px] h-[30px] rounded-md"
          style={{
            background: `color-mix(in srgb, ${c} 12%, transparent)`,
            border: `1px solid color-mix(in srgb, ${c} 30%, transparent)`,
          }}
        >
          <span className="text-title font-bold font-mono" style={{ color: c }}>{initial}</span>
        </div>
        <div className="flex-1 min-w-0">
          <div className="text-body font-semibold text-primary truncate">{agent.name}</div>
          <div className="text-caption text-muted truncate font-mono">{agent.endpointName}</div>
        </div>
      </div>
      <div className="flex items-center gap-2 mt-1.5 text-caption text-muted pl-10">
        <span className="truncate">{agent.projectName}</span>
        <span aria-hidden>·</span>
        <span className="shrink-0"><Plural value={agent.toolCount} one="# tool" other="# tools" /></span>
        <span className="ml-auto shrink-0 font-mono">{agent.lastUsedAt ? fmtRelative(agent.lastUsedAt) : <Trans>never</Trans>}</span>
      </div>
    </RowButton>
  );
}
