import { useState } from 'react';
import { Trans, useLingui } from '@lingui/react/macro';
import { Button, IconButton } from './Button';
import { Popover } from './Popover';
import { Input } from './Input';
import { FormField } from './FormField';
import { RowButton } from './RowButton';
import { ClockIcon, ChevronDownIcon, XIcon, CheckIcon } from '../icons';
import { cn } from '../../lib/cn';
import {
  TIME_PRESETS,
  ALL_TIME,
  formatRangeLabel,
  isRangeActive,
  isoToLocalInput,
  localInputToIso,
  presetWindow,
  type TimeRange,
  type TimeRangePreset,
} from '../../lib/timeRange';

interface TimeRangePickerProps {
  value: TimeRange;
  onChange: (range: TimeRange) => void;
  /** Prefix for the component's data-testid hooks (e.g. "error-log-time", "traces-time"). */
  testId?: string;
  /**
   * Overrides the trigger label for the unbounded range. Pass this when the page does **not** query
   * all of history for it — the Costs page bounds an unbounded range to the current UTC month, and
   * a trigger reading "All time" would be telling the user something untrue.
   */
  unboundedLabel?: string;
}

interface Draft {
  from: string;
  to: string;
}

function seedDraft(value: TimeRange): Draft {
  if (value.kind === 'absolute') {
    return { from: isoToLocalInput(value.from), to: isoToLocalInput(value.to) };
  }
  if (value.kind === 'preset') {
    const w = presetWindow(value.preset);
    return { from: isoToLocalInput(w.from), to: isoToLocalInput(w.to) };
  }
  return { from: '', to: '' };
}

/**
 * From/To time-range filter for the Error Log. Combines one-click relative presets with an
 * absolute custom range, in a single popover. Pure range logic lives in `../timeRange.ts`.
 */
export function TimeRangePicker({
  value,
  onChange,
  testId = 'time-range',
  unboundedLabel,
}: TimeRangePickerProps) {
  const { t, i18n } = useLingui();
  const [open, setOpen] = useState(false);
  const [draft, setDraft] = useState<Draft>(() => seedDraft(value));

  const active = isRangeActive(value);
  const activePreset = value.kind === 'preset' ? value.preset : null;

  // Re-seed the custom inputs from the committed range each time the popover opens.
  function handleOpenChange(next: boolean) {
    if (next) setDraft(seedDraft(value));
    setOpen(next);
  }

  // Picking a preset both commits the (live, open-ended) range and pre-fills the custom From/To
  // inputs with its concrete window, so the selection is visible and can be fine-tuned. The
  // popover stays open for that reason; a second click on the trigger (or Esc) dismisses it.
  function applyPreset(preset: TimeRangePreset) {
    const w = presetWindow(preset);
    setDraft({ from: isoToLocalInput(w.from), to: isoToLocalInput(w.to) });
    onChange({ kind: 'preset', preset });
  }

  function clear() {
    onChange(ALL_TIME);
    setOpen(false);
  }

  const fromIso = localInputToIso(draft.from);
  const toIso = localInputToIso(draft.to);
  const invalid = fromIso != null && toIso != null && fromIso > toIso;

  function applyCustom() {
    if (invalid) return;
    onChange(fromIso == null && toIso == null ? ALL_TIME : { kind: 'absolute', from: fromIso, to: toIso });
    setOpen(false);
  }

  const trigger = (
    <Button
      variant="secondary"
      size="sm"
      leftIcon={<ClockIcon size={13} />}
      rightIcon={<ChevronDownIcon size={12} strokeWidth={2.5} className="text-muted" />}
      className={cn('h-9 font-medium', active && 'text-primary border-accent/60')}
      data-testid={`${testId}-trigger`}
    >
      {value.kind === 'all' && unboundedLabel ? unboundedLabel : formatRangeLabel(value, i18n)}
    </Button>
  );

  return (
    <div className="inline-flex items-center gap-1">
      <Popover open={open} onOpenChange={handleOpenChange} align="end" trigger={trigger}>
        <div className="flex flex-col sm:flex-row" data-testid={`${testId}-popover`}>
          <div className="flex flex-col gap-0.5 p-1.5 sm:w-[176px] border-b sm:border-b-0 sm:border-r border-hairline">
            <span className="px-2 pt-1 pb-1 text-caption font-medium uppercase tracking-wide text-secondary">
              <Trans>Quick ranges</Trans>
            </span>
            {TIME_PRESETS.map(p => {
              const isActive = activePreset === p.preset;
              return (
                <RowButton
                  key={p.preset}
                  onClick={() => applyPreset(p.preset)}
                  data-testid={`${testId}-preset-${p.preset}`}
                  className={cn(
                    'flex items-center justify-between gap-2 px-2 py-1.5 rounded-md text-body transition-colors duration-[var(--motion-fast)]',
                    isActive ? 'text-primary bg-[var(--bg-wash-active)]' : 'text-secondary hover:text-primary hover:bg-[var(--bg-wash-hover)]',
                  )}
                >
                  <span>{i18n._(p.label)}</span>
                  {isActive && <CheckIcon size={12} strokeWidth={2.5} className="text-accent shrink-0" />}
                </RowButton>
              );
            })}
          </div>

          <div className="flex flex-col gap-3 p-3 sm:w-[272px]">
            <span className="text-caption font-medium uppercase tracking-wide text-secondary"><Trans>Custom range</Trans></span>
            <FormField label={t`From`} htmlFor={`${testId}-from`}>
              <Input
                id={`${testId}-from`}
                type="datetime-local"
                // eslint-disable-next-line lingui/no-unlocalized-strings -- size variant token, not UI copy
                inputSize="sm"
                value={draft.from}
                onChange={e => setDraft(d => ({ ...d, from: e.target.value }))}
                invalid={invalid}
                leftAddon={<ClockIcon size={12} />}
                data-testid={`${testId}-from`}
              />
            </FormField>
            <FormField label={t`To`} htmlFor={`${testId}-to`} error={invalid ? t`"From" must be before "To".` : undefined}>
              <Input
                id={`${testId}-to`}
                type="datetime-local"
                // eslint-disable-next-line lingui/no-unlocalized-strings -- size variant token, not UI copy
                inputSize="sm"
                value={draft.to}
                onChange={e => setDraft(d => ({ ...d, to: e.target.value }))}
                invalid={invalid}
                leftAddon={<ClockIcon size={12} />}
                data-testid={`${testId}-to`}
              />
            </FormField>
            <div className="flex items-center justify-end gap-2 pt-1">
              <Button variant="ghost" size="sm" onClick={clear} data-testid={`${testId}-reset`}>
                <Trans>Clear</Trans>
              </Button>
              <Button variant="primary" size="sm" onClick={applyCustom} disabled={invalid} data-testid={`${testId}-apply`}>
                <Trans>Apply</Trans>
              </Button>
            </div>
          </div>
        </div>
      </Popover>

      {active && (
        <IconButton
          size="sm"
          aria-label={t`Clear time filter`}
          onClick={() => onChange(ALL_TIME)}
          data-testid={`${testId}-clear`}
        >
          <XIcon size={13} />
        </IconButton>
      )}
    </div>
  );
}
