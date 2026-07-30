import { useMemo } from 'react';
import { Trans, useLingui } from '@lingui/react/macro';
import { Card } from '../../../components/ui/Card';
import { EmptyState } from '../../../components/ui/EmptyState';
import { Skeleton } from '../../../components/ui/Skeleton';
import { EYEBROW_CLS } from '../../../components/ui/classes';
import { agentColor } from '../../../lib/colors';
import { fmtCost } from '../../../lib/format';
import type { ApiKeyCostTotalDto } from '../../../api/costs';
import { topApiKeys } from '../costSeries';

/** Beyond this the list stops being scannable; the tail folds into one "other" row. */
const TOP_N = 6;

interface ApiKeyCostSectionProps {
  totals: readonly ApiKeyCostTotalDto[];
  isLoading: boolean;
  isError: boolean;
}

/**
 * Which credential spent the money. Answers "who do I cap?" before a key budget is set — and shows
 * the unattributed remainder explicitly, so the per-key figures visibly reconcile with the project
 * total instead of quietly falling short of it.
 */
export function ApiKeyCostSection({ totals, isLoading, isError }: ApiKeyCostSectionProps) {
  const { t } = useLingui();

  const { rows, otherEur, unattributedEur } = useMemo(() => topApiKeys(totals, TOP_N), [totals]);
  const total = useMemo(() => totals.reduce((sum, r) => sum + r.costEur, 0), [totals]);

  return (
    <Card padding="md" data-testid="cost-by-api-key">
      <Card.Header title={t`Spend by API key`} />
      <Card.Body>
        {isLoading && <Skeleton height={140} />}
        {!isLoading && isError && (
          <p className="text-body-sm text-danger"><Trans>Could not load the API key breakdown.</Trans></p>
        )}
        {!isLoading && !isError && total === 0 && (
          <div data-testid="cost-by-api-key-empty-state">
            <EmptyState title={t`No attributed spend yet`} />
          </div>
        )}
        {!isLoading && !isError && total > 0 && (
          <ul className="flex flex-col gap-1" data-testid="cost-api-key-list">
            <li className={`flex items-center justify-between gap-3 pb-1 ${EYEBROW_CLS}`}>
              <span><Trans>API Key</Trans></span>
              <span><Trans>Spend</Trans></span>
            </li>
            {rows.map(row => (
              <li
                key={row.apiKeyId}
                className="flex items-center justify-between gap-3 border-b border-border-subtle py-1"
                data-testid={`cost-api-key-row-${row.apiKeyId}`}
              >
                <span className="flex items-center gap-2 min-w-0">
                  <span
                    className="w-2 h-2 rounded-full shrink-0"
                    style={{ backgroundColor: agentColor(row.apiKeyId ?? '') }}
                  />
                  <span className="text-body text-primary truncate" title={row.apiKeyName ?? undefined}>
                    {row.apiKeyName}
                  </span>
                  {row.keyPrefix && (
                    <span className="font-mono text-body-sm text-muted shrink-0">{row.keyPrefix}</span>
                  )}
                </span>
                <span className="font-mono text-body-sm text-secondary shrink-0">{fmtCost(row.costEur)}</span>
              </li>
            ))}
            {otherEur > 0 && (
              <li className="flex items-center justify-between gap-3 py-1" data-testid="cost-api-key-row-other">
                <span className="text-body text-muted"><Trans>Other keys</Trans></span>
                <span className="font-mono text-body-sm text-muted">{fmtCost(otherEur)}</span>
              </li>
            )}
            {unattributedEur > 0 && (
              <li
                className="flex items-center justify-between gap-3 py-1"
                data-testid="cost-api-key-row-unattributed"
              >
                <span className="text-body text-muted">
                  <Trans>Unattributed</Trans>
                </span>
                <span className="font-mono text-body-sm text-muted">{fmtCost(unattributedEur)}</span>
              </li>
            )}
          </ul>
        )}
        {!isLoading && !isError && unattributedEur > 0 && (
          <p className="text-body-sm text-muted pt-2">
            <Trans>
              Unattributed spend came through the provider's own key, or was recorded before per-key
              tracking existed. A key budget cannot cap it — the project budget is what holds it.
            </Trans>
          </p>
        )}
      </Card.Body>
    </Card>
  );
}
