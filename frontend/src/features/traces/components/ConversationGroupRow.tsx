import { Pill } from '../../../components/ui/Pill';
import { StatusDot } from '../../../components/ui/StatusDot';
import { agentColor, modelColor } from '../../../lib/colors';
import { fmtTokens, fmtRelative } from '../../../lib/format';
import { cn } from '../../../lib/cn';
import { tracePreview } from '../../../lib/trace';
import type { AgentCallListItemDto } from '../../../api/models';
import { TRACE_GRID_CLS, toolCount } from '../tracesMeta';
import type { ConversationGroup } from '../tracesMeta';
import { OutlierCell, TokenCell, CachedCell, ToolsCell, LatencyCell } from './TraceTableCells';
import { Trans, Plural } from '@lingui/react/macro';

interface Props {
  group: ConversationGroup;
  expanded: boolean;
  onToggle: () => void;
  selectedId: string | null;
  /** Turns that arrived over the live stream moments ago — they flash the arrival wash once. */
  freshIds: ReadonlySet<string>;
  onSelectTrace: (trace: AgentCallListItemDto) => void;
}

export function ConversationGroupRow({ group, expanded, onToggle, selectedId, freshIds, onSelectTrace }: Props) {
  const { turns, conversationId } = group;
  const totalTokens = turns.reduce((n, t) => n + t.inputTokens + t.outputTokens, 0);
  const totalInput = turns.reduce((n, t) => n + t.inputTokens, 0);
  const totalCachedInput = turns.reduce((n, t) => n + t.cachedInputTokens, 0);
  const totalMs = turns.reduce((n, t) => n + t.durationMs, 0);
  const totalTools = turns.reduce((n, t) => n + toolCount(t), 0);
  const agentName = turns[0].agentName;
  const model = turns[0].model;
  const c = turns[0].agentId ? agentColor(turns[0].agentId) : agentColor(conversationId);
  const allOk = turns.every(t => t.httpStatus >= 200 && t.httpStatus < 300);
  // When every turn shares the same status, show that exact code (e.g. "200") rather than the
  // coarse "2xx" bucket; only fall back to a bucket label when the turns disagree.
  const uniformStatus = turns.every(t => t.httpStatus === turns[0].httpStatus) ? turns[0].httpStatus : null;
  // Collapsed group is flagged when any turn is — OR the turns' bitmasks so the chip's tooltip
  // lists every characteristic that tripped across the conversation.
  const groupFlags = turns.reduce((acc, t) => acc | t.outlierFlags, 0);
  // A live turn joining an existing conversation grows this group rather than adding a row, so the
  // header carries the arrival wash on the group's behalf — collapsed, it is the only thing on screen.
  const groupFresh = turns.some(t => freshIds.has(t.id));

  return (
    // One wrapping element, not a fragment: the list virtualizer measures a single node per item,
    // and an expanding group has to re-measure as one. Deliberately unstyled — the header and turn
    // rows below already own their own dividers and padding.
    <div>
      {/* Header row */}
      <div
        role="row"
        data-testid={`conversation-group-row-${conversationId}`}
        onClick={onToggle}
        className={cn(
          'grid items-center px-4 py-2.5 min-h-[44px] cursor-pointer transition-colors duration-[100ms] border-b border-border-subtle hover:bg-white/[0.025] bg-white/[0.015]',
          groupFresh && 'arrival-flash',
          TRACE_GRID_CLS,
        )}
      >
        <span className="flex items-center gap-2 min-w-0 pr-3">
          <span className="w-[3px] h-[18px] shrink-0" style={{ background: c }} />
          <span
            className="inline-flex items-center text-caption font-semibold px-1.5 py-0.5 rounded-none shrink-0"
            style={{ background: `color-mix(in srgb, ${c} 14%, transparent)`, color: c }}
          >
            <Plural value={turns.length} one="# turn" other="# turns" />
          </span>
          <span className="text-body-sm text-secondary overflow-hidden text-ellipsis whitespace-nowrap min-w-0">
            {tracePreview(turns[0]) ?? <span className="text-muted">—</span>}
          </span>
        </span>

        <span className="text-body text-secondary overflow-hidden text-ellipsis whitespace-nowrap pr-3 @max-2xl:hidden">
          {agentName ?? <span className="text-muted">—</span>}
        </span>

        <span className="min-w-0 overflow-hidden pr-3 @max-2xl:hidden">
          <Pill label={model} color={modelColor(model)} size="sm" />
        </span>

        <span className="inline-flex items-center gap-1.5">
          {uniformStatus != null
            ? <StatusDot httpStatus={uniformStatus} />
            : allOk
              ? <><StatusDot httpStatus={200} showLabel={false} /><span className="mono text-body-sm text-success">2xx</span></>
              : <><StatusDot httpStatus={500} showLabel={false} /><span className="mono text-body-sm text-warn"><Trans>mixed</Trans></span></>}
        </span>

        <span className="@max-2xl:hidden"><ToolsCell count={totalTools} /></span>

        <span className="mono text-body-sm @max-2xl:hidden">
          <span className="text-primary">{fmtTokens(totalTokens)}</span>
          <span className="text-muted ml-1.5 text-caption"><Trans>total</Trans></span>
        </span>

        <span className="@max-2xl:hidden"><CachedCell cachedInput={totalCachedInput} input={totalInput} /></span>

        <span className="@max-2xl:hidden"><LatencyCell ms={totalMs} /></span>

        <span className="flex items-center justify-center"><OutlierCell flags={groupFlags} /></span>

        <span className="text-muted text-body-sm whitespace-nowrap text-right">{fmtRelative(turns[0].createdAt)}</span>
      </div>

      {/* Child turn rows */}
      {expanded && turns.map((turn, i) => (
        <div
          key={turn.id}
          role="row"
          data-trace-id={turn.id}
          data-testid={`conversation-turn-${turn.id}`}
          onClick={() => onSelectTrace(turn)}
          className={cn(
            'grid items-center pl-8 pr-4 py-2.5 min-h-[44px] cursor-pointer transition-colors duration-[100ms]',
            'border-b border-border-subtle hover:bg-white/[0.025]',
            turn.id === selectedId && 'bg-white/[0.04]',
            freshIds.has(turn.id) && 'arrival-flash',
            TRACE_GRID_CLS,
          )}
          style={{ borderLeft: `2px solid color-mix(in srgb, ${c} 38%, transparent)` }}
        >
          <span className="flex items-center gap-2 min-w-0 pr-3">
            <span className="mono text-caption text-muted shrink-0"><Trans>Turn {turns.length - i}</Trans></span>
            <span className="text-body-sm text-secondary overflow-hidden text-ellipsis whitespace-nowrap min-w-0">
              {tracePreview(turn) ?? <span className="text-muted">—</span>}
            </span>
          </span>
          <span className="text-body text-secondary overflow-hidden text-ellipsis whitespace-nowrap pr-3 @max-2xl:hidden">
            {turn.agentName ?? <span className="text-muted">—</span>}
          </span>
          <span className="min-w-0 overflow-hidden pr-3 @max-2xl:hidden">
            <Pill label={turn.model} color={modelColor(turn.model)} size="sm" />
          </span>
          <StatusDot httpStatus={turn.httpStatus} />
          <span className="@max-2xl:hidden"><ToolsCell count={toolCount(turn)} /></span>
          <span className="@max-2xl:hidden"><TokenCell trace={turn} /></span>
          <span className="@max-2xl:hidden"><CachedCell cachedInput={turn.cachedInputTokens} input={turn.inputTokens} /></span>
          <span className="@max-2xl:hidden"><LatencyCell ms={turn.durationMs} /></span>
          <span className="flex items-center justify-center"><OutlierCell flags={turn.outlierFlags} /></span>
          <span className="text-muted text-body-sm whitespace-nowrap text-right">{fmtRelative(turn.createdAt)}</span>
        </div>
      ))}
    </div>
  );
}
