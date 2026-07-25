import { Link } from 'react-router';
import { Trans, useLingui } from '@lingui/react/macro';
import { msg } from '@lingui/core/macro';
import { type MessageDescriptor } from '@lingui/core';
import { CheckIcon, SparklesIcon } from '../../../../components/icons';
import { Badge } from '../../../../components/ui/Badge';
import { Spinner } from '../../../../components/ui/Spinner';
import { agentColor } from '../../../../lib/colors';
import { ProposalKind, TheoryStatus, type TheoryDto } from '../../../../api/models';
import { ToolUIFrame } from './ToolUIFrame';
import { PRIORITY_VARIANT, THEORY_STATUS_LABEL, THEORY_STATUS_VARIANT } from './badge-variants';
import { TheoryChangePreview } from './TheoryChangePreview';
import { useLiveTheory } from './useLiveTheory';

const KIND_LABEL: Record<ProposalKind, MessageDescriptor> = {
  [ProposalKind.SystemPrompt]: msg`System prompt`,
  [ProposalKind.Tool]: msg`Tool update`,
  [ProposalKind.ModelSwitch]: msg`Model switch`,
};

/**
 * The live theory card Tracey renders after submitting an optimization theory. Streams the
 * validation status (queued → A/B testing → improved/rejected) and, on a win, links to the
 * proposal the theory spawned.
 */
export function LiveTheoryCard({ initial }: { initial: TheoryDto }) {
  const { t, i18n } = useLingui();
  const theory = useLiveTheory(initial);
  const color = agentColor(theory.agentId);
  const isRunning = theory.status === TheoryStatus.Proposed || theory.status === TheoryStatus.Validating;
  const kindLabel = i18n._(KIND_LABEL[theory.kind]);

  return (
    <ToolUIFrame
      state="ready"
      title={t`${kindLabel} for ${theory.agentName}`}
      icon={<SparklesIcon size={14} />}
      accentBar={color}
      live={isRunning}
      testId="tracey-theory-card"
    >
      <div className="flex flex-col gap-2.5">
        <div className="flex flex-wrap items-center gap-1.5">
          <Badge label={i18n._(THEORY_STATUS_LABEL[theory.status])} variant={THEORY_STATUS_VARIANT[theory.status]} size="sm" />
          <Badge label={theory.priority} variant={PRIORITY_VARIANT[theory.priority]} size="sm" />
          <span className="text-body-sm text-muted"><Trans>via Tracey AI</Trans></span>
        </div>

        <p className="line-clamp-2 text-body-sm text-secondary">{theory.rationale}</p>

        <TheoryChangePreview details={theory.details} />

        <div className="mt-0.5 flex items-center gap-2 border-t border-hairline pt-2 text-body-sm" data-testid="tracey-theory-status">
          {isRunning && (
            <>
              <Spinner size={12} />
              <span className="text-secondary"><Trans>Running A/B test…</Trans></span>
            </>
          )}
          {theory.status === TheoryStatus.Validated && (
            <>
              <span className="text-success"><CheckIcon size={14} /></span>
              <span className="text-secondary"><Trans>Improved the pass rate.</Trans></span>
              <Link
                to={`/proposals?agentId=${theory.agentId}`}
                data-testid="tracey-theory-proposal-link"
                className="ml-auto font-medium text-accent hover:text-[var(--accent-hover)] focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[color-mix(in_srgb,var(--accent-primary)_60%,transparent)]"
              >
                <Trans>View proposal</Trans>
              </Link>
            </>
          )}
          {theory.status === TheoryStatus.Invalidated && (
            <span className="text-muted"><Trans>No improvement — theory rejected.</Trans></span>
          )}
          {theory.status === TheoryStatus.Failed && (
            <span className="text-danger"><Trans>The A/B test could not run — the theory was not tested.</Trans></span>
          )}
        </div>
      </div>
    </ToolUIFrame>
  );
}
