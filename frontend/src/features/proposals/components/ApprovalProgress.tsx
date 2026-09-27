import { useLingui } from '@lingui/react/macro';
import { ProposalStatus, type OptimizationProposalDto } from '../../../api/models';
import { cn } from '../../../lib/cn';

export function ApprovalProgress({ proposal }: { proposal: OptimizationProposalDto }) {
  const { t } = useLingui();
  if (proposal.status === ProposalStatus.Rejected) return null;
  const current = proposal.status === ProposalStatus.Adopted ? 2 : proposal.status === ProposalStatus.Accepted ? 1 : 0;
  const steps = [t`Review`, t`Approved`, proposal.adoptedManually ? t`Marked adopted` : t`Observed in traffic`];
  return (
    <ol className="m-0 flex list-none flex-wrap items-center gap-2 p-0 text-body-sm" data-testid="proposal-approval-progress">
      {steps.map((label, i) => (
        <li key={i} aria-current={i === current ? 'step' : undefined}
          className={cn('flex items-center gap-2', i === current ? 'font-semibold text-accent' : 'text-secondary')}>
          {i > 0 && <span aria-hidden="true">→</span>}{label}
        </li>
      ))}
    </ol>
  );
}
