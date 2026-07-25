// Live trace stream section — most recent agent calls, grouped by conversation.

import { useMemo } from 'react';
import { Trans, useLingui } from '@lingui/react/macro';
import { useNavigate } from 'react-router-dom';
import { Button } from '../../../components/ui/Button';
import { EmptyState } from '../../../components/ui/EmptyState';
import { Skeleton } from '../../../components/ui/Skeleton';
import type { AgentCallListItemDto } from '../../../api/models';
import { buildRows } from '../../../lib/trace';
import { useNowTick } from '../../../hooks/useNowTick';
import { LiveStreamRow, LIVE_STREAM_GRID, LIVE_STREAM_GRID_WIDE, LIVE_STREAM_GRID_NARROW, NARROW_HIDDEN } from './LiveStreamRow';
import { cn } from '../../../lib/cn';
import { COL_HEADER_CLS } from '../dashboardMeta';

interface LiveTraceStreamProps {
  traces: AgentCallListItemDto[];
  isLoading: boolean;
  freshIds: Set<string>;
}

export function LiveTraceStream({ traces, isLoading, freshIds }: LiveTraceStreamProps) {
  const { t } = useLingui();
  const navigate = useNavigate();
  const rows = useMemo(() => buildRows(traces), [traces]);
  const now = useNowTick(5000);

  return (
    <section
      data-testid="live-trace-stream"
      className="rounded-lg bg-card px-3.5 pt-2.5 pb-1.5 flex flex-col shadow-[var(--shadow-card)] @container"
      style={{ '--live-grid': LIVE_STREAM_GRID_WIDE, '--live-grid-narrow': LIVE_STREAM_GRID_NARROW } as React.CSSProperties}
    >
      <header className="flex items-end justify-between mb-3">
        <div>
          <span className="text-caption text-accent-hover font-mono tracking-[0.18em] uppercase font-bold flex items-center gap-1.5">
            <span className="size-1.5 rounded-full bg-success pulse-dot" />
            <Trans>Live feed</Trans>
          </span>
          <p className="text-body-sm text-muted mt-0.5 font-mono">
            <Trans>auto-refresh · {traces.length} most recent</Trans>
          </p>
        </div>
        <Button variant="link" className="text-body-sm" onClick={() => navigate('/traces')}>
          <Trans>View all →</Trans>
        </Button>
      </header>

      {/* The header rule and every row run full-bleed: they cancel the section's `px-3.5` with
          `-mx-3.5` and re-apply the inset as their own `px-5` (14px card padding + the rows' own
          6px). Without that the row hover wash and the divider stopped 14px short of the card
          edge, so a hovered row read as a floating strip rather than a filled row. */}
      <div className={cn(LIVE_STREAM_GRID, COL_HEADER_CLS, '-mx-3.5 px-5 pb-2.5 border-b border-border-subtle')}>
        <span /><span><Trans>Message</Trans></span><span className={cn('text-center', NARROW_HIDDEN)}><Trans>Turns</Trans></span><span className={cn('text-center', NARROW_HIDDEN)}><Trans>Model</Trans></span><span className="text-center"><Trans>Status</Trans></span><span className="text-right"><Trans>Tokens</Trans></span><span className="text-right"><Trans>Latency</Trans></span><span className="text-right"><Trans>Age</Trans></span>
      </div>

      {isLoading ? (
        <div className="py-3 flex flex-col gap-1.5">
          {Array.from({ length: 12 }, (_, i) => <Skeleton key={i} height={26} className="rounded-sm" />)}
        </div>
      ) : rows.length === 0 ? (
        <div className="py-10">
          <EmptyState
            title={t`No traces yet`}
            description={t`Route your agent through the Proxytrace proxy to start capturing traces.`}
          />
        </div>
      ) : (
        <div className="-mx-3.5">
          {rows.map((row, i) => (
            <LiveStreamRow
              key={row.type === 'flat' ? row.trace.id : row.conversationId}
              row={row}
              freshIds={freshIds}
              isLast={i === rows.length - 1}
              now={now}
              onSelect={id => navigate(`/traces?focus=${id}`)}
            />
          ))}
        </div>
      )}
    </section>
  );
}
