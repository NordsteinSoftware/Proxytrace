import { useState } from 'react';
import { Trans, useLingui } from '@lingui/react/macro';
import type { ModelEndpointDto, UpdateModelPricingRequest } from '../../../api/models';
import { Modal } from '../../../components/overlays/Modal';
import { Button } from '../../../components/ui/Button';
import { FormField } from '../../../components/ui/FormField';
import { Input } from '../../../components/ui/Input';
import { useUpdateModelPricing } from '../hooks/useProviderMutations';

export function EditPricesModal({ model, onClose }: { model: ModelEndpointDto; onClose: () => void }) {
  const { t } = useLingui();
  const [values, setValues] = useState({
    inputTokenCost: model.inputTokenCost?.toString() ?? '',
    outputTokenCost: model.outputTokenCost?.toString() ?? '',
    cachedInputTokenCost: model.cachedInputTokenCost?.toString() ?? '',
  });
  const [error, setError] = useState<string | null>(null);
  const update = useUpdateModelPricing(model.providerId, model.id);
  const fields = [
    { key: 'inputTokenCost', label: t`Input price` },
    { key: 'outputTokenCost', label: t`Output price` },
    { key: 'cachedInputTokenCost', label: t`Cached-input price` },
  ] as const;

  function save() {
    const prices: UpdateModelPricingRequest = { inputTokenCost: null, outputTokenCost: null, cachedInputTokenCost: null, manualPricing: true };
    for (const { key } of fields) {
      const value = values[key].trim();
      if (!value) continue;
      const number = Number(value);
      const [whole, fraction = ''] = value.replace(/^0+(?=\d)/, '').split('.');
      if (!/^\d{1,12}(\.\d{0,6})?$/.test(value) || !Number.isFinite(number) || number >= 1e12
        || number.toFixed(6) !== `${whole}.${fraction.padEnd(6, '0')}`) {
        setError(t`Enter prices from 0 to below 1,000,000,000,000 with up to 6 decimal places. Leave blank for unknown.`);
        return;
      }
      prices[key] = number;
    }
    if (prices.inputTokenCost !== null && prices.cachedInputTokenCost !== null && prices.cachedInputTokenCost > prices.inputTokenCost) {
      setError(t`Cached-input price cannot exceed the input price.`);
      return;
    }
    setError(null);
    update.mutate(prices, { onSuccess: onClose });
  }

  return (
    <Modal title={t`Edit prices`} onClose={() => { if (!update.isPending) onClose(); }} maxWidth={460}>
      <form className="flex flex-col gap-3.5" onSubmit={event => { event.preventDefault(); save(); }}>
        <p className="m-0 text-body text-secondary">{model.modelName}</p>
        <p className="m-0 text-body-sm text-muted"><Trans>EUR per 1M tokens. Leave blank for unknown. An unknown cached-input price uses the input price.</Trans></p>
        <p className="m-0 text-body-sm text-muted"><Trans>Saving makes all three prices manual. Manual prices are preserved when models and prices reload.</Trans></p>
        {fields.map(({ key, label }) => (
          <FormField key={key} label={label} htmlFor={`price-${key}`}>
            <Input id={`price-${key}`} inputMode="decimal" value={values[key]} disabled={update.isPending}
              onChange={event => setValues(previous => ({ ...previous, [key]: event.target.value }))} />
          </FormField>
        ))}
        {(error || update.isError) && <p role="alert" className="m-0 text-body-sm text-danger">{error ?? t`Could not save prices. Please try again.`}</p>}
        <div className="flex justify-end gap-2 mt-2">
          <Button variant="ghost" onClick={onClose} disabled={update.isPending}><Trans>Cancel</Trans></Button>
          <Button type="submit" variant="primary" loading={update.isPending}><Trans>Save</Trans></Button>
        </div>
      </form>
    </Modal>
  );
}
