import { useLingui } from '@lingui/react/macro';
import type { ScopeDetailDto } from '../../../api/models';
import { fmtRelative, fmtTokens } from '../../../lib/format';
import { KpiCard } from '../../../components/ui/KpiCard';

/** The scope's within-retention activity: traces, tokens, member agents, last activity. */
export function ScopeKpis({ scope }: { scope: ScopeDetailDto }) {
  const { t } = useLingui();
  return (
    <div className="grid grid-cols-[repeat(auto-fit,minmax(160px,1fr))] gap-3" data-testid="scope-kpis">
      <KpiCard label={t`Traces`} value={scope.traceCount.toLocaleString()} sub={t`Within retention`} accent />
      <KpiCard label={t`Tokens`} value={fmtTokens(scope.totalTokens)} sub={t`Input + output`} />
      <KpiCard label={t`Agents`} value={String(scope.agents.length)} sub={t`Seen in this scope`} />
      <KpiCard
        label={t`Last activity`}
        value={scope.lastActivityAt ? fmtRelative(scope.lastActivityAt) : '—'}
        sub={scope.lastActivityAt ? undefined : t`No traces within retention`}
      />
    </div>
  );
}
