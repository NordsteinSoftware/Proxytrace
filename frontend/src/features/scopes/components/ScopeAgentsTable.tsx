import { useNavigate } from 'react-router';
import { useLingui } from '@lingui/react/macro';
import type { ScopeAgentDto } from '../../../api/models';
import { agentColor } from '../../../lib/colors';
import { fmtRelative, fmtTokens } from '../../../lib/format';
import { Card } from '../../../components/ui/Card';
import { DataTable, type DataColumn } from '../../../components/ui/DataTable';

/** The agents that served the scope, each with its activity inside it; a row opens the agent. */
export function ScopeAgentsTable({ agents }: { agents: ScopeAgentDto[] }) {
  const { t } = useLingui();
  const navigate = useNavigate();

  const columns: DataColumn<ScopeAgentDto>[] = [
    {
      key: 'agent',
      label: t`Agent`,
      width: '2fr',
      render: a => (
        <span className="flex items-center gap-2 min-w-0">
          <span aria-hidden className="w-[6px] h-[6px] rounded-full shrink-0" style={{ background: agentColor(a.agentId) }} />
          <span className="text-body text-primary truncate">{a.agentName}</span>
        </span>
      ),
    },
    { key: 'traces', label: t`Traces`, width: '0.7fr', render: a => <span className="mono text-body-sm">{a.traceCount.toLocaleString()}</span> },
    { key: 'tokens', label: t`Tokens`, width: '0.7fr', render: a => <span className="mono text-body-sm">{fmtTokens(a.totalTokens)}</span> },
    { key: 'last', label: t`Last seen`, width: '0.9fr', render: a => <span className="text-body-sm text-muted">{fmtRelative(a.lastSeenAt)}</span> },
  ];

  return (
    <Card padding="none" data-testid="scope-agents">
      <div className="px-4 pt-3">
        <Card.Header title={t`Agents`} description={t`Membership follows the traces: an agent can serve several scopes.`} />
      </div>
      <div className="mt-3">
        <DataTable
          columns={columns}
          rows={agents}
          rowKey={a => a.agentId}
          onRowClick={a => navigate(`/agents?id=${a.agentId}`)}
          emptyMessage={t`No agent has sent traces in this scope within retention.`}
        />
      </div>
    </Card>
  );
}
