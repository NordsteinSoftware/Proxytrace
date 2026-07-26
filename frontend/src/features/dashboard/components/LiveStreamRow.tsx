// One row of the dashboard live trace stream — either a single trace or a
// collapsed multi-turn conversation summary. Clicking drills into the Traces tab.

import { Trans, Plural } from '@lingui/react/macro';
import { Pill } from '../../../components/ui/Pill';
import { RowButton } from '../../../components/ui/RowButton';
import { agentColor, modelColor, statusColor } from '../../../lib/colors';
import { fmtLatency, fmtRelative, fmtTokens } from '../../../lib/format';
import { tracePreview } from '../../../lib/trace';
import type { TraceRow } from '../../../lib/trace';
import { cn } from '../../../lib/cn';
import { hoverAccentWashCls } from '../../../components/ui/classes';

// Shared grid template — header row and every data row align to this. Columns are
// fixed-width (not auto) so the header grid and each row's grid compute identical
// tracks; auto tracks would size to each grid's own content and drift out of line.
// Columns: dot · message+agent · turns · model · status · tokens · latency · age.
//
// The panel this feed lives in (Dashboard's 1.45fr live-theater column) is narrower
// than a viewport-based breakpoint would assume — at 1024/1280px viewport it's only
// ~486–535px wide, well under the ~554px the full 8-track template needs before the
// flexible message column even gets space (see #295). Viewport breakpoints (`lg:`/`xl:`)
// can't see that; they only know the window width, not the sibling column eating into
// this one. So — per DESIGN.md §4's "wide row grids" pattern (see also `tracesMeta.ts`'s
// TRACE_GRID_CLS) — this collapses on a *container* query instead: the wrapping card
// declares `@container` (LiveTraceStream.tsx) and exposes both templates as CSS vars;
// below the `2xl` container breakpoint (~672px) the turns and model columns drop out
// entirely (both the grid track and the corresponding cells, via `@max-2xl:hidden`),
// leaving the message column all the room it needs even at narrow panel widths.
// eslint-disable-next-line lingui/no-unlocalized-strings -- Tailwind grid-template class, not UI copy
export const LIVE_STREAM_GRID = 'grid [grid-template-columns:var(--live-grid)] gap-4 @max-2xl:[grid-template-columns:var(--live-grid-narrow)] @max-2xl:gap-3';

// Full (wide) and narrow column templates, exposed as CSS vars on the `@container` card — see
// {@link LIVE_STREAM_GRID}. Kept as plain strings (not a combined object) so this file keeps
// exporting only constants + the component, per react-refresh/only-export-components.
// Track widths are budgeted around the model column, which carries the longest value in the row
// (`deepseek/deepseek-v4-flash`): the column gap drops 20px→16px and hands the whole 28px it frees
// to the model track. The turns track keeps its 64px — "12 turns" already fills it. Total fixed
// width stays at the 554px documented above (442px of tracks + 112px of gaps), so the narrow-panel
// budget is unchanged.
export const LIVE_STREAM_GRID_WIDE = '14px minmax(0,1fr) 64px 132px 56px 60px 64px 52px';
export const LIVE_STREAM_GRID_NARROW = '14px minmax(0,1fr) 56px 60px 64px 52px';

/** Shared visibility class for the turns + model cells, which drop below the `2xl` container breakpoint. */
// eslint-disable-next-line lingui/no-unlocalized-strings -- Tailwind class, not UI copy
export const NARROW_HIDDEN = '@max-2xl:hidden';

// The turns and model cells sit on *fixed* grid tracks, and `justify-self-center` sizes them to
// their content — so content wider than the track (a long model id like `deepseek/deepseek-v4-flash`
// on the 104px model track) overflows on *both* sides and paints over the turns badge and the status
// column. `max-w-full` clamps the cell back to its track and gives the Badge inside a real width to
// shrink into; the Badge ellipsises and keeps the full value in its `title`.
// eslint-disable-next-line lingui/no-unlocalized-strings -- Tailwind classes, not UI copy
const CLAMPED_CELL = 'justify-self-center max-w-full min-w-0 overflow-hidden';

interface Props {
  row: TraceRow;
  freshIds: Set<string>;
  isLast: boolean;
  now: number;
  onSelect: (id: string) => void;
}

