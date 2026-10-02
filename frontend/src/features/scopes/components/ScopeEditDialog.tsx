import { useState } from 'react';
import { useLingui } from '@lingui/react/macro';
import type { ScopeDetailDto, UpdateScopeRequest } from '../../../api/models';
import { Modal, ModalFooter } from '../../../components/overlays/Modal';
import { FormField } from '../../../components/ui/FormField';
import { Input } from '../../../components/ui/Input';
import { Textarea } from '../../../components/ui/Textarea';
import { MAX_SCOPE_DESCRIPTION, MAX_SCOPE_DISPLAY_NAME } from '../scopesMeta';

interface Props {
  scope: ScopeDetailDto;
  onCancel: () => void;
  onSubmit: (body: UpdateScopeRequest) => void;
  loading: boolean;
}

/** Edits a scope's user-curated label and description. The key is the URL segment and stays fixed. */
export function ScopeEditDialog({ scope, onCancel, onSubmit, loading }: Props) {
  const { t } = useLingui();
  const [displayName, setDisplayName] = useState(scope.displayName ?? '');
  const [description, setDescription] = useState(scope.description ?? '');

  return (
    <Modal
      title={t`Edit scope`}
      onClose={onCancel}
      footer={
        <ModalFooter
          onCancel={onCancel}
          onSubmit={() => onSubmit({
            displayName: displayName.trim() || null,
            description: description.trim() || null,
          })}
          submitLabel={loading ? t`Saving…` : t`Save`}
          loading={loading}
        />
      }
    >
      <div className="flex flex-col gap-4" data-testid="scope-edit-dialog">
        <FormField label={t`Key`}>
          <Input value={scope.key} disabled readOnly className="font-mono" />
        </FormField>
        <FormField label={t`Display name`}>
          <Input
            autoFocus
            value={displayName}
            maxLength={MAX_SCOPE_DISPLAY_NAME}
            onChange={e => setDisplayName(e.target.value)}
            placeholder={scope.key}
            data-testid="scope-display-name-input"
          />
        </FormField>
        <FormField label={t`Description`}>
          <Textarea
            value={description}
            maxLength={MAX_SCOPE_DESCRIPTION}
            rows={3}
            onChange={e => setDescription(e.target.value)}
            placeholder={t`What do the agents in this scope work on together?`}
            data-testid="scope-description-input"
          />
        </FormField>
      </div>
    </Modal>
  );
}
