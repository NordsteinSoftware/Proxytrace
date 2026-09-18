import { useState } from 'react';
import { Trans, useLingui } from '@lingui/react/macro';
import type { ModelEndpointDto } from '../../../api/models';
import { Button, IconButton } from '../../../components/ui/Button';
import { ConfirmDialog } from '../../../components/overlays/ConfirmDialog';
import { EmptyState } from '../../../components/ui/EmptyState';
import { TrashIcon } from '../../../components/icons';
import { cn } from '../../../lib/cn';
import { EditPricesModal } from './EditPricesModal';
import { useDeleteModel, useUpdateModelPricing } from '../hooks/useProviderMutations';

// Last column is a FIXED width (not `auto`): the header's action cell is empty while each row's is
// row actions, so `auto` would resolve to different widths and shift every fr column out of
// alignment between header and body.
const GRID = cn('grid min-w-[760px] grid-cols-[2fr_1fr_1fr_1fr_180px]');

interface ModelsSectionProps {
  providerId: string;
  models: ModelEndpointDto[];
  reloading: boolean;
  onReload: () => void;
}

export function ModelsSection({ models, reloading, onReload }: ModelsSectionProps) {
  const { t } = useLingui();
  const [toEdit, setToEdit] = useState<ModelEndpointDto | null>(null);
  const [toDelete, setToDelete] = useState<ModelEndpointDto | null>(null);
  const deleteModel = useDeleteModel();

  return (
    <>
      <div className="flex items-center justify-between">
        <div>
          <div className="text-h2 font-semibold text-primary mb-0.5"><Trans>Models</Trans></div>
          <div className="text-body-sm text-muted"><Trans>Models and automatic prices reload from the provider. Manual prices are preserved.</Trans></div>
        </div>
        <Button data-testid="model-reload-btn" variant="ghost" size="sm" loading={reloading} onClick={() => onReload()}>
          <Trans>Reload models &amp; prices</Trans>
        </Button>
      </div>

      {models.length === 0 && (
        <EmptyState title={t`No models yet`} description={t`Reload to pull this provider's models, or let Proxytrace auto-discover them from traces.`} />
      )}
      {models.length > 0 && (
        <div className="bg-card-2 rounded-lg border border-hairline overflow-x-auto">
          <div className={`${GRID} px-4 py-2.5 text-caption font-semibold text-secondary tracking-[0.07em] uppercase border-b border-hairline`}>
            <span><Trans>Model</Trans></span><span><Trans>Input / 1M €</Trans></span><span><Trans>Output / 1M €</Trans></span><span><Trans>Cached / 1M €</Trans></span><span />
          </div>
          {models.map((m, i) => (
            <div key={m.id} data-testid={`model-row-${m.id}`} className={i < models.length - 1 ? 'border-b border-hairline' : ''}>
              <div className={`${GRID} px-4 py-2.5 items-center`}>
                <div className="min-w-0">
                  <span className="font-mono text-body text-primary break-all">{m.modelName}</span>
                  {m.manualPricing && <AutomaticPricing model={m} />}
                </div>
                <span className="text-body text-secondary">{m.inputTokenCost != null ? m.inputTokenCost.toFixed(4) : '—'}</span>
                <span className="text-body text-secondary">{m.outputTokenCost != null ? m.outputTokenCost.toFixed(4) : '—'}</span>
                <span className="text-body text-secondary">{m.cachedInputTokenCost != null ? m.cachedInputTokenCost.toFixed(4) : '—'}</span>
                <div className="flex items-center gap-1">
                  <Button data-write variant="ghost" size="sm" className="whitespace-normal" onClick={() => setToEdit(m)}><Trans>Edit prices</Trans></Button>
                  <IconButton aria-label={t`Delete model`} danger onClick={() => setToDelete(m)}>
                    <TrashIcon size={13} />
                  </IconButton>
                </div>
              </div>
            </div>
          ))}
        </div>
      )}

      {toEdit && <EditPricesModal model={toEdit} onClose={() => setToEdit(null)} />}

      {toDelete && (
        <ConfirmDialog
          entityName={toDelete.modelName}
          onConfirm={() => deleteModel.mutate(toDelete.id, { onSuccess: () => setToDelete(null) })}
          onCancel={() => setToDelete(null)}
          loading={deleteModel.isPending}
        />
      )}
    </>
  );
}

function AutomaticPricing({ model }: { model: ModelEndpointDto }) {
  const update = useUpdateModelPricing(model.providerId, model.id);
  return (
    <div className="flex flex-col items-start gap-1 text-body-sm">
      <span className="text-secondary"><Trans>Manual</Trans></span>
      <Button data-write variant="ghost" size="sm" className="whitespace-normal text-left" loading={update.isPending}
        onClick={() => update.mutate({ inputTokenCost: null, outputTokenCost: null, cachedInputTokenCost: null, manualPricing: false })}>
        <Trans>Use automatic pricing</Trans>
      </Button>
      {update.isError && <span role="alert" className="text-danger"><Trans>Could not restore automatic pricing. Please try again.</Trans></span>}
    </div>
  );
}
