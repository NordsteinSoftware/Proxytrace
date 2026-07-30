import { useState, type ReactNode } from 'react';
import { Trans, useLingui } from '@lingui/react/macro';
import type { ApiKeyDto, ApiKeyScope, ProjectDto } from '../../../api/models';
import { Button, IconButton } from '../../../components/ui/Button';
import { Checkbox } from '../../../components/ui/Checkbox';
import { CopyButton } from '../../../components/ui/CopyButton';
import { ConfirmDialog } from '../../../components/overlays/ConfirmDialog';
import { DataTable, type DataColumn } from '../../../components/ui/DataTable';
import { EmptyState } from '../../../components/ui/EmptyState';
import { FormField } from '../../../components/ui/FormField';
import { Input } from '../../../components/ui/Input';
import { Select } from '../../../components/ui/Select';
import { CopyIcon, PlusIcon, TrashIcon, XIcon } from '../../../components/icons';
import { KeyCapabilities } from './KeyCapabilities';
import { ingestionUrl } from '../../../lib/ingestion';
import { useIngestionBase } from '../../../hooks/useIngestionBase';
import useToast from '../../../hooks/useToast';
import { apiBaseUrl, hasApi, hasIngestion, hasMcp, mcpEndpointUrl } from '../keyScopes';
import { useCreateKey, useDeleteKey } from '../hooks/useProviderMutations';
import { useUsersList } from '../hooks/useProviderQueries';

interface KeysSectionProps {
  providerId: string;
  keys: ApiKeyDto[];
  projects: ProjectDto[];
  defaultProjectId: string;
}

