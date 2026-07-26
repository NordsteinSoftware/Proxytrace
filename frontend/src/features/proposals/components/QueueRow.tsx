import { Trans, useLingui } from '@lingui/react/macro';
import { AlertTriangleIcon, BeakerIcon, CheckIcon, CpuIcon, XIcon, ZapIcon } from '../../../components/icons';
import { RowButton } from '../../../components/ui/RowButton';
import type { OptimizationProposalDto, TheoryDto } from '../../../api/models';
import { ProposalKind, ProposalStatus, TheoryStatus } from '../../../api/models';
import { cn } from '../../../lib/cn';
import { agentColor } from '../../../lib/colors';
import { fmtRelative } from '../../../lib/format';
import { SELECTION_ROW_INACTIVE, selectionBarStyle, selectionRowStyle } from '../../../lib/selectionRow';
import { KIND_META } from '../shared';
import { adoptionLabel } from '../validatedView';
import type { QueueGroupKey } from '../theoryQueue';
import { formatPValue, passRateTransition, significanceLabel, theoryShortId } from '../theoryQueue';

interface Props {
  theory: TheoryDto;
  proposal: OptimizationProposalDto | null;
  group: QueueGroupKey;
  selected: boolean;
  onSelect: () => void;
}

const KIND_ICON: Record<ProposalKind, React.ReactNode> = {
  [ProposalKind.SystemPrompt]: <BeakerIcon size={10} />,
  [ProposalKind.Tool]: <ZapIcon size={10} />,
  [ProposalKind.ModelSwitch]: <CpuIcon size={10} />,
};

// Kind → leaf text-color class; mirrors KIND_META colors without threading CSS-var strings.
const KIND_TEXT: Record<ProposalKind, string> = {
  [ProposalKind.SystemPrompt]: cn('text-accent'),
  [ProposalKind.Tool]: cn('text-success'),
  [ProposalKind.ModelSwitch]: cn('text-teal'),
};

/** One queue-rail row; density and metadata vary with the group the theory sits in. */
export function QueueRow({ theory, proposal, group, selected, onSelect }: Props) {
  const { i18n } = useLingui();
  // agentColor is a runtime hash-derived color → inline style is the sanctioned use (DESIGN §6).
  const aColor = agentColor(theory.agentId);
  const transition = passRateTransition(theory);
  const compact = group === 'inflight' || group === 'history';

  return (
    <RowButton
      onClick={onSelect}
      data-testid={`theory-row-${theory.id}`}
      className={cn('relative rounded-md px-3 py-2.5 transition-colors duration-[var(--motion-fast)]', !selected && SELECTION_ROW_INACTIVE)}
      style={selected ? selectionRowStyle(aColor) : undefined}
    >
      {selected && <span aria-hidden className="absolute left-0 top-1.5 bottom-1.5 w-[3px]" style={selectionBarStyle(aColor)} />}

      <div className="flex items-center gap-1.5">
        <span className={cn('inline-flex items-center gap-1 text-caption font-semibold', KIND_TEXT[theory.kind])}>
          {KIND_ICON[theory.kind]} {i18n._(KIND_META[theory.kind].label)}
        </span>
        <span className="ml-auto shrink-0">
          {group === 'decision' || group === 'adoption' ? (
            transition && transition.deltaPt !== 0 && (
              <span
                className={cn(
                  'mono rounded-none px-1.5 py-px text-caption font-semibold',
                  transition.deltaPt > 0 ? 'bg-success-subtle text-success' : 'bg-danger-subtle text-danger',
                )}
              >
                {transition.deltaPt > 0 ? '+' : '−'}{Math.abs(transition.deltaPt)}<Trans>pt</Trans>
              </span>
            )
          ) : group === 'inflight' ? (
            <span className="mono text-caption text-muted">{theoryShortId(theory.id)}</span>
          ) : (
            <span className="text-caption text-muted">{fmtRelative(theory.updatedAt)}</span>
          )}
        </span>
      </div>

      <div className={cn('mt-1 text-body leading-snug', compact ? 'truncate text-secondary' : 'line-clamp-2 font-medium text-primary')}>
        {theory.rationale}
      </div>

      <div className="mt-1.5 flex items-center gap-2">
        <span className="mono inline-flex min-w-0 items-center gap-1 text-caption" style={{ color: aColor }}>
          <span aria-hidden className="size-1.5 shrink-0 rounded-full" style={{ background: aColor }} />
          <span className="truncate">{theory.agentName}</span>
        </span>
        <span className="ml-auto shrink-0">
          <RowStatus theory={theory} proposal={proposal} group={group} />
        </span>
      </div>

      {theory.status === TheoryStatus.Validating && (
        <div className="indeterminate-bar mt-2 h-[3px] overflow-hidden bg-card-2" />
      )}
    </RowButton>
  );
}

/** Right-aligned status caption of a row: what the loop is doing with (or did to) the theory. */
function RowStatus({ theory, proposal, group }: { theory: TheoryDto; proposal: OptimizationProposalDto | null; group: QueueGroupKey }) {
  const { i18n } = useLingui();
  if (group === 'decision') {
    return theory.pValue != null ? (
      <span className="mono text-caption text-muted">
        {formatPValue(theory.pValue)} · {i18n._(significanceLabel(theory.pValue, theory.status === TheoryStatus.Validated))}
      </span>
    ) : null;
  }
  if (group === 'attention') {
    return (
      <span className="inline-flex items-center gap-1 text-caption text-danger">
        <AlertTriangleIcon size={10} /> <Trans>Could not be tested</Trans>
      </span>
    );
  }
  if (group === 'adoption') {
    return (
      <span className="inline-flex items-center gap-1.5 text-caption text-success">
        <span aria-hidden className="pulse-dot size-1.5 rounded-full bg-success" /> <Trans>Watching traffic</Trans>
      </span>
    );
  }
  if (group === 'inflight') {
    return theory.status === TheoryStatus.Validating ? (
      <span className="inline-flex items-center gap-1.5 text-caption text-teal">
        <span aria-hidden className="pulse-dot size-1.5 rounded-full bg-teal" /> <Trans>A/B in flight</Trans>
      </span>
    ) : (
      <span className="text-caption text-muted"><Trans>Queued for validation</Trans></span>
    );
  }
  // History: terminal outcome.
  if (proposal?.status === ProposalStatus.Adopted) {
    return (
      <span className="inline-flex items-center gap-1 text-caption text-success">
        <CheckIcon size={10} /> {i18n._(adoptionLabel(proposal))}
      </span>
    );
  }
  if (proposal?.status === ProposalStatus.Rejected) {
    return (
      <span className="inline-flex items-center gap-1 text-caption text-muted">
        <XIcon size={10} /> <Trans>Dismissed</Trans>
      </span>
    );
  }
  const disproven = theory.pValue != null || theory.baselinePassRate != null;
  return disproven ? (
    <span className="inline-flex items-center gap-1 text-caption text-muted">
      <XIcon size={10} /> <Trans>No improvement</Trans>
    </span>
  ) : (
    <span className="inline-flex items-center gap-1 text-caption text-muted">
      <XIcon size={10} /> <Trans>Dismissed</Trans>
    </span>
  );
}
