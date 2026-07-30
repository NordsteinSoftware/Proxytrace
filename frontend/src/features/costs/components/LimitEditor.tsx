import { useRef, useState } from 'react';
import { Trans, useLingui } from '@lingui/react/macro';
import { Modal } from '../../../components/overlays/Modal';
import { Button } from '../../../components/ui/Button';
import { Collapsible } from '../../../components/ui/Collapsible';
import { FormField } from '../../../components/ui/FormField';
import { Input } from '../../../components/ui/Input';
import { Switch } from '../../../components/ui/Switch';
import type { CostLimitDto } from '../../../api/costs';
import { parseAmount, validateBudget, type BudgetFormError } from '../budgetMeter';
import { draftFromLimit, emptyDraft, toLimitScope, type LimitDraft, type ScopeKind } from '../limitDraft';
import { defaultScopeKind, isScopeAvailable, type ScopeAvailability } from '../scopeAvailability';
import { BudgetScopeFields } from './BudgetScopeFields';

interface LimitEditorProps {
  /** The limit being edited, or null when creating a new one. */
  editing: CostLimitDto | null;
  /** Which scopes a new budget can still target — every scope holds at most one. */
  availability: ScopeAvailability;
  isSaving: boolean;
  /** The last save failure, surfaced beside the fields instead of only as a toast. */
  saveError: string | null;
  onSave: (draft: LimitDraft) => void;
  onClose: () => void;
}

/**
 * Create/edit form for one monthly budget. The scope is fixed once created — retargeting a budget
 * at a different agent or key means a different budget — so editing shows it read-only rather than
 * as a disabled picker.
 */
export function LimitEditor({
  editing,
  availability,
  isSaving,
  saveError,
  onSave,
  onClose,
}: LimitEditorProps) {
  const { t } = useLingui();
  // A draft, not a copy of server state: the user is editing text that is not yet a budget. A new
  // budget opens on the first scope that is actually free, so Save is never a guaranteed conflict.
  const [draft, setDraft] = useState<LimitDraft>(
    () => (editing ? draftFromLimit(editing) : emptyDraft(defaultScopeKind(availability))),
  );
  const softRef = useRef<HTMLInputElement>(null);

  const soft = parseAmount(draft.soft);
  const hard = parseAmount(draft.hard);
  const parseError = !soft.valid || !hard.valid;
  const validation: BudgetFormError | null = parseError ? null : validateBudget(soft.value, hard.value);
  const scope = toLimitScope(draft.scope);
  // Already spoken for — the kind is exhausted, or the agent/key the draft points at has since been
  // deleted or budgeted elsewhere. Either way the picker says so; Save must not offer the round
  // trip that would come back 409.
  const scopeTaken = editing === null && !isScopeAvailable(draft.scope, availability);

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
      maxWidth={520}
      // Focus the first amount field: the scope is either pre-selected or already fixed, so the
      // number is what the user came to type. Without this the Close button takes focus.
      initialFocusRef={softRef}
      title={editing ? t`Edit monthly budget` : t`New monthly budget`}
      footer={
        <>
          <Button variant="secondary" size="sm" onClick={onClose} data-testid="budget-cancel-btn">
            <Trans>Cancel</Trans>
          </Button>
          <Button
            variant="primary"
            size="sm"
            disabled={errorText !== null || scope === null || scopeTaken}
            loading={isSaving}
            onClick={() => onSave(draft)}
            data-testid="budget-save-btn"
          >
            <Trans>Save</Trans>
          </Button>
        </>
      }
    >
      <div className="flex flex-col gap-4" data-testid="budget-editor">
        {/*
          The period is the one thing about a budget that is never chosen and never shown elsewhere
          in this dialog — without it "hard limit 100" reads equally as a daily or a lifetime cap.
        */}
        <p className="text-body-sm text-secondary">
          <Trans>
            Measured over the <strong className="text-primary">UTC calendar month</strong>. Resets on
            the 1st, when alerts re-arm and any block lifts.
          </Trans>
        </p>

        <BudgetScopeFields
          editing={editing}
          scope={draft.scope}
          availability={availability}
          onChange={next => setDraft(d => ({ ...d, scope: next }))}
        />

        <div className="grid grid-cols-2 gap-3">
          <FormField label={t`Soft limit`} htmlFor="budget-soft">
            <Input
              id="budget-soft"
              ref={softRef}
              value={draft.soft}
              onChange={e => setDraft(d => ({ ...d, soft: e.target.value }))}
              placeholder={t`No warning`}
              inputMode="decimal"
              invalid={!soft.valid || validation === 'soft-above-hard'}
              leftAddon="€"
              data-testid="budget-soft-input"
            />
          </FormField>
          <FormField label={t`Hard limit`} htmlFor="budget-hard">
            <Input
              id="budget-hard"
              value={draft.hard}
              onChange={e => setDraft(d => ({ ...d, hard: e.target.value }))}
              placeholder={t`No blocking`}
              inputMode="decimal"
              invalid={!hard.valid}
              leftAddon="€"
              data-testid="budget-hard-input"
            />
          </FormField>
        </div>
        <p className="text-body-sm text-secondary">
          <Trans>
            The soft limit warns. The hard limit warns and rejects further proxied calls until the
            month resets or you raise it.
          </Trans>
        </p>

        {errorText && <p className="text-body-sm text-danger" data-testid="budget-editor-error">{errorText}</p>}
        {/* The API's own refusal — a scope taken since the dialog opened, a licence lapse. It used
            to reach the user only as a bare "409 Conflict" toast beside an unchanged dialog. */}
        {saveError && <p className="text-body-sm text-danger" data-testid="budget-save-error">{saveError}</p>}

        <div className="flex items-center justify-between">
          <span className="text-title font-medium text-secondary"><Trans>Enabled</Trans></span>
          <Switch
            checked={draft.enabled}
            onChange={value => setDraft(d => ({ ...d, enabled: value }))}
            aria-label={t`Enabled`}
            data-testid="budget-enabled-switch"
          />
        </div>
        {!draft.enabled && (
          <p className="text-body-sm text-muted">
            <Trans>A disabled budget keeps its configuration but stops warning and blocking.</Trans>
          </p>
        )}

        <div data-testid="budget-scope-help">
          <Collapsible
            title={<span className="text-body-sm text-secondary"><Trans>How this scope is enforced</Trans></span>}
            contentClassName="pt-2 pl-4 flex flex-col gap-2"
          >
            <EnforcementNote kind={draft.scope.kind} />
            {editing && (
              <p className="text-body-sm text-muted">
                <Trans>Saving clears this budget's alert state, so the next check re-evaluates against the new thresholds.</Trans>
              </p>
            )}
          </Collapsible>
        </div>
      </div>
    </Modal>
  );
}

/** How a budget of this scope actually bites — the one caveat per scope, not all three at once. */
function EnforcementNote({ kind }: { kind: ScopeKind }) {
  return (
    <p className="text-body-sm text-muted">
      {kind === 'agent' ? (
        <Trans>
          Blocking an agent only catches calls that send the agent header. Traffic without it is
          held by the project budget. An agent's spend also counts toward the project budget.
        </Trans>
      ) : kind === 'apiKey' ? (
        <Trans>
          Every proxied call authenticates with a key, so this block cannot be bypassed — except by
          callers using the provider's own key, which the project budget holds. A key's spend also
          counts toward the project budget.
        </Trans>
      ) : (
        <Trans>
          Covers every call in the project, including traffic that names no agent — which makes it
          the reliable backstop behind any agent or key budget.
        </Trans>
      )}
    </p>
  );
}
