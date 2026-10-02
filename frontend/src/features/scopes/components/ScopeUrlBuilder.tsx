import { useState } from 'react';
import { Trans, useLingui } from '@lingui/react/macro';
import { scopeKey } from '../../../lib/ingestion';
import { FormField } from '../../../components/ui/FormField';
import { Input } from '../../../components/ui/Input';
import { ScopeBaseUrl } from './ScopeBaseUrl';

/**
 * Scopes are created by traffic, not by a form — so "new scope" is a URL builder: type a name, see
 * the base URL to point a client at. The scope appears in the list with its first trace. Purely
 * client-side; the key rules mirror the backend's. Rendered inside a card (empty page) or a modal.
 */
export function ScopeUrlBuilder() {
  const { t } = useLingui();
  const [name, setName] = useState('');
  const key = scopeKey(name);

  return (
    <div className="flex flex-col gap-3" data-testid="scope-url-builder">
      <p className="text-body text-secondary m-0">
        <Trans>
          A scope groups the agents of one use case. It is created automatically by the first trace
          that names it — pick a name and point a client at its URL.
        </Trans>
      </p>
      <FormField label={t`Scope name`}>
        <Input
          autoFocus
          value={name}
          onChange={e => setName(e.target.value)}
          placeholder={t`e.g. support-agents`}
          data-testid="scope-url-builder-input"
        />
      </FormField>
      {name.trim() !== '' && key === '' && (
        <span className="text-body-sm text-danger" data-testid="scope-url-builder-invalid">
          <Trans>That name can't be used as a scope — try letters or digits.</Trans>
        </span>
      )}
      {key !== '' && <ScopeBaseUrl scopeKey={key} />}
    </div>
  );
}