export function KeysSection({ providerId, keys, projects, defaultProjectId }: KeysSectionProps) {
  const { t } = useLingui();
  const { show: toast } = useToast();
  const proxyBase = useIngestionBase();
  const [showNew, setShowNew] = useState(false);
  const [newKey, setNewKey] = useState<{ name: string; projectId: string; scopes: ApiKeyScope[]; userId: string }>({
    name: '',
    projectId: '',
    scopes: ['Ingestion'],
    userId: '',
  });
  const [toDelete, setToDelete] = useState<ApiKeyDto | null>(null);
  const [created, setCreated] = useState<ApiKeyDto | null>(null);

  const createKey = useCreateKey(providerId);
  const deleteKey = useDeleteKey(providerId);
  const { data: users } = useUsersList();
  const userOptions = users ?? [];

  const mcpEndpoint = mcpEndpointUrl(window.location.origin);
  const apiBase = apiBaseUrl(window.location.origin);
  const anyMcp = keys.some(k => hasMcp(k.scopes));

  const scopeDescriptions: Record<ApiKeyScope, ReactNode> = {
    Ingestion: <Trans>Ingestion proxy — authenticate clients at the Proxytrace proxy</Trans>,
    McpRead: <Trans>MCP read — let external agents read this project over the MCP server</Trans>,
    McpWrite: <Trans>MCP write — let agents curate suites, start runs and change proposals</Trans>,
    ApiRead: <Trans>REST API read — let a service read this project over the REST API</Trans>,
    ApiWrite: <Trans>REST API write — let a service create and change data over the REST API</Trans>,
    Passthrough: <Trans>Upstream pass-through — forward non-inference paths (such as /health) to the provider using its own credential. Not traced, and not checked by detectors.</Trans>,
  };

  const toggleScope = (scope: ApiKeyScope, on: boolean) =>
    setNewKey(k => ({
      ...k,
      scopes: on ? [...new Set([...k.scopes, scope])] : k.scopes.filter(s => s !== scope),
    }));

  const columns: DataColumn<ApiKeyDto>[] = [
    { key: 'name', label: t`Name`, width: '1.4fr', render: k => <span data-testid={`key-row-${k.id}`} className="block truncate text-title font-semibold text-primary">{k.name}</span> },
    { key: 'project', label: t`Project`, width: '1fr', render: k => <span className="block truncate text-body text-secondary">{k.projectName}</span> },
    { key: 'owner', label: t`Owner`, width: '1.5fr', render: k => <span className="block truncate text-body text-secondary" data-testid={`key-owner-${k.id}`}>{k.ownerEmail}</span> },
    { key: 'capabilities', label: t`Capabilities`, width: '1.1fr', render: k => <KeyCapabilities scopes={k.scopes} /> },
    {
      key: 'key', label: t`Key`, width: '1.3fr',
      render: k => (
        <code
          data-testid={`key-prefix-${k.id}`}
          title={t`Shown in full only once, when created`}
          className="font-mono text-body text-muted overflow-hidden text-ellipsis whitespace-nowrap block"
        >
          {k.keyPrefix}…
        </code>
      ),
    },
    {
      key: 'ingestion', label: t`Ingestion URL`, width: '2fr',
      render: k => {
        if (!hasIngestion(k.scopes)) return null;
        const url = ingestionUrl(k.projectName, proxyBase);
        return (
          <div className="flex items-center gap-1.5 min-w-0">
            <code className="font-mono text-body-sm text-secondary overflow-hidden text-ellipsis whitespace-nowrap flex-1">{url}</code>
            <CopyButton text={url} label={t`Copy ingestion URL`} />
          </div>
        );
      },
    },
    { key: 'delete', label: '', width: '0.5fr', render: k => <div className="flex justify-end"><IconButton data-testid={`key-delete-btn-${k.id}`} aria-label={t`Delete key`} danger onClick={() => setToDelete(k)}><TrashIcon size={13} /></IconButton></div> },
  ];

  return (
    <>
      <div className="flex items-center justify-between">
        <div>
          <div className="text-h2 font-semibold text-primary mb-0.5"><Trans>Proxytrace API keys</Trans></div>
          <div className="text-body-sm text-muted"><Trans>Keys that authenticate clients at the proxy and the MCP server.</Trans></div>
        </div>
        <Button
          data-testid="key-create-btn"
          variant="secondary"
          size="sm"
          leftIcon={<PlusIcon size={13} />}
          onClick={() => { setShowNew(true); setNewKey({ name: '', projectId: defaultProjectId, scopes: ['Ingestion'], userId: '' }); }}
        >
          <Trans>Generate key</Trans>
        </Button>
      </div>

      {anyMcp && (
        <div className="flex flex-wrap items-center gap-x-3 gap-y-1 rounded-lg border border-hairline bg-card-2 px-3 py-2">
          <span className="text-body-sm font-semibold text-secondary"><Trans>MCP endpoint</Trans></span>
          <code data-testid="mcp-endpoint" className="font-mono text-body-sm text-primary">{mcpEndpoint}</code>
          <CopyButton text={mcpEndpoint} label={t`Copy MCP endpoint`} data-testid="mcp-endpoint-copy" />
          <span className="ml-auto text-body-sm text-muted"><Trans>Send an MCP-enabled key as the bearer token.</Trans></span>
        </div>
      )}

      {created?.plaintextKey && (
        <div className="px-4 py-3 rounded-lg flex items-center gap-3 bg-success-subtle border border-[color-mix(in_srgb,var(--success)_28%,transparent)]">
          <div className="flex-1 min-w-0">
            <div className="text-body font-semibold mb-1 text-success"><Trans>Key "{created.name}" created — copy it now, it won't be shown again</Trans></div>
            <code data-testid="key-value-reveal" className="font-mono text-body text-primary break-all">{created.plaintextKey}</code>
            {hasIngestion(created.scopes) && (
              <div className="flex items-baseline gap-1.5 mt-2 min-w-0">
                <span className="text-body-sm text-muted whitespace-nowrap"><Trans>Ingestion URL</Trans></span>
                <code className="font-mono text-body-sm text-secondary break-all">{ingestionUrl(created.projectName, proxyBase)}</code>
              </div>
            )}
            {hasMcp(created.scopes) && (
              <div className="flex items-baseline gap-1.5 mt-2 min-w-0">
                <span className="text-body-sm text-muted whitespace-nowrap"><Trans>MCP endpoint</Trans></span>
                <code className="font-mono text-body-sm text-secondary break-all">{mcpEndpoint}</code>
              </div>
            )}
            {hasApi(created.scopes) && (
              <div className="flex items-baseline gap-1.5 mt-2 min-w-0">
                <span className="text-body-sm text-muted whitespace-nowrap"><Trans>REST API base</Trans></span>
                <code className="font-mono text-body-sm text-secondary break-all">{apiBase}</code>
              </div>
            )}
          </div>
          {/* eslint-disable-next-line lingui/no-unlocalized-strings -- toast tone token, not UI copy */}
          <Button variant="success" size="sm" leftIcon={<CopyIcon size={12} />} onClick={() => { navigator.clipboard.writeText(created.plaintextKey ?? ''); toast(t`API key copied`, 'success'); }}><Trans>Copy</Trans></Button>
          <IconButton aria-label={t`Dismiss`} onClick={() => setCreated(null)}><XIcon size={14} /></IconButton>
        </div>
      )}

      {showNew && (
        <div className="p-4 bg-card-2 rounded-lg border border-hairline flex flex-col gap-3">
          <div className="text-title font-semibold text-primary"><Trans>Generate new key</Trans></div>
          <div className="grid grid-cols-3 gap-2.5">
            <FormField label={t`Key name`}>
              <Input data-testid="key-name-input" value={newKey.name} onChange={e => setNewKey(k => ({ ...k, name: e.target.value }))} placeholder={t`e.g. production-agent`} />
            </FormField>
            <FormField label={t`Project`}>
              <Select value={newKey.projectId} onValueChange={v => setNewKey(k => ({ ...k, projectId: v }))}>
                {projects.map(p => <option key={p.id} value={p.id}>{p.name}</option>)}
              </Select>
            </FormField>
            <FormField label={t`Owner`}>
              <Select value={newKey.userId} onValueChange={v => setNewKey(k => ({ ...k, userId: v }))}>
                <option value="">{t`Yourself (creator)`}</option>
                {userOptions.map(u => <option key={u.id} value={u.id}>{u.email}</option>)}
              </Select>
            </FormField>
          </div>
          <FormField label={t`Capabilities`}>
            <div className="flex flex-col gap-1.5" data-testid="key-scopes-select">
              {(Object.keys(scopeDescriptions) as ApiKeyScope[]).map(scope => (
                <Checkbox
                  key={scope}
                  data-testid={`key-scope-${scope}`}
                  label={scopeDescriptions[scope]}
                  checked={newKey.scopes.includes(scope)}
                  onChange={e => toggleScope(scope, e.target.checked)}
                />
              ))}
            </div>
          </FormField>
          <div className="flex gap-2 justify-end">
            <Button variant="ghost" size="sm" onClick={() => setShowNew(false)}><Trans>Cancel</Trans></Button>
            <Button
              data-testid="key-create-submit"
              data-write variant="primary" size="sm"
              loading={createKey.isPending}
              disabled={!newKey.name || !newKey.projectId || newKey.scopes.length === 0}
              onClick={() => createKey.mutate(
                { name: newKey.name, projectId: newKey.projectId, scopes: newKey.scopes, userId: newKey.userId || undefined },
                { onSuccess: k => { setShowNew(false); setCreated(k); } },
              )}
            >
              <Trans>Generate</Trans>
            </Button>
          </div>
        </div>
      )}

      {keys.length === 0 && !showNew && (
        <EmptyState title={t`No API keys yet`} description={t`Generate one to start proxying requests.`} />
      )}
      {keys.length > 0 && (
        <div className="bg-card-2 rounded-lg border border-hairline overflow-hidden">
          <DataTable columns={columns} rows={keys} rowKey={k => k.id} />
        </div>
      )}

      {toDelete && (
        <ConfirmDialog
          entityName={toDelete.name}
          onConfirm={() => deleteKey.mutate(toDelete.id, { onSuccess: () => setToDelete(null) })}
          onCancel={() => setToDelete(null)}
          loading={deleteKey.isPending}
        />
      )}
    </>
  );
}
