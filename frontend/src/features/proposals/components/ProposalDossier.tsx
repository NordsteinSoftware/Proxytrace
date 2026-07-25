import { Link } from 'react-router';
import { Trans } from '@lingui/react/macro';
import { ExternalLinkIcon } from '../../../components/icons';
import type { OptimizationProposalDto, TheoryDto } from '../../../api/models';
import { AbTestHero } from '../AbTestHero';
import { ChangeSections } from './ChangeSections';
import { EvidenceList } from './EvidenceList';
import { HandoffPanel } from './HandoffPanel';

interface Props {
  theory: TheoryDto;
  proposal: OptimizationProposalDto | null;
}

/**
 * Dossier body for a theory the A/B test has settled: the concrete change gets the wide column,
 * the evidence that justifies it sits beside it. A promoted proposal leads with its handoff
 * package, since applying the change in code is now the user's next step.
 */
export function ProposalDossier({ theory, proposal }: Props) {
  return (
    <div className="flex flex-col gap-4" data-testid="validated-proposal">
      {proposal && <HandoffPanel proposal={proposal} />}

      <div className="grid grid-cols-1 gap-4 @3xl:grid-cols-[minmax(0,1fr)_minmax(260px,320px)]">
        <section className="flex min-w-0 flex-col gap-2">
          <h3 className="m-0 text-h2 font-semibold leading-tight text-primary"><Trans>Proposed change</Trans></h3>
          <ChangeSections details={theory.details} />
        </section>

        <aside className="flex min-w-0 flex-col gap-3">
          <section className="flex flex-col gap-1.5">
            <h3 className="m-0 text-h2 font-semibold leading-tight text-primary"><Trans>Why this change</Trans></h3>
            <p className="m-0 text-body leading-relaxed text-secondary">{theory.rationale}</p>
          </section>

          {proposal?.abTestRun ? (
            <AbTestHero ab={proposal.abTestRun} expectedPassRateDelta={proposal.expectedPassRateDelta} />
          ) : theory.abTestRunId ? (
            <Link
              to={`/runs?run=${theory.abTestRunId}`}
              className="inline-flex items-center gap-1 self-start text-body-sm text-secondary transition-colors hover:text-primary"
            >
              <Trans>View A/B run</Trans> <ExternalLinkIcon size={11} />
            </Link>
          ) : null}

          {theory.evidenceTestRunIds.length > 0 && <EvidenceList ids={theory.evidenceTestRunIds} />}
        </aside>
      </div>
    </div>
  );
}