export function LiveStreamRow({ row, freshIds, isLast, now, onSelect }: Props) {
  // `px-5` (not `px-1.5`) because the row is full-bleed — the list cancels the card's `px-3.5`
  // so the hover wash and the divider reach the card edge; see LiveTraceStream.
  const rowCls = cn('w-full text-left', LIVE_STREAM_GRID, 'items-center py-2.5 px-5 font-mono text-body-sm cursor-pointer transition-colors', hoverAccentWashCls, isLast ? '' : 'border-b border-border-subtle');

  if (row.type === 'flat') {
    const t = row.trace;
    const sc = statusColor(t.httpStatus);
    const isFresh = freshIds.has(t.id);
    return (
      <RowButton
        data-testid={`live-trace-row-${t.id}`}
        onClick={() => onSelect(t.id)}
        className={cn(rowCls, isFresh && 'slide-in arrival-flash')}
      >
        <span className="size-[7px] rounded-full" style={{ background: sc }} />
        <span className="min-w-0 flex flex-col gap-0.5 pr-2 overflow-hidden">
          <span className="font-sans text-secondary overflow-hidden text-ellipsis whitespace-nowrap">
            {tracePreview(t) ?? <span className="text-muted">—</span>}
          </span>
          <span className="text-caption font-mono overflow-hidden text-ellipsis whitespace-nowrap" style={{ color: agentColor(t.agentId ?? '') }}>
            {t.agentName ?? <Trans>unknown agent</Trans>}
          </span>
        </span>
        <span className={cn('text-muted text-center', NARROW_HIDDEN)}>—</span>
        <span className={cn(CLAMPED_CELL, NARROW_HIDDEN)}><Pill label={t.model} color={modelColor(t.model)} size="sm" /></span>
        <span className="text-caption font-semibold text-center" style={{ color: sc }}>{t.httpStatus}</span>
        <span className="text-secondary text-right min-w-[54px]">{fmtTokens(t.inputTokens + t.outputTokens)}</span>
        <span className="text-muted text-right min-w-[58px]">{fmtLatency(t.durationMs)}</span>
        <span className="text-muted text-right text-caption tabular-nums">{fmtRelative(t.createdAt, now)}</span>
      </RowButton>
    );
  }

  const { turns, conversationId } = row;
  const head = turns[0];
  const totalTokens = turns.reduce((n, t) => n + t.inputTokens + t.outputTokens, 0);
  const totalMs = turns.reduce((n, t) => n + t.durationMs, 0);
  const allOk = turns.every(t => t.httpStatus >= 200 && t.httpStatus < 300);
  const sc = allOk ? 'var(--success)' : 'var(--warn)';
  const isFresh = turns.some(t => freshIds.has(t.id));
  return (
    <RowButton
      data-testid={`live-trace-group-${conversationId}`}
      onClick={() => onSelect(head.id)}
      className={cn(rowCls, isFresh && 'slide-in arrival-flash')}
    >
      <span className="size-[7px] rounded-full" style={{ background: sc }} />
      <span className="min-w-0 flex flex-col gap-0.5 pr-2 overflow-hidden">
        <span className="font-sans text-secondary overflow-hidden text-ellipsis whitespace-nowrap">
          {tracePreview(head) ?? <span className="text-muted">—</span>}
        </span>
        <span className="text-caption font-mono overflow-hidden text-ellipsis whitespace-nowrap" style={{ color: agentColor(head.agentId ?? '') }}>
          {head.agentName ?? <Trans>unknown agent</Trans>}
        </span>
      </span>
      <span className={cn(CLAMPED_CELL, 'inline-flex items-center text-caption font-semibold px-1.5 py-0.5 rounded-none text-accent bg-accent-subtle whitespace-nowrap', NARROW_HIDDEN)}>
        <span className="min-w-0 overflow-hidden text-ellipsis">
          <Plural value={turns.length} one="# turn" other="# turns" />
        </span>
      </span>
      <span className={cn(CLAMPED_CELL, NARROW_HIDDEN)}><Pill label={head.model} color={modelColor(head.model)} size="sm" /></span>
      <span className="text-caption font-semibold text-center" style={{ color: sc }}>{allOk ? '2xx' : <Trans>mixed</Trans>}</span>
      <span className="text-secondary text-right min-w-[54px]">{fmtTokens(totalTokens)}</span>
      <span className="text-muted text-right min-w-[58px]">{fmtLatency(totalMs)}</span>
      <span className="text-muted text-right text-caption tabular-nums">{fmtRelative(head.createdAt, now)}</span>
    </RowButton>
  );
}
