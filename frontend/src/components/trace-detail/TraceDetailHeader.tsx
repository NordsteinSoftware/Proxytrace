import { useNavigate } from 'react-router-dom';
import type { AgentCallDto, CustomAnomalyHitDto } from '../../api/models';
import { agentColor, modelColor } from '../../lib/colors';
import { fmtDateTime } from '../../lib/format';
import { cn } from '../../lib/cn';
import { PlusIcon, ChevronRightIcon, KeyIcon } from '../icons';
import { CopyButton } from '../ui/CopyButton';
import { ColoredBadge } from '../ui/ColoredBadge';
import { Button, IconButton } from '../ui/Button';
import { AskTraceyButton } from '../tracey/AskTraceyButton';
import { tracePrompt } from '../tracey/askTraceyPrompts';
import { Trans, useLingui } from '@lingui/react/macro';

export interface PromoteAction {
  disabled: boolean;
  tooltip: string;
  onStart: () => void;
}

interface Props {
  trace: AgentCallDto;
  anomalyHits: CustomAnomalyHitDto[];
  onClose: () => void;
  onPrev?: () => void;
  onNext?: () => void;
  promote: PromoteAction;
}

/**
 * Drawer header, two rows. Identity row: the agent (title, entity-colored, jumps to the agent
 * page) + model + HTTP status, with prev/next navigation. Provenance row: the full trace ID
 * (mono, copyable, truncates first) + exact capture time, with the actions. Message/tool-call
 * counts are deliberately absent — the tab badges below already carry them.
 */
export function TraceDetailHeader({ trace, anomalyHits, onClose, onPrev, onNext, promote }: Props) {
  const navigate = useNavigate();
  const { t } = useLingui();

  const aColor = agentColor(trace.agentId ?? trace.id);
  const statusOk = trace.httpStatus >= 200 && trace.httpStatus < 300;
  const statusErr = trace.httpStatus >= 500;
  const statusColor = statusOk ? 'var(--success)' : statusErr ? 'var(--danger)' : 'var(--warn)';
  const statusLabel = statusOk ? t`OK` : statusErr ? t`ERROR` : t`RATE_LIMIT`;

  return (
    <div className="px-5 pt-4 pb-3 flex flex-col gap-2.5 border-b border-hairline shrink-0">
      {/* Identity row: agent (title) + model + status, prev/next right */}
      <div className="flex items-center gap-2.5 min-w-0">
        <span className="w-[6px] h-[6px] rounded-full shrink-0" style={{ background: aColor }} />
        {trace.agentName && trace.agentId ? (
          <Button
            variant="link"
            data-testid="trace-detail-agent-name"
            onClick={() => { onClose(); navigate(`/agents?id=${trace.agentId}`); }}
            title={t`Open agent`}
            className="text-h2 font-semibold leading-none truncate min-w-0 block"
            style={{ color: aColor }}
          >
            {trace.agentName}
          </Button>
        ) : (
          <span className="text-h2 font-semibold leading-none text-muted truncate min-w-0">
            <Trans>No agent</Trans>
          </span>
        )}
        <ColoredBadge color={modelColor(trace.model)} label={trace.model} dot size="md" />
        <span
          className={cn(
            'inline-flex items-center gap-1.5 px-2 py-0.5 rounded-none text-caption font-semibold font-mono shrink-0',
            statusOk ? 'bg-success-subtle' : statusErr ? 'bg-danger-subtle' : 'bg-[color-mix(in_srgb,var(--warn)_15%,transparent)]',
          )}
          style={{ color: statusColor }}
        >
          <span className="w-[5px] h-[5px] rounded-full" style={{ background: statusColor }} />
          {trace.httpStatus} {statusLabel}
        </span>
        <span className="flex-1" />
        {(onPrev || onNext) && (
          <div className="flex items-center gap-1 shrink-0">
            {onPrev && (
              <IconButton size="sm" onClick={onPrev} aria-label={t`Previous trace`} className="rotate-180">
                <ChevronRightIcon size={14} strokeWidth={2.5} />
              </IconButton>
            )}
            {onNext && (
              <IconButton size="sm" onClick={onNext} aria-label={t`Next trace`}>
                <ChevronRightIcon size={14} strokeWidth={2.5} />
              </IconButton>
            )}
          </div>
        )}
      </div>

      {/* Provenance row: trace ID + exact capture time left, actions right */}
      <div className="flex items-center gap-2 min-w-0">
        <span className="mono text-body-sm text-secondary truncate min-w-0" title={trace.id}>{trace.id}</span>
        <CopyButton text={trace.id} label={t`Copy trace ID`} className="shrink-0" />
        <span aria-hidden className="text-body-sm text-muted shrink-0">·</span>
        <span className="mono text-body-sm text-muted whitespace-nowrap shrink-0">{fmtDateTime(trace.createdAt)}</span>
        {trace.sessionId && (
          <>
            <span aria-hidden className="text-body-sm text-muted shrink-0">·</span>
            <Button
              variant="link"
              size="sm"
              data-testid="trace-session-link"
              onClick={() => { onClose(); navigate(`/sessions/${trace.sessionId}`); }}
              title={t`Open session`}
              leftIcon={<KeyIcon size={12} />}
              className="text-body-sm shrink-0"
            >
              <Trans>Session</Trans>
            </Button>
          </>
        )}
        <span className="flex-1" />
        <div className="flex items-center gap-2 shrink-0">
          <AskTraceyButton
            data-testid="ask-tracey-btn-trace"
            prompt={() => tracePrompt(trace, anomalyHits)}
          />
          <Button
            data-testid="promote-btn"
            onClick={() => !promote.disabled && promote.onStart()}
            disabled={promote.disabled}
            title={promote.tooltip || undefined}
            variant="primary"
            size="sm"
            leftIcon={<PlusIcon strokeWidth={2.5} size={12} />}
          >
            <Trans>Add test</Trans>
          </Button>
        </div>
      </div>
    </div>
  );
}
