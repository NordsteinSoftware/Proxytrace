import { useState } from 'react';
import { Plural, Trans, useLingui } from '@lingui/react/macro';
import type { ScopeListItemDto } from '../../../api/models';
import { scopeColor } from '../../../lib/colors';
import { scopeLabel } from '../../../lib/scopes';
import { selectionRowStyle, selectionBarStyle, SELECTION_ROW_INACTIVE } from '../../../lib/selectionRow';
import { cn } from '../../../lib/cn';
import { fmtRelative } from '../../../lib/format';
import { ListRail } from '../../../components/ui/ListRail';
import { RowButton } from '../../../components/ui/RowButton';
import { EmptyState } from '../../../components/ui/EmptyState';

interface Props {
  scopes: ScopeListItemDto[];
  selectedId: string | null;
  onSelect: (id: string) => void;
  isLoading: boolean;
  /** Opens the "new scope" URL builder (scopes are created by traffic, not by a form). */
  onCreate: () => void;
}

export function ScopeList({ scopes, selectedId, onSelect, isLoading, onCreate }: Props) {
  const { t } = useLingui();
  const [search, setSearch] = useState('');

  const q = search.trim().toLowerCase();
  const filtered = q
    ? scopes.filter(s => s.key.includes(q) || (s.displayName ?? '').toLowerCase().includes(q))
    : scopes;

  return (
    <ListRail
      listTestId="scope-list"
      title={t`Scopes`}
      count={scopes.length}
      create={{ onClick: onCreate, label: t`New scope`, testId: 'scope-new-btn' }}
      search={{ value: search, onChange: setSearch, placeholder: t`Search scopes…` }}
      loading={isLoading}
      isEmpty={filtered.length === 0}
      empty={<EmptyState title={t`No matches`} description={t`Clear the search to see all scopes.`} />}
    >
      <div className="flex flex-col gap-1.5">
        {filtered.map(s => (
          <ScopeRow key={s.id} scope={s} selected={selectedId === s.id} onClick={() => onSelect(s.id)} />
        ))}
      </div>
    </ListRail>
  );
}

function ScopeRow({ scope, selected, onClick }: { scope: ScopeListItemDto; selected: boolean; onClick: () => void }) {
  const c = scopeColor(scope.id);
  return (
    <RowButton
      onClick={onClick}
      data-testid={`scope-row-${scope.id}`}
      className={cn(
        'rounded-lg relative overflow-hidden transition-[box-shadow,background-color] duration-150 px-3 py-2.5 pl-3.5',
        !selected && SELECTION_ROW_INACTIVE,
      )}
      style={selected ? selectionRowStyle(c) : undefined}
    >
      {selected && <div aria-hidden className="absolute left-0 top-0 bottom-0 w-[3px]" style={selectionBarStyle(c)} />}
      <div className="text-body font-semibold text-primary truncate">{scopeLabel(scope)}</div>
      <div className="text-caption text-muted truncate font-mono">{scope.key}</div>
      <div className="flex items-center gap-2 mt-1.5 text-caption text-muted">
        <span className="shrink-0"><Plural value={scope.agentIds.length} one="# agent" other="# agents" /></span>
        <span aria-hidden>·</span>
        <span className="shrink-0"><Plural value={scope.traceCount} one="# trace" other="# traces" /></span>
        <span className="ml-auto shrink-0 font-mono">
          {scope.lastActivityAt ? fmtRelative(scope.lastActivityAt) : <Trans>no traces</Trans>}
        </span>
      </div>
    </RowButton>
  );
}
