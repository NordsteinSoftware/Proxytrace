import { useMemo } from 'react';
import { Trans, useLingui } from '@lingui/react/macro';
import { Card } from '../../../components/ui/Card';
import { EmptyState } from '../../../components/ui/EmptyState';
import { Skeleton } from '../../../components/ui/Skeleton';
import { Donut, type DonutSegment } from '../../../components/charts';
import { EYEBROW_CLS } from '../../../components/ui/classes';
import { agentColor } from '../../../lib/colors';
import { fmtCost } from '../../../lib/format';
import type { AgentCostTotalDto } from '../../../api/costs';
import { topAgents } from '../costSeries';

/** Beyond this the donut stops being readable; the tail folds into one "other" slice. */
const TOP_N = 6;

interface AgentCostSectionProps {
  totals: readonly AgentCostTotalDto[];
  isLoading: boolean;
  isError: boolean;
}

/** Who spent the money: a share donut over the window plus the exact per-agent figures. */
export function AgentCostSection({ totals, isLoading, isError }: AgentCostSectionProps) {
  const { t } = useLingui();

  const { rows, otherEur } = useMemo(() => topAgents(totals, TOP_N), [totals]);
  const total = useMemo(() => totals.reduce((sum, r) => sum + r.costEur, 0), [totals]);

  const segments: DonutSegment[] = useMemo(() => {
    const base = rows.map(r => ({ label: r.agentName, value: r.costEur, color: agentColor(r.agentId) }));
    return otherEur > 0
      ? [...base, { label: t`Other`, value: otherEur, color: 'var(--text-muted)' }]
      : base;
  }, [rows, otherEur, t]);

  return (
    <Card padding="md" data-testid="cost-by-agent">
      <Card.Header title={t`Spend by agent`} />
      <Card.Body>
        {isLoading && <Skeleton height={180} />}
        {!isLoading && isError && (
          <p className="text-body-sm text-danger"><Trans>Could not load the agent breakdown.</Trans></p>
        )}
        {!isLoading && !isError && total === 0 && (
          <div data-testid="cost-by-agent-empty-state">
            <EmptyState title={t`No attributed spend yet`} />
          </div>
        )}
        {!isLoading && !isError && total > 0 && (
          <div className="flex flex-wrap items-center gap-6">
            <Donut segments={segments} size={132} thickness={16}>
              <span className="font-mono text-body-sm text-primary">{fmtCost(total)}</span>
            </Donut>
            <ul className="flex-1 min-w-[220px] flex flex-col gap-1" data-testid="cost-agent-list">
              <li className={`flex items-center justify-between gap-3 pb-1 ${EYEBROW_CLS}`}>
                <span><Trans>Agent</Trans></span>
                <span><Trans>Spend</Trans></span>
              </li>
              {rows.map(row => (
                <li
                  key={row.agentId}
                  className="flex items-center justify-between gap-3 border-b border-border-subtle py-1"
                  data-testid={`cost-agent-row-${row.agentId}`}
                >
                  <span className="flex items-center gap-2 min-w-0">
                    <span
                      className="w-2 h-2 rounded-full shrink-0"
                      style={{ backgroundColor: agentColor(row.agentId) }}
                    />
                    <span className="text-body text-primary truncate" title={row.agentName}>{row.agentName}</span>
                  </span>
                  <span className="font-mono text-body-sm text-secondary shrink-0">{fmtCost(row.costEur)}</span>
                </li>
              ))}
              {otherEur > 0 && (
                <li className="flex items-center justify-between gap-3 py-1" data-testid="cost-agent-row-other">
                  <span className="text-body text-muted"><Trans>Other agents</Trans></span>
                  <span className="font-mono text-body-sm text-muted">{fmtCost(otherEur)}</span>
                </li>
              )}
            </ul>
          </div>
        )}
      </Card.Body>
    </Card>
  );
}
