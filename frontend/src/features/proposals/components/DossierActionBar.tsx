import { Trans, useLingui } from '@lingui/react/macro';
import type { I18n } from '@lingui/core';
import { ArrowUpRightIcon, CheckIcon, ResetIcon, StopIcon, XIcon } from '../../../components/icons';
import { Button } from '../../../components/ui/Button';
import type { OptimizationProposalDto, TheoryDto } from '../../../api/models';
import { ProposalStatus, TheoryStatus } from '../../../api/models';
import { adoptionLabel, REVIEW_META } from '../validatedView';

export interface DossierActions {
  onSetStatus: (status: ProposalStatus) => void;
  onReset: () => void;
  onReject: () => void;
  actionPending: boolean;
  resetPending: boolean;
  rejectPending: boolean;
}

interface Props extends DossierActions {
  theory: TheoryDto;
  proposal: OptimizationProposalDto | null;
}

/**
 * The dossier's pinned decision bar. What it offers follows the review state: a Draft proposal
 * gets Approve/Dismiss, an in-flight theory gets its cancel, terminal states read their outcome
 * and (where the server allows) a reset back to Proposed.
 */
export function DossierActionBar({ theory, proposal, onSetStatus, onReset, onReject, actionPending, resetPending, rejectPending }: Props) {
  const { t, i18n } = useLingui();
  const validated = theory.status === TheoryStatus.Validated;
  const reviewable = validated && (proposal == null || proposal.status === ProposalStatus.Draft);
  const inFlight = theory.status === TheoryStatus.Proposed || theory.status === TheoryStatus.Validating;
  const isValidating = theory.status === TheoryStatus.Validating;
  const failed = theory.status === TheoryStatus.Failed;
  // A reset re-runs validation from scratch; refused server-side once a proposal is promoted or
  // adopted, so hide it there.
  const canReset = !inFlight && proposal?.status !== ProposalStatus.Accepted && proposal?.status !== ProposalStatus.Adopted;

  return (
    <div
      className="flex shrink-0 flex-wrap items-center gap-2 border-t border-hairline px-4 py-3"
      data-testid="dossier-action-bar"
    >
      {reviewable && (
        <>
          <p className="m-0 w-full text-body-sm text-secondary" data-testid="proposal-approval-help">
            <Trans>Approval prepares the handoff. Apply the change in your agent's code, then Proxytrace checks live traffic for adoption.</Trans>
          </p>
          <Button
            variant="success" size="sm"
            loading={actionPending}
            disabled={proposal == null}
            leftIcon={<ArrowUpRightIcon size={12} />}
            onClick={() => proposal && onSetStatus(ProposalStatus.Accepted)}
            data-testid="proposal-promote-btn"
          >
            <Trans>Approve for implementation</Trans>
          </Button>
          <Button
            variant="secondary" size="sm"
            disabled={actionPending || proposal == null}
            onClick={() => proposal && onSetStatus(ProposalStatus.Rejected)}
            data-testid="proposal-dismiss-btn"
          >
            <Trans>Dismiss</Trans>
          </Button>
        </>
      )}

      {inFlight && (
        <Button
          variant="ghost" size="sm"
          loading={rejectPending}
          leftIcon={isValidating ? <StopIcon size={12} /> : <XIcon size={12} />}
          onClick={onReject}
          data-testid="theory-reject-btn"
        >
          {isValidating ? t`Cancel validation` : t`Reject theory`}
        </Button>
      )}

      {failed && (
        <Button
          variant="ghost" size="sm"
          loading={rejectPending} disabled={resetPending}
          leftIcon={<XIcon size={12} />}
          onClick={onReject}
          data-testid="theory-dismiss-btn"
        >
          <Trans>Dismiss</Trans>
        </Button>
      )}

      {!reviewable && !inFlight && (
        <span className="inline-flex items-center gap-1.5 text-body-sm text-secondary" data-testid="dossier-outcome">
          {proposal?.status === ProposalStatus.Adopted && <CheckIcon size={12} className="text-success" />}
          {outcomeText(theory, proposal, i18n)}
        </span>
      )}

      {canReset && (
        <Button
          variant="ghost" size="sm" className="ml-auto"
          loading={resetPending} disabled={actionPending || rejectPending}
          leftIcon={<ResetIcon size={12} />}
          onClick={onReset}
          data-testid="proposal-reset-btn"
        >
          {failed ? <Trans>Retry validation</Trans> : <Trans>Reset to Proposed</Trans>}
        </Button>
      )}
    </div>
  );
}

function outcomeText(theory: TheoryDto, proposal: OptimizationProposalDto | null, i18n: I18n): React.ReactNode {
  if (theory.status === TheoryStatus.Failed) {
    return <Trans>The A/B validation could not run (e.g. the provider was unreachable) — the theory was never tested. Retry it or dismiss it.</Trans>;
  }
  if (theory.status === TheoryStatus.Invalidated) {
    const hadAbTest = theory.pValue != null || theory.baselinePassRate != null;
    return hadAbTest
      ? <Trans>The A/B test found no improvement, so the theory was rejected automatically.</Trans>
      : <Trans>This theory was dismissed without running an A/B validation.</Trans>;
  }
  if (proposal?.status === ProposalStatus.Adopted) {
    return <Trans>{i18n._(adoptionLabel(proposal))} — the change is live in the agent.</Trans>;
  }
  if (proposal?.status === ProposalStatus.Rejected) return <Trans>Dismissed — a reviewer chose not to apply this change.</Trans>;
  return i18n._(REVIEW_META[proposal?.status ?? ProposalStatus.Draft].description);
}
