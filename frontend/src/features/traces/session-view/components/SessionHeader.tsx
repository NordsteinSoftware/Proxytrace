import { Trans } from '@lingui/react/macro';
import { Card } from '../../../../components/ui/Card';
import { ActivityIcon, ClockIcon, CoinsIcon, HashIcon, KeyIcon } from '../../../../components/icons';
import { fmtDateTimeShort, fmtRelative } from '../../../../lib/format';
import type { SessionDto } from '../../../../api/models';

interface Props {
  session: SessionDto;
  live: boolean;
}

function StatItem({ icon, label, value, testId }: { icon: React.ReactNode; label: React.ReactNode; value: React.ReactNode; testId: string }) {
  return (
    <div className="flex items-center gap-2">
      <span aria-hidden className="text-muted shrink-0">{icon}</span>
      <div className="min-w-0">
        <div className="text-caption text-muted">{label}</div>
        <div className="text-body-sm font-semibold text-primary truncate" data-testid={testId}>{value}</div>
      </div>
    </div>
  );
}

/** Session identity + counters. Presentational — the page owns data and the live computation. */
export function SessionHeader({ session, live }: Props) {
  return (
    <Card padding="md" data-testid="session-header">
      <div className="flex items-start gap-3.5 flex-wrap">
        <span aria-hidden className="w-9 h-9 rounded-md bg-accent-subtle text-accent-text flex items-center justify-center shrink-0">
          <KeyIcon size={18} />
        </span>
        <div className="flex-1 min-w-0">
          <div className="flex items-center gap-2.5 flex-wrap">
            <h1 className="text-h1 font-semibold font-mono leading-tight m-0 truncate" data-testid="session-external-key">
              {session.externalKey}
            </h1>
            {live && (
              <span
                data-testid="session-live-indicator"
                className="inline-flex items-center gap-1.5 px-2.5 py-0.5 rounded-full bg-success-subtle text-success text-caption font-semibold"
              >
                <span className="pulse-dot w-[5px] h-[5px] rounded-full bg-success motion-reduce:animate-none" />
                <Trans>Live</Trans>
              </span>
            )}
          </div>
          <div className="mt-3 grid gap-3.5 grid-cols-[repeat(auto-fit,minmax(150px,1fr))]">
            <StatItem
              icon={<HashIcon size={15} />}
              label={<Trans>Traces</Trans>}
              value={session.traceCount.toLocaleString()}
              testId="session-trace-count"
            />
            <StatItem
              icon={<CoinsIcon size={15} />}
              label={<Trans>Total tokens</Trans>}
              value={session.totalTokens.toLocaleString()}
              testId="session-total-tokens"
            />
            <StatItem
              icon={<ClockIcon size={15} />}
              label={<Trans>First seen</Trans>}
              value={fmtDateTimeShort(session.createdAt)}
              testId="session-first-seen"
            />
            <StatItem
              icon={<ActivityIcon size={15} />}
              label={<Trans>Last activity</Trans>}
              value={fmtRelative(session.lastActivityAt)}
              testId="session-last-activity"
            />
          </div>
        </div>
      </div>
    </Card>
  );
}
