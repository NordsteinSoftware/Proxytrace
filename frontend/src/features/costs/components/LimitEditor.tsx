import { useState } from 'react';
import { Trans, useLingui } from '@lingui/react/macro';
import { Modal } from '../../../components/overlays/Modal';
import { Button } from '../../../components/ui/Button';
import { FormField } from '../../../components/ui/FormField';
import { Input } from '../../../components/ui/Input';
import { Select } from '../../../components/ui/Select';
import { Switch } from '../../../components/ui/Switch';
import type { CostLimitDto } from '../../../api/costs';
import type { AgentListItemDto } from '../../../api/models';
import { parseAmount, validateBudget, type BudgetFormError } from '../budgetMeter';
import { draftFromLimit, emptyDraft, type LimitDraft } from '../limitDraft';

interface LimitEditorProps {
  /** The limit being edited, or null when creating a new one. */
  editing: CostLimitDto | null;
  /** Agents available for an override — already filtered to those without a budget. */
  availableAgents: readonly AgentListItemDto[];
  isSaving: boolean;
  onSave: (draft: LimitDraft) => void;
  onDelete?: () => void;
  onClose: () => void;
}

/**
 * Create/edit form for one monthly budget. The scope is fixed once created — retargeting a budget
 * at a different agent means a different budget — so the picker is disabled when editing.
 */
export function LimitEditor({
  editing,
  availableAgents,
  isSaving,
  onSave,
  onDelete,
  onClose,
}: LimitEditorProps) {
  const { t } = useLingui();
  // A draft, not a copy of server state: the user is editing text that is not yet a budget.
  const [draft, setDraft] = useState<LimitDraft>(() => (editing ? draftFromLimit(editing) : emptyDraft()));

  const soft = parseAmount(draft.soft);
  const hard = parseAmount(draft.hard);
  const parseError = !soft.valid || !hard.valid;
  const validation: BudgetFormError | null = parseError ? null : validateBudget(soft.value, hard.value);

  const errorText = parseError
    ? t`Enter a number, or leave the field empty to unset the threshold.`
    : validation === 'no-threshold'
      ? t`Set at least a soft or a hard limit.`
      : validation === 'not-positive'
        ? t`Amounts must be greater than zero.`
        : validation === 'soft-above-hard'
          ? t`The soft limit must not exceed the hard limit.`
          : null;

  return (
    <Modal
      onClose={onClose}
      title={editing ? t`Edit budget` : t`New budget`}
      footer={
        <div className="flex items-center justify-between gap-2 w-full">
          {editing && onDelete ? (
            <Button variant="dangerOutline" size="sm" onClick={onDelete} data-testid="budget-delete-btn">
              <Trans>Delete</Trans>
            </Button>
          ) : <span />}
          <div className="flex items-center gap-2">
            <Button variant="secondary" size="sm" onClick={onClose} data-testid="budget-cancel-btn">
              <Trans>Cancel</Trans>
            </Button>
            <Button
              variant="primary"
              size="sm"
              disabled={errorText !== null}
              loading={isSaving}
              onClick={() => onSave(draft)}
              data-testid="budget-save-btn"
            >
              <Trans>Save</Trans>
            </Button>
          </div>
        </div>
      }
    >
      <div className="flex flex-col gap-4" data-testid="budget-editor">
        <FormField label={t`Scope`}>
          <Select
            value={draft.agentId ?? ''}
            onValueChange={value => setDraft(d => ({ ...d, agentId: value === '' ? null : value }))}
            disabled={editing !== null}
          >
            <option value="">{t`Whole project`}</option>
            {availableAgents.map(agent => (
              <option key={agent.id} value={agent.id}>{agent.name}</option>
            ))}
          </Select>
          <p className="text-body-sm text-muted">
            <Trans>An agent's spend also counts toward the project budget.</Trans>
          </p>
        </FormField>

        <FormField label={t`Soft limit (EUR)`}>
          <Input
            value={draft.soft}
            onChange={e => setDraft(d => ({ ...d, soft: e.target.value }))}
            placeholder={t`No warning`}
            inputMode="decimal"
            data-testid="budget-soft-input"
          />
          <p className="text-body-sm text-muted">
            <Trans>Raises a warning notification. Never blocks.</Trans>
          </p>
        </FormField>

        <FormField label={t`Hard limit (EUR)`}>
          <Input
            value={draft.hard}
            onChange={e => setDraft(d => ({ ...d, hard: e.target.value }))}
            placeholder={t`No blocking`}
            inputMode="decimal"
            data-testid="budget-hard-input"
          />
          <p className="text-body-sm text-muted">
            <Trans>
              Raises a critical notification and rejects further proxied calls until the month resets
              or the limit is raised.
            </Trans>
          </p>
        </FormField>

        <Switch
          checked={draft.enabled}
          onChange={value => setDraft(d => ({ ...d, enabled: value }))}
          label={t`Enabled`}
          data-testid="budget-enabled-switch"
        />

        {errorText && <p className="text-body-sm text-danger" data-testid="budget-editor-error">{errorText}</p>}

        <p className="text-body-sm text-muted">
          <Trans>
            Saving clears this budget's alert state, so the next check re-evaluates against the new
            thresholds.
          </Trans>
        </p>
      </div>
    </Modal>
  );
}
