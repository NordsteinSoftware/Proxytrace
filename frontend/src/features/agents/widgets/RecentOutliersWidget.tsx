import { Trans, useLingui } from '@lingui/react/macro';
import { useNavigate } from 'react-router';
import { fmtRelative } from '../../../lib/format';
import { tracePreview } from '../../../lib/trace';
import { OUTLIER_FLAG_LABEL, outlierFlagKeys } from '../../../lib/outliers';
import { RowButton } from '../../../components/ui/RowButton';
import { Skeleton } from '../../../components/ui/Skeleton';
import { Button } from '../../../components/ui/Button';
import { useAgentRecentOutliers } from '../hooks/useAgentRecentOutliers';
import { Widget } from './Widget';

interface Props {
  agentId: string;
  className?: string;
}

export function RecentOutliersWidget({ agentId, className }: Props) {
  const { t, i18n } = useLingui();
  const navigate = useNavigate();
  const { outliers, isLoading } = useAgentRecentOutliers(agentId);

  // Resolved here because the `outliers.map(o => …)` callback below shadows the i18n `t`.
  const tOpenTrace = t`Open trace`;

  return (
    <Widget
      title={t`Recent outliers`}
      right={
        outliers.length > 0 && (
          <Button variant="link" className="text-body-sm" onClick={() => navigate('/traces')}>
            <Trans>View all ›</Trans>
          </Button>
        )
      }
      className={className}
    >
      {isLoading && (
        <div className="flex flex-col gap-1.5" data-testid="agent-recent-outliers-loading">
          {Array.from({ length: 4 }, (_, i) => (
            <Skeleton key={i} height={28} className="rounded-md" />
          ))}
        </div>
      )}

      {!isLoading && outliers.length === 0 && (
        <p className="text-body-sm text-muted" data-testid="agent-recent-outliers-empty">
          <Trans>No outliers detected.</Trans>
        </p>
      )}

      {!isLoading && outliers.length > 0 && (
        <div className="flex flex-col gap-0.5" data-testid="agent-recent-outliers-list">
          {outliers.map(o => {
            const preview = tracePreview(o);
            const reasons = outlierFlagKeys(o.outlierFlags).map(key => i18n._(OUTLIER_FLAG_LABEL[key])).join(', ');
            return (
              <RowButton
                key={o.id}
                data-testid={`agent-recent-outlier-${o.id}`}
                onClick={() => navigate(`/traces?focus=${o.id}`)}
                title={preview ?? tOpenTrace}
                className="flex items-center gap-2.5 rounded-md px-2 py-1.5 -mx-2 transition-colors duration-100 hover:bg-[var(--bg-wash-hover)]"
              >
                <span className="w-1.5 h-1.5 rounded-full shrink-0 bg-warn" aria-hidden />
                <span className="text-body-sm text-secondary truncate">
                  {preview ?? <span className="text-muted italic"><Trans>No user message</Trans></span>}
                </span>
                <span className="ml-auto shrink-0 text-caption text-warn truncate max-w-[42%]" title={reasons}>{reasons}</span>
                <span className="shrink-0 text-caption text-muted w-14 text-right">{fmtRelative(o.createdAt)}</span>
              </RowButton>
            );
          })}
        </div>
      )}
    </Widget>
  );
}
