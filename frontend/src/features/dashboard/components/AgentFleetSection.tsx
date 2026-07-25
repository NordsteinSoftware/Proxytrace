// Agent fleet roster — per-agent identity, activity sparkline, token share, and load
// in one section. Replaces the old donut + agents-grid pair; the top pulse band's EKG
// decomposed into one heartbeat per agent.

import { Trans, Plural, useLingui } from '@lingui/react/macro';
import { useNavigate } from 'react-router-dom';
import { Button } from '../../../components/ui/Button';
import { RowButton } from '../../../components/ui/RowButton';
import { EmptyState } from '../../../components/ui/EmptyState';
import { Skeleton } from '../../../components/ui/Skeleton';
import { SparklesIcon } from '../../../components/icons';
import { FOCUS_RING } from '../../../lib/constants';
import { EYEBROW_CLS, COL_HEADER_CLS, type AgentFleetEntry } from '../dashboardMeta';
import { useNowTick } from '../../../hooks/useNowTick';
import { AgentFleetRow, FLEET_GRID, FLEET_GRID_WIDE, FLEET_GRID_MID, FLEET_GRID_NARROW, FLEET_XL_HIDDEN, FLEET_2XL_HIDDEN } from './AgentFleetRow';
import { cn } from '../../../lib/cn';

const MAX_ROWS = 8;

interface AgentFleetSectionProps {
  fleet: AgentFleetEntry[];
  isLoading: boolean;
  /** Count of Draft (pending-review) optimization proposals; 0 hides the chip. */
  proposalCount: number;
}

export function AgentFleetSection({ fleet, isLoading, proposalCount }: AgentFleetSectionProps) {
  const { t } = useLingui();
  const navigate = useNavigate();
  const now = useNowTick(30_000);
  const visible = fleet.slice(0, MAX_ROWS);
  const restCount = fleet.length - visible.length;

  return (
    <section
      data-testid="agent-fleet"
      className="relative overflow-hidden rounded-lg bg-card px-3.5 pt-2.5 pb-1.5 flex flex-col shadow-[var(--shadow-card)] @container"
      style={{ '--fleet-grid': FLEET_GRID_WIDE, '--fleet-grid-mid': FLEET_GRID_MID, '--fleet-grid-narrow': FLEET_GRID_NARROW } as React.CSSProperties}
    >
      <header className="relative flex items-end justify-between mb-3">
        <div>
          <span className={EYEBROW_CLS}>
            <Trans>Agent fleet</Trans>
          </span>
          <p className="text-body-sm text-muted mt-0.5 font-mono">
            <Trans>
              <span data-testid="dashboard-agents-count" className="text-secondary font-semibold">{fleet.length}</span> detected
              · ranked by activity · tap to inspect
            </Trans>
          </p>
        </div>
        <div className="flex items-center gap-2">
          {proposalCount > 0 && (
            <RowButton
              data-testid="dashboard-proposals-chip"
              onClick={() => navigate('/proposals')}
              className={cn(
                'w-auto px-2.5 py-1.5 text-body-sm font-semibold text-accent-hover inline-flex items-center gap-1.5 bg-accent-subtle transition-colors hover:text-accent-text',
                FOCUS_RING,
              )}
            >
              <SparklesIcon size={11} />
              <Plural value={proposalCount} one="# proposal" other="# proposals" />
            </RowButton>
          )}
          <Button variant="secondary" size="sm" onClick={() => navigate('/agents')}>
            <Trans>Manage</Trans>
          </Button>
        </div>
      </header>

      {isLoading ? (
        <div className="pb-2 flex flex-col gap-1.5">
          {Array.from({ length: 4 }, (_, i) => <Skeleton key={i} height={44} className="rounded-sm" />)}
        </div>
      ) : visible.length === 0 ? (
        <div className="py-8" data-testid="agent-fleet-empty-state">
          <EmptyState
            title={t`No agents yet`}
            description={t`Agents are detected automatically when you route traffic through the Proxytrace proxy.`}
          />
        </div>
      ) : (
        <div className="relative">
          <div className={cn(FLEET_GRID, COL_HEADER_CLS, 'px-1.5 pb-2 border-b border-border-subtle')}>
            <span />
            <span><Trans>Agent</Trans></span>
            <span className={FLEET_XL_HIDDEN}><Trans>Activity</Trans></span>
            <span className="text-right"><Trans>Tokens</Trans></span>
            <span className="text-right"><Trans>Traces</Trans></span>
            <span className={cn('text-right', FLEET_2XL_HIDDEN)}><Trans>Last active</Trans></span>
          </div>
          {visible.map((entry, i) => (
            <AgentFleetRow
              key={entry.id}
              entry={entry}
              isLast={i === visible.length - 1 && restCount === 0}
              now={now}
              onSelect={id => navigate(`/agents?id=${id}`)}
            />
          ))}
          {restCount > 0 && (
            <RowButton
              onClick={() => navigate('/agents')}
              className={cn('py-2 px-1.5 text-body-sm text-muted font-mono transition-colors hover:text-secondary', FOCUS_RING)}
            >
              <Plural value={restCount} one="+# more agent →" other="+# more agents →" />
            </RowButton>
          )}
        </div>
      )}
    </section>
  );
}
