import { useCallback, useMemo, useRef, useState } from 'react';
import { Trans, Plural, useLingui } from '@lingui/react/macro';
import {
  computeTimelineTrace, timeToX, xToTime, timelineAxisTicks, zoomTowardPivot, formatBucketInstant,
} from './chart-math';
import { useElementWidth } from '../../hooks/useElementWidth';
import { useWheelZoom } from '../../hooks/useWheelZoom';
import type { TraceHistogramBucket } from '../../api/models';

/** Don't let a zoom-in narrow the window below this — past it the histogram is just noise. */
const MIN_ZOOM_SPAN_MS = 1000;
/** Fraction of the span kept per wheel-in step (smaller = faster zoom). */
const WHEEL_ZOOM_FACTOR = 0.8;

interface Props {
  buckets: TraceHistogramBucket[];
  /** Window bounds in epoch ms (the active time-range). */
  from: number;
  to: number;
  /** Drag-selected a sub-range: zoom the window (and filter) into it. */
  onZoom: (range: { from: number; to: number }) => void;
  /** Double-click: step one zoom level back out. */
  onZoomOut: () => void;
  /** Whether a zoom-out is possible (controls the hint affordance). */
  canZoomOut: boolean;
  height?: number;
}

export function TraceTimeline({ buckets, from, to, onZoom, onZoomOut, canZoomOut, height = 92 }: Props) {
  const { t } = useLingui();
  const [ref, measuredWidth] = useElementWidth<HTMLDivElement>(600);
  const w = measuredWidth || 600;
  const geo = useMemo(() => computeTimelineTrace(buckets, w, height), [buckets, w, height]);
  // One tick per ~120px of strip width, kept between 2 and 7 so labels never crowd.
  const ticks = useMemo(() => {
    const count = Math.max(2, Math.min(7, Math.round(w / 120)));
    return timelineAxisTicks(from, to, geo.plotL, geo.plotR, count);
  }, [from, to, geo.plotL, geo.plotR, w]);
  const drag = useRef<{ startX: number } | null>(null);
  const [dragSel, setDragSel] = useState<{ from: number; to: number } | null>(null);
  const [hoverIdx, setHoverIdx] = useState<number | null>(null);

  const pxToTime = (clientX: number) => {
    const rect = ref.current?.getBoundingClientRect();
    if (!rect) return from;
    const xVb = ((clientX - rect.left) / rect.width) * w;
    return xToTime(xVb, from, to, geo.plotL, geo.plotR);
  };

  const bucketAt = (clientX: number) => {
    const rect = ref.current?.getBoundingClientRect();
    if (!rect || geo.points.length === 0) return null;
    const xVb = ((clientX - rect.left) / rect.width) * w;
    const slot = (geo.plotR - geo.plotL) / geo.points.length;
    return Math.min(geo.points.length - 1, Math.max(0, Math.floor((xVb - geo.plotL) / slot)));
  };

  const handlePointerDown = (e: React.PointerEvent) => {
    (e.target as Element).setPointerCapture(e.pointerId);
    drag.current = { startX: e.clientX };
  };

  const handlePointerMove = (e: React.PointerEvent) => {
    setHoverIdx(bucketAt(e.clientX));
    if (!drag.current) return;
    const a = pxToTime(drag.current.startX);
    const b = pxToTime(e.clientX);
    setDragSel({ from: Math.min(a, b), to: Math.max(a, b) });
  };

  // Zoom the window down to a single bucket's [start, nextStart) slice.
  const zoomIntoBucket = (idx: number) => {
    if (idx < 0 || idx >= buckets.length) return;
    const start = new Date(buckets[idx].start).getTime();
    const end = idx + 1 < buckets.length ? new Date(buckets[idx + 1].start).getTime() : to;
    if (end > start) onZoom({ from: start, to: end });
  };

  const handlePointerUp = (e: React.PointerEvent) => {
    const moved = drag.current ? Math.abs(e.clientX - drag.current.startX) : 0;
    if (drag.current && dragSel && moved >= 4) {
      onZoom(dragSel);
    } else if (moved < 4) {
      // A click (not a drag) focuses the bucket under the cursor.
      const idx = bucketAt(e.clientX);
      if (idx !== null) zoomIntoBucket(idx);
    }
    drag.current = null;
    setDragSel(null);
  };

  // Mouse-wheel: scroll up zooms in toward the cursor, scroll down steps back out.
  const handleWheelZoomIn = useCallback((clientX: number) => {
    const rect = ref.current?.getBoundingClientRect();
    if (!rect) return;
    const xVb = ((clientX - rect.left) / rect.width) * w;
    const cursorT = xToTime(xVb, from, to, geo.plotL, geo.plotR);
    const next = zoomTowardPivot(cursorT, from, to, WHEEL_ZOOM_FACTOR);
    if (next.to - next.from >= MIN_ZOOM_SPAN_MS) onZoom(next);
  }, [from, to, geo.plotL, geo.plotR, w, onZoom, ref]);

  useWheelZoom(ref, handleWheelZoomIn, onZoomOut);

  const selX1 = dragSel ? timeToX(dragSel.from, from, to, geo.plotL, geo.plotR) : 0;
  const selX2 = dragSel ? timeToX(dragSel.to, from, to, geo.plotL, geo.plotR) : 0;
  const hoverBucket = hoverIdx !== null ? buckets[hoverIdx] : null;
  const hoverPt = hoverIdx !== null ? geo.points[hoverIdx] : null;

  return (
    <div
      ref={ref}
      data-testid="traces-timeline"
      className="relative w-full shrink-0 select-none cursor-crosshair border border-border bg-card"
      onPointerDown={handlePointerDown}
      onPointerMove={handlePointerMove}
      onPointerUp={handlePointerUp}
      onPointerLeave={() => setHoverIdx(null)}
      title={
        canZoomOut
          ? t`Scroll or drag to zoom in · click a bucket to focus it · scroll down to zoom out`
          : t`Scroll or drag to zoom in · click a bucket to focus it`
      }
    >
      <svg viewBox={`0 0 ${w} ${height}`} width="100%" height={height} className="block">
        {/* Faint vertical guides spanning both lanes, aligned to the interior axis ticks. */}
        {ticks.slice(1, -1).map((tick, i) => (
          <line key={`g${i}`} x1={tick.x} x2={tick.x} y1={geo.plotT} y2={geo.plotB} stroke="var(--border-subtle)" />
        ))}
        {/* Volume: a flat body under a smooth curve through the bucket counts. */}
        <path d={geo.totalArea} fill="var(--accent-primary)" fillOpacity={0.2} />
        <path
          d={geo.totalLine}
          fill="none"
          stroke="var(--accent-primary)"
          strokeWidth="1.5"
          strokeLinecap="round"
          strokeLinejoin="round"
        />
        {/* Errors hang below the shared zero line as solid notches, on a scale of their own. The
            lane is meaningless unlabelled, so it names itself — inside the one band the volume
            series never reaches, and under the notches so data always wins the overlap. */}
        <text
          x={geo.plotL + 4}
          y={(geo.baselineY + geo.plotB) / 2 + 3}
          fill="var(--text-muted)"
          fontSize="9"
          fontFamily="JetBrains Mono, monospace"
          letterSpacing="0.6"
        >
          {t`Errors`}
        </text>
        {geo.errorAreas.map((d, i) => (
          <path key={`e${i}`} d={d} fill="var(--danger)" fillOpacity={0.7} />
        ))}
        <line x1={geo.plotL} x2={geo.plotR} y1={geo.baselineY} y2={geo.baselineY} stroke="var(--border-color)" />
        {dragSel && (
          <rect
            x={selX1} y={geo.plotT} width={Math.max(selX2 - selX1, 1)} height={geo.plotB - geo.plotT}
            fill="var(--accent-primary)" opacity={0.12} stroke="var(--accent-primary)" strokeOpacity={0.5}
          />
        )}
        {hoverPt && !dragSel && (
          <g>
            {/* Playhead: one rule crossing both lanes, so the two series read as one instant. */}
            <line
              x1={hoverPt.x} x2={hoverPt.x} y1={geo.plotT} y2={geo.plotB}
              stroke="var(--accent-primary)" strokeOpacity={0.45}
            />
            <circle cx={hoverPt.x} cy={hoverPt.totalY} r="3" fill="var(--accent-primary)" />
            <circle cx={hoverPt.x} cy={hoverPt.totalY} r="1.25" fill="var(--bg-card)" />
          </g>
        )}
        {/* Time axis: a short tick + label under the plot. */}
        {ticks.map((tick, i) => (
          <g key={`t${i}`}>
            <line x1={tick.x} x2={tick.x} y1={geo.plotB} y2={geo.plotB + 3} stroke="var(--border-color)" />
            <text
              x={tick.x}
              y={height - 4}
              textAnchor={tick.anchor}
              fill="var(--text-muted)"
              fontSize="9"
              fontFamily="JetBrains Mono, monospace"
            >
              {tick.label}
            </text>
          </g>
        ))}
      </svg>
      {buckets.length === 0 && (
        <div className="pointer-events-none absolute inset-0 flex items-center justify-center text-body-sm text-muted">
          {canZoomOut
            ? <Trans>No traces in this range · scroll down to zoom out</Trans>
            : <Trans>No traces in this range</Trans>}
        </div>
      )}
      {hoverBucket && (
        <div className="pointer-events-none absolute top-1 left-1 bg-card px-2 py-1 font-mono text-caption tabular-nums text-secondary">
          {formatBucketInstant(new Date(hoverBucket.start).getTime(), to - from)} · <Plural value={hoverBucket.total} one="# trace" other="# traces" />
          {hoverBucket.errors > 0 && <span className="text-danger"> · <Trans>{hoverBucket.errors} err</Trans></span>}
        </div>
      )}
    </div>
  );
}
