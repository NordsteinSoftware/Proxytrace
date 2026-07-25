import { Link } from 'react-router';
import { Trans } from '@lingui/react/macro';
import type { SearchHit, SearchKind } from '../../api/search';
import { RowButton } from '../ui/RowButton';
import { searchHitToHref } from '../../lib/search-routes';
import { KIND_META } from './searchMeta';
import {
  UsersIcon, CheckboxIcon, ActivityIcon, ScaleIcon,
} from '../icons';
import { cn } from '../../lib/cn';
import { kbdCls } from '../ui/classes';

// Icon lookup (presentation-layer concern, kept here beside usage)
const KIND_ICON: Record<SearchKind, (s: number) => React.ReactNode> = {
  agent:     s => <UsersIcon size={s} />,
  testSuite: s => <CheckboxIcon size={s} />,
  agentCall: s => <ActivityIcon size={s} />,
  evaluator: s => <ScaleIcon size={s} />,
  testCase:  s => <CheckboxIcon size={s} />,
};

interface SearchResultListProps {
  groupOrder: { kind: SearchKind; label: string }[];
  grouped: Map<SearchKind, SearchHit[]>;
  groupOffsets: Map<SearchKind, number>;
  activeIndex: number;
  isRecentMode: boolean;
  onSelect?: (hit: SearchHit) => void;
  onHover: (index: number) => void;
  onLinkClick: () => void;
  onCommit: (hit: SearchHit) => void;
}

export function SearchResultList({
  groupOrder,
  grouped,
  groupOffsets,
  activeIndex,
  isRecentMode,
  onSelect,
  onHover,
  onLinkClick,
  onCommit,
}: SearchResultListProps) {
  return (
    <>
      {isRecentMode && (
        <div className="px-3 pt-1 pb-1.5 text-caption uppercase tracking-wider text-secondary font-semibold">
          <Trans>Recent</Trans>
        </div>
      )}
      {groupOrder.map(g => {
        const groupHitsForKind = grouped.get(g.kind) ?? [];
        if (groupHitsForKind.length === 0) return null;
        const meta = KIND_META[g.kind];
        const icon = KIND_ICON[g.kind];
        const startIndex = groupOffsets.get(g.kind) ?? 0;
        return (
          <div key={g.kind} className="mb-2">
            {!isRecentMode && (
              <div className="px-3 py-1 text-caption uppercase tracking-wider text-secondary flex items-center gap-1.5">
                <span style={{ color: meta.accent }}>{icon(11)}</span>
                {g.label}
                <span className="text-muted">· {groupHitsForKind.length}</span>
              </div>
            )}
            <div className="flex flex-col px-1.5">
              {groupHitsForKind.map((hit, i) => {
                const globalIndex = startIndex + i;
                const active = globalIndex === activeIndex;
                const itemCls = cn(
                  'flex items-center gap-2 px-2.5 py-2 rounded-md cursor-pointer text-left',
                  active ? 'bg-card-2' : 'hover:bg-[var(--bg-wash-hover)]',
                );
                const itemContent = (
                  <>
                    <span
                      className="size-[22px] rounded-md flex items-center justify-center shrink-0"
                      style={{ background: `${meta.accent}1f`, color: meta.accent }}
                    >
                      {icon(12)}
                    </span>
                    <span className="text-title text-primary truncate flex-1 min-w-0">{hit.title}</span>
                    {active && (
                      <kbd className={kbdCls}>↵</kbd>
                    )}
                  </>
                );
                return onSelect ? (
                  <RowButton
                    key={`${hit.kind}-${hit.entityId}`}
                    data-testid={`search-result-${hit.entityId}`}
                    onMouseEnter={() => onHover(globalIndex)}
                    onClick={() => onCommit(hit)}
                    className={itemCls}
                  >
                    {itemContent}
                  </RowButton>
                ) : (
                  <Link
                    key={`${hit.kind}-${hit.entityId}`}
                    to={searchHitToHref(hit)}
                    data-testid={`search-result-${hit.entityId}`}
                    onMouseEnter={() => onHover(globalIndex)}
                    onClick={onLinkClick}
                    className={itemCls}
                  >
                    {itemContent}
                  </Link>
                );
              })}
            </div>
          </div>
        );
      })}
    </>
  );
}
