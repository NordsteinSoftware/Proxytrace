import { Trans, Plural, useLingui } from '@lingui/react/macro';
import { useNavigate } from 'react-router';
import type { AgentSuitePassRateDto } from '../../../api/models';
import { fmtRelative } from '../../../lib/format';
import { RowButton } from '../../../components/ui/RowButton';
import { Widget } from './Widget';

interface Props {
  suitePassRates: AgentSuitePassRateDto[];
  agentId: string;
  className?: string;
}

function color(pct: number): string {
  if (pct >= 80) return 'var(--success)';
  if (pct >= 50) return 'var(--accent-primary)';
  return 'var(--warn)';
}

export function SuitePassRatesWidget({ suitePassRates, agentId, className }: Props) {
  const { t } = useLingui();
  const navigate = useNavigate();
  if (suitePassRates.length === 0) {
    return (
      <Widget title={t`Suite Pass Rates`} className={className}>
        <div className="text-body text-muted italic"><Trans>No suite runs yet</Trans></div>
      </Widget>
    );
  }

  return (
    <Widget
      title={t`Suite Pass Rates`}
      right={<span className="text-body-sm text-muted"><Plural value={suitePassRates.length} one="# suite" other="# suites" /></span>}
      className={className}
    >
      <div className="flex flex-col gap-3.5">
        {suitePassRates.map(s => {
          const pct = s.testCases > 0 ? (s.passed / s.testCases) * 100 : 0;
          const clr = color(pct);
          return (
            <RowButton
              key={s.suiteId}
              data-testid={`suite-pass-rate-${s.suiteId}`}
              onClick={() => navigate(`/suites?agentId=${agentId}&suiteId=${s.suiteId}`)}
              title={t`Open ${s.suiteName}`}
              className="flex flex-col gap-1.5 rounded-md px-2 py-2 -mx-2 transition-colors duration-100 hover:bg-[var(--bg-wash-hover)]"
            >
              <div className="flex items-baseline justify-between gap-2 text-body-sm w-full">
                <span className="font-medium text-primary truncate">{s.suiteName}</span>
                <span className="shrink-0 flex items-baseline gap-1.5 font-mono">
                  <span className="text-muted">{s.passed}/{s.testCases}</span>
                  <span className="font-semibold" style={{ color: clr }}>{Math.round(pct)}%</span>
                </span>
              </div>
              <div className="h-[6px] bg-card-2 overflow-hidden w-full">
                <div className="h-full transition-[width] duration-300" style={{ width: `${pct}%`, background: clr }} />
              </div>
              <span className="text-caption text-muted">{fmtRelative(s.latestRunAt)}</span>
            </RowButton>
          );
        })}
      </div>
    </Widget>
  );
}
