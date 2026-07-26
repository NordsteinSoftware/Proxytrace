import { Trans, useLingui } from '@lingui/react/macro';
import type { OptimizationProposalDto, TheoryDto } from '../../../api/models';
import { ProposalStatus, TheoryStatus } from '../../../api/models';
import { cn } from '../../../lib/cn';
import { agentColor } from '../../../lib/colors';
import { AskTraceyButton } from '../../../components/tracey/AskTraceyButton';
import { theoryPrompt } from '../../../components/tracey/askTraceyPrompts';
import { TONE_BG, TONE_SUBTLE_BG, TONE_TEXT } from '../shared';
import { THEORY_SOURCE_LABEL, THEORY_STATUS_META } from '../theoryMeta';
import { buildGainSummary, formatDeltaPt, REVIEW_META } from '../validatedView';
import { formatPValue, significanceLabel, theoryShortId } from '../theoryQueue';
import { KindPill } from './KindPill';

interface Props {
  theory: TheoryDto;
  proposal: OptimizationProposalDto | null;
  suiteName: string | undefined;
}

/**
 * Verdict header of the dossier pane: what kind of change this is, who it belongs to, and —
 * once the A/B test has spoken — the measured gain as the headline number.
 */
export function DossierHeader({ theory, proposal, suiteName }: Props) {
  const { t, i18n } = useLingui();
  const aColor = agentColor(theory.agentId);
  const terminal =
    theory.status === TheoryStatus.Validated ||
    theory.status === TheoryStatus.Invalidated ||
    theory.status === TheoryStatus.Failed;
  const failed = theory.status === TheoryStatus.Failed;
  const gain = terminal ? buildGainSummary(theory, proposal) : null;
  const validated = theory.status === TheoryStatus.Validated;
  const won = validated && gain?.deltaPt != null && gain.deltaPt > 0;
  const status = validated
    ? REVIEW_META[proposal?.status ?? ProposalStatus.Draft]
    : THEORY_STATUS_META[theory.status];

  return (
    <header className="flex flex-col gap-2.5" data-testid="dossier-header">
      <div className="flex flex-wrap items-center gap-1.5">
        <KindPill kind={theory.kind} />
        <span
          className={cn('inline-flex items-center gap-1.5 rounded-none px-2 py-0.5 text-caption font-semibold', TONE_SUBTLE_BG[status.tone], TONE_TEXT[status.tone])}
          data-testid="dossier-status"
        >
          {'pulse' in status && status.pulse && <span aria-hidden className={cn('pulse-dot size-1.5 rounded-full', TONE_BG[status.tone])} />}
          {i18n._(status.label)}
        </span>
        {/* agentColor is a runtime hash-derived color → inline style is the sanctioned use (DESIGN §6). */}
        <span
          className="mono inline-flex items-center gap-1 rounded-none px-2 py-px text-caption font-medium"
          style={{ background: `color-mix(in srgb, ${aColor} 12%, transparent)`, color: aColor }}
        >
          <span aria-hidden className="size-1.5 rounded-full" style={{ background: aColor }} />
          {theory.agentName}
        </span>
        <span className="mono ml-auto text-caption text-muted" data-testid="dossier-handle">{theoryShortId(theory.id)}</span>
        <AskTraceyButton data-testid="ask-tracey-btn-theory" prompt={() => theoryPrompt(theory)} />
      </div>

      {gain ? (
        <div className="flex items-end gap-3" data-testid="gain-hero">
          <span
            className={cn(
              'mono text-display font-bold leading-none tracking-[-0.02em]',
              won ? 'text-success' : validated ? 'text-secondary' : 'text-muted',
            )}
          >
            {gain.deltaPt != null ? formatDeltaPt(gain.deltaPt) : `${gain.toPct}%`}
          </span>
          <span className="mono pb-0.5 text-body-sm text-secondary">
            {gain.fromPct != null ? t`${gain.fromPct}% → ${gain.toPct}% pass rate` : t`pass rate after change`}
          </span>
          {theory.pValue != null && (
            <span className="mono ml-auto pb-0.5 text-caption text-muted">
              {formatPValue(theory.pValue)} · {i18n._(significanceLabel(theory.pValue, validated))}
            </span>
          )}
        </div>
      ) : (
        terminal && (
          <div className="text-body-sm text-muted" data-testid="gain-hero">
            {failed
              ? <Trans>The A/B validation could not run — nothing was measured.</Trans>
              : <Trans>No pass-rate metrics recorded.</Trans>}
          </div>
        )
      )}

      <p className="m-0 text-caption text-muted">
        {failed
          ? <Trans>via {i18n._(THEORY_SOURCE_LABEL[theory.source])} · validation errored against {suiteName ?? t`a suite`}</Trans>
          : terminal
            ? <Trans>via {i18n._(THEORY_SOURCE_LABEL[theory.source])} · validated against {suiteName ?? t`a suite`}</Trans>
            : <Trans>via {i18n._(THEORY_SOURCE_LABEL[theory.source])} · validating against {suiteName ?? t`a suite`}</Trans>}
      </p>
    </header>
  );
}
