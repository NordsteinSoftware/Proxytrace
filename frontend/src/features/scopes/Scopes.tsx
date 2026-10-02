import { useState } from 'react';
import { Trans, useLingui } from '@lingui/react/macro';
import { cn } from '../../lib/cn';
import { useSelectedId } from '../../hooks/useSelectedId';
import { useIsMobile } from '../../hooks/useMediaQuery';
import { useProjectScopes } from '../../hooks/useProjectScopes';
import { LIST_RAIL_COLS } from '../../components/ui/ListRail';
import { Button } from '../../components/ui/Button';
import { Card } from '../../components/ui/Card';
import { EmptyState } from '../../components/ui/EmptyState';
import { Skeleton } from '../../components/ui/Skeleton';
import { Modal } from '../../components/overlays/Modal';
import { ChevronRightIcon } from '../../components/icons';
import { ScopeList } from './components/ScopeList';
import { ScopeDetail } from './components/ScopeDetail';
import { ScopeUrlBuilder } from './components/ScopeUrlBuilder';
import { useScopeDetail } from './hooks/useScopes';

/** Scopes page: the project's use-case groups of agents (master/detail) and how to send traffic to one. */
export default function Scopes() {
  const { t } = useLingui();
  const [selectedId, setSelectedId] = useSelectedId();
  const [builderOpen, setBuilderOpen] = useState(false);
  const { scopes, isLoading } = useProjectScopes();
  const isMobile = useIsMobile();

  const explicitSelectedId = selectedId && scopes.some(s => s.id === selectedId) ? selectedId : null;
  const effectiveSelectedId = explicitSelectedId ?? (isMobile ? null : scopes[0]?.id ?? null);
  const { data: detail } = useScopeDetail(effectiveSelectedId);
  const isEmpty = !isLoading && scopes.length === 0;

  if (isEmpty) {
    return (
      <div className="w-full max-w-3xl flex flex-col gap-3" data-testid="scopes-empty-state">
        <EmptyState title={t`No scopes yet`} description={t`Scopes group the agents of one use case inside this project.`} />
        <Card><ScopeUrlBuilder /></Card>
      </div>
    );
  }

  return (
    <div className="w-full min-w-0 flex flex-col gap-3 h-full overflow-hidden">
      <div className={cn('fade-up flex-1 min-h-0', isMobile ? 'flex flex-col' : `grid gap-4 ${LIST_RAIL_COLS}`)}>
        {(!isMobile || !effectiveSelectedId) && (
          <ScopeList
            scopes={scopes}
            selectedId={effectiveSelectedId}
            onSelect={id => setSelectedId(id)}
            isLoading={isLoading}
            onCreate={() => setBuilderOpen(true)}
          />
        )}

        {(!isMobile || effectiveSelectedId) && (
          <main className="min-w-0 min-h-0 overflow-y-auto pr-1 pb-6">
            {isMobile && (
              <Button
                variant="ghost"
                size="sm"
                className="mb-2"
                data-testid="scopes-back-to-list"
                onClick={() => setSelectedId(null)}
                leftIcon={<ChevronRightIcon size={14} className="rotate-180" />}
              >
                <Trans>All scopes</Trans>
              </Button>
            )}
            {detail && detail.id === effectiveSelectedId ? (
              <ScopeDetail key={detail.id} scope={detail} />
            ) : effectiveSelectedId ? (
              <div role="status" aria-busy="true" className="flex flex-col gap-3.5">
                <span className="sr-only"><Trans>Loading scope…</Trans></span>
                <Skeleton height={96} className="rounded-lg" />
                <Skeleton height={104} className="rounded-lg" />
                <Skeleton height={200} className="rounded-lg" />
              </div>
            ) : null}
          </main>
        )}
      </div>

      {builderOpen && (
        <Modal title={t`New scope`} onClose={() => setBuilderOpen(false)} size="md">
          <ScopeUrlBuilder />
        </Modal>
      )}
    </div>
  );
}
