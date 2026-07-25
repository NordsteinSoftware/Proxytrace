import { Link } from 'react-router';
import { Trans } from '@lingui/react/macro';
import { ExternalLinkIcon } from '../../../components/icons';
import type { TheoryDto } from '../../../api/models';
import { TheoryStatus } from '../../../api/models';
import { ChangeSections } from './ChangeSections';
import { EvidenceList } from './EvidenceList';

interface Props {
  theory: TheoryDto;
}

/**
 * Dossier body for a theory still moving through the loop: the planned change and its
 * motivation, plus where validation stands. Decisions wait until the A/B test has spoken.
 */
export function InFlightDossier({ theory }: Props) {
  const validating = theory.status === TheoryStatus.Validating;
  return (
    <div className="flex flex-col gap-4" data-testid="inflight-dossier">
      <section className="flex flex-col gap-2 rounded-md bg-card-2 px-3.5 py-3">
        {validating ? (
          <>
            <p className="m-0 text-body-sm text-secondary"><Trans>Benchmarking the change against the current agent…</Trans></p>
            <div className="indeterminate-bar h-[3px] overflow-hidden bg-card" />
            {theory.abTestRunId && (
              <Link
                to={`/runs?run=${theory.abTestRunId}`}
                className="inline-flex items-center gap-1 self-start text-body-sm text-secondary transition-colors hover:text-primary"
              >
                <Trans>View A/B run</Trans> <ExternalLinkIcon size={11} />
              </Link>
            )}
          </>
        ) : (
          <p className="m-0 text-body-sm text-secondary">
            <Trans>Queued — the candidate change has not been benchmarked yet. A proposal is created only once the A/B test shows an improvement.</Trans>
          </p>
        )}
      </section>

      <div className="grid grid-cols-1 gap-4 @3xl:grid-cols-[minmax(0,1fr)_minmax(260px,320px)]">
        <section className="flex min-w-0 flex-col gap-2">
          <h3 className="m-0 text-h2 font-semibold leading-tight text-primary"><Trans>Planned change</Trans></h3>
          <ChangeSections details={theory.details} />
        </section>

        <aside className="flex min-w-0 flex-col gap-3">
          <section className="flex flex-col gap-1.5">
            <h3 className="m-0 text-h2 font-semibold leading-tight text-primary"><Trans>Why this change</Trans></h3>
            <p className="m-0 text-body leading-relaxed text-secondary">{theory.rationale}</p>
          </section>
          {theory.evidenceTestRunIds.length > 0 && <EvidenceList ids={theory.evidenceTestRunIds} />}
        </aside>
      </div>
    </div>
  );
}
