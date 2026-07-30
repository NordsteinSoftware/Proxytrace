import { Trans, useLingui } from '@lingui/react/macro';
import { Combobox } from '../../../components/ui/Combobox';
import { FormField } from '../../../components/ui/FormField';
import { Select } from '../../../components/ui/Select';
import { readonlyFieldCls } from '../../../components/ui/classes';
import type { CostLimitDto } from '../../../api/costs';
import type { AgentListItemDto, ApiKeyDto } from '../../../api/models';
import type { DraftScope, ScopeKind } from '../limitDraft';
import { SCOPE_KINDS, toScopeKind } from '../limitDraft';
import { emptyReason, type ScopeAvailability, type ScopeEmptyReason } from '../scopeAvailability';

interface BudgetScopeFieldsProps {
  /** The budget being edited, or null when creating — a saved scope is shown, never re-picked. */
  editing: CostLimitDto | null;
  scope: DraftScope;
  availability: ScopeAvailability;
  onChange: (scope: DraftScope) => void;
}

/**
 * The scope half of the budget editor: **two** decisions, one control each — the kind of scope, and
 * then which agent or key. They used to share a single flat dropdown of
 * `project | agent×N | key×N`, which had no search (installs have many agents), offered the
 * project scope even when it was already taken, and — because a budgeted agent is filtered out of
 * the roster — could not represent its own value when reopened, printing the raw `agent:<uuid>`.
 *
 * Editing shows both values read-only. They are read off the saved budget, not looked up in the
 * roster, so the name is always available and an id can never reach the screen.
 */
export function BudgetScopeFields({ editing, scope, availability, onChange }: BudgetScopeFieldsProps) {
  const { t } = useLingui();

  const kindLabel: Record<ScopeKind, string> = {
    project: t`Whole project`,
    agent: t`Agent`,
    apiKey: t`API key`,
  };

  if (editing) {
    const savedKind: ScopeKind = editing.agentId !== null
      ? 'agent'
      : editing.apiKeyId !== null ? 'apiKey' : 'project';

    return (
      <>
        <FormField label={t`Scope`}>
          <div className={readonlyFieldCls} data-testid="budget-scope-locked-kind">
            {kindLabel[savedKind]}
          </div>
        </FormField>

        {savedKind !== 'project' && (
          <FormField label={kindLabel[savedKind]}>
            <div className={readonlyFieldCls} data-testid="budget-scope-locked-element">
              {/* A null name means the key was revoked while the dialog was open. Say so — the id
                  would be worse than useless to whoever is reading it. */}
              {editing.agentName ?? editing.apiKeyName ?? t`No longer available`}
            </div>
          </FormField>
        )}

        <p className="text-body-sm text-muted">
          <Trans>A budget's scope is fixed. To retarget it, delete this budget and create a new one.</Trans>
        </p>
      </>
    );
  }

  const reason = emptyReason(scope.kind, availability);
  const items: readonly (AgentListItemDto | ApiKeyDto)[] =
    scope.kind === 'agent' ? availability.agents : availability.apiKeys;
  // The roster is live query data: the picked agent can be deleted — or take a budget in another
  // tab — while this dialog is open. The Combobox then shows its placeholder, because the value is
  // no longer one of its items, and without this line the field would just look untouched.
  const elementGone =
    scope.kind !== 'project' &&
    scope.elementId !== null &&
    !items.some(item => item.id === scope.elementId);

  return (
    <>
      <FormField label={t`Scope`} htmlFor="budget-scope-kind">
        <Select
          id="budget-scope-kind"
          value={scope.kind}
          onValueChange={value => onChange({ kind: toScopeKind(value), elementId: null })}
          data-testid="budget-scope-select"
        >
          {/*
            Every kind stays selectable even when it has nothing to offer. A scope holds at most one
            budget, and the interesting question — *why* can't I add another? — is answered by the
            line below; a disabled option would withhold exactly that. Save is what refuses.
          */}
          {SCOPE_KINDS.map(kind => (
            <option key={kind} value={kind}>{kindLabel[kind]}</option>
          ))}
        </Select>
      </FormField>

      {scope.kind !== 'project' && reason === null && (
        <FormField label={kindLabel[scope.kind]}>
          <Combobox
            value={scope.elementId}
            onChange={id => onChange({ ...scope, elementId: id })}
            items={items}
            itemKey={item => item.id}
            // Also the search predicate, so a key's prefix has to be part of its label or searching
            // for the thing printed on the key card would match nothing.
            itemLabel={item => ('keyPrefix' in item ? `${item.name} (${item.keyPrefix})` : item.name)}
            placeholder={scope.kind === 'agent' ? t`Select an agent…` : t`Select an API key…`}
            searchPlaceholder={scope.kind === 'agent' ? t`Search agents…` : t`Search API keys…`}
            aria-label={kindLabel[scope.kind]}
            data-testid="budget-scope-element"
          />
        </FormField>
      )}

      {elementGone && (
        <p className="text-body-sm text-warn" data-testid="budget-scope-stale">
          {scope.kind === 'agent'
            ? <Trans>That agent is no longer available for a budget. Pick another.</Trans>
            : <Trans>That API key is no longer available for a budget. Pick another.</Trans>}
        </p>
      )}

      {reason !== null && (
        <p className="text-body-sm text-warn" data-testid="budget-scope-empty">
          <ScopeEmptyCopy kind={scope.kind} reason={reason} />
        </p>
      )}
    </>
  );
}

/**
 * Why this scope has nothing left to point at. Three distinct answers, because they call for three
 * different next steps — and the old UI gave none of them: the project option was offered as if it
 * were free, and an exhausted agent/key group simply vanished from the dropdown.
 */
function ScopeEmptyCopy({ kind, reason }: { kind: ScopeKind; reason: ScopeEmptyReason }) {
  if (kind === 'project') {
    return <Trans>This project already has a budget. Edit that one to change its limits.</Trans>;
  }
  if (reason === 'all-taken') {
    return kind === 'agent'
      ? <Trans>Every agent in this project already has a budget. Edit one instead.</Trans>
      : <Trans>Every API key in this project already has a budget. Edit one instead.</Trans>;
  }
  return kind === 'agent'
    ? (
      <Trans>
        This project has no agents yet. One appears once a call arrives naming it in the{' '}
        <span className="font-mono text-primary">x-proxytrace-agent</span> header.
      </Trans>
    )
    : <Trans>This project has no inbound API keys yet. Create one under Providers first.</Trans>;
}
