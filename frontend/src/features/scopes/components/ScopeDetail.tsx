import { useState } from 'react';
import { useNavigate } from 'react-router';
import { Trans, useLingui } from '@lingui/react/macro';
import type { ScopeDetailDto } from '../../../api/models';
import { scopeColor } from '../../../lib/colors';
import { scopeLabel } from '../../../lib/scopes';
import { Button } from '../../../components/ui/Button';
import { Card } from '../../../components/ui/Card';
import { CopyButton } from '../../../components/ui/CopyButton';
import { ConfirmDialog } from '../../../components/overlays/ConfirmDialog';
import { ScopeKpis } from './ScopeKpis';
import { ScopeAgentsTable } from './ScopeAgentsTable';
import { ScopeBaseUrl } from './ScopeBaseUrl';
import { ScopeEditDialog } from './ScopeEditDialog';
import { useDeleteScope, useUpdateScope } from '../hooks/useScopes';

/** One scope: identity + actions, activity KPIs, member agents, and how to send traffic to it. */
export function ScopeDetail({ scope }: { scope: ScopeDetailDto }) {
  const { t } = useLingui();
  const navigate = useNavigate();
  const [editing, setEditing] = useState(false);
  const [deleting, setDeleting] = useState(false);
  const update = useUpdateScope(scope.projectId);
  const remove = useDeleteScope(scope.projectId);
  const name = scopeLabel(scope);

  return (
    <div className="flex flex-col gap-3.5 min-w-0" data-testid="scope-detail">
      <Card accentBar={scopeColor(scope.id)}>
        <Card.Header
          title={<span data-testid="scope-detail-name">{name}</span>}
          description={scope.description ?? undefined}
          action={
            <>
              <Button
                variant="dangerOutline"
                size="sm"
                data-testid="scope-delete-btn"
                onClick={() => setDeleting(true)}
              >
                <Trans>Delete</Trans>
              </Button>
              <Button
                variant="secondary"
                size="sm"
                data-write
                data-testid="scope-edit-btn"
                onClick={() => setEditing(true)}
              >
                <Trans>Edit</Trans>
              </Button>
              <Button
                variant="primary"
                size="sm"
                data-testid="scope-view-traces-btn"
                onClick={() => navigate(`/traces?scope=${scope.id}`)}
              >
                <Trans>View traces</Trans>
              </Button>
            </>
          }
        />
        <div className="flex items-center gap-1.5 mt-2">
          <span className="mono text-body-sm text-secondary" data-testid="scope-detail-key">{scope.key}</span>
          <CopyButton text={scope.key} label={t`Copy scope key`} />
        </div>
      </Card>

      <ScopeKpis scope={scope} />
      <ScopeAgentsTable agents={scope.agents} />

      <Card>
        <Card.Header title={t`Send traffic to this scope`} />
        <Card.Body>
          <ScopeBaseUrl scopeKey={scope.key} />
        </Card.Body>
      </Card>

      {editing && (
        <ScopeEditDialog
          scope={scope}
          loading={update.isPending}
          onCancel={() => setEditing(false)}
          onSubmit={body => update.mutate({ id: scope.id, body }, { onSuccess: () => setEditing(false) })}
        />
      )}

      {deleting && (
        <ConfirmDialog
          title={t`Delete the scope "${name}"?`}
          message={t`This removes the scope with its display name and description, and frees its place under the project's scope limit. Its traces are kept: if a client sends "${scope.key}" again, the scope comes back with them.`}
          onConfirm={() => remove.mutate(scope.id, { onSuccess: () => setDeleting(false) })}
          onCancel={() => setDeleting(false)}
          loading={remove.isPending}
        />
      )}
    </div>
  );
}
