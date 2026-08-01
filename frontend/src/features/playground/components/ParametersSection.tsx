/**
 * Sampling / budget controls section of the RightRailDrawer: parameter sliders,
 * numeric max-tokens/seed inputs, and the reasoning-effort control.
 *
 * There is no choice-count (`n`) input. The playground streams into one message and the OpenAI
 * stream carries no choice index, so asking for several completions returns them interleaved and
 * unattributable while billing for all of them — see issue #496.
 */
import { Trans, useLingui } from '@lingui/react/macro';
import { Input } from '../../../components/ui/Input';
import type { ModelParametersDto } from '../../../api/models';
import type { PlaygroundOverrides } from '../state/types';
import { ParameterSlider } from './ParameterSlider';
import { ReasoningEffortControl } from './ReasoningEffortControl';

interface ParametersSectionProps {
  overrides: PlaygroundOverrides;
  defaultParameters?: ModelParametersDto | null;
  onChange: (next: PlaygroundOverrides) => void;
}

export function ParametersSection({
  overrides,
  defaultParameters,
  onChange,
}: ParametersSectionProps) {
  const { t } = useLingui();

  const setParam = (key: keyof ModelParametersDto, value: number | null) => {
    onChange({ ...overrides, parameters: { ...overrides.parameters, [key]: value } });
  };

  const setParamRaw = (key: keyof ModelParametersDto, value: string) => {
    const v = value === '' ? null : Number(value);
    if (value !== '' && Number.isNaN(v)) return;
    onChange({ ...overrides, parameters: { ...overrides.parameters, [key]: v } });
  };
  return (
    <div className="flex flex-col gap-3.5">
      <ParameterSlider
        label={t`Temperature`}
        value={overrides.parameters.temperature}
        defaultValue={defaultParameters?.temperature ?? null}
        min={0} max={2} step={0.01}
        onChange={v => setParam('temperature', v)}
        testId="parameter-slider-temperature"
      />
      <ParameterSlider
        label={t`Top-P`}
        value={overrides.parameters.topP}
        defaultValue={defaultParameters?.topP ?? null}
        min={0} max={1} step={0.01}
        onChange={v => setParam('topP', v)}
      />
      <ParameterSlider
        label={t`Freq Penalty`}
        value={overrides.parameters.frequencyPenalty}
        defaultValue={defaultParameters?.frequencyPenalty ?? null}
        min={-2} max={2} step={0.01}
        onChange={v => setParam('frequencyPenalty', v)}
      />
      <ParameterSlider
        label={t`Pres Penalty`}
        value={overrides.parameters.presencePenalty}
        defaultValue={defaultParameters?.presencePenalty ?? null}
        min={-2} max={2} step={0.01}
        onChange={v => setParam('presencePenalty', v)}
      />

      <div className="grid grid-cols-2 gap-1.5">
        <label className="flex flex-col gap-0.5">
          <span className="text-caption text-secondary uppercase tracking-[0.06em] font-semibold"><Trans>Max tokens</Trans></span>
          <Input
            type="number" min={1} step={1}
            value={overrides.parameters.maxTokens ?? ''}
            placeholder="—"
            onChange={e => setParamRaw('maxTokens', e.target.value)}
          />
        </label>
        <label className="flex flex-col gap-0.5">
          <span className="text-caption text-secondary uppercase tracking-[0.06em] font-semibold"><Trans>Seed</Trans></span>
          <Input
            type="number" step={1}
            value={overrides.parameters.seed ?? ''}
            placeholder="—"
            onChange={e => setParamRaw('seed', e.target.value)}
          />
        </label>
      </div>

      <ReasoningEffortControl overrides={overrides} onChange={onChange} />
    </div>
  );
}
