import { useMemo, useState } from 'react';
import { computeAreaChart } from './chart-math';
import { ChartTooltip } from './ChartTooltip';
import { useElementWidth } from '../../hooks/useElementWidth';

interface AreaChartProps {
  data: number[];
  width?: number;
  height?: number;
  color: string;
  padding?: { l?: number; r?: number; t?: number; b?: number };
  showAxis?: boolean;
  xLabelFn?: (i: number, n: number) => string | null;
  formatValue?: (v: number) => string;
  tooltipLabelFn?: (i: number) => string;
  /** Animate the line path drawing in on mount via `.chart-draw-in`. Default false. */
  drawIn?: boolean;
}

export function AreaChart({
  data,
  width = 820,
  height = 240,
  color,
  padding,
  showAxis = true,
  xLabelFn,
  formatValue,
  tooltipLabelFn,
  drawIn = false,
}: AreaChartProps) {
  const padL = padding?.l ?? (showAxis ? 38 : 4);
  const padR = padding?.r ?? (showAxis ? 10 : 4);
  const padT = padding?.t ?? (showAxis ? 14 : 4);
  const padB = padding?.b ?? (showAxis ? 24 : 4);

  const [wrapRef, measuredWidth] = useElementWidth<HTMLDivElement>(width);
  const w = measuredWidth || width;

  const chart = useMemo(
    () => computeAreaChart(data, w, height, padL, padR, padT, padB, showAxis, xLabelFn),
    [data, w, height, padL, padR, padT, padB, showAxis, xLabelFn],
  );

  const [hoverIdx, setHoverIdx] = useState<number | null>(null);

  if (data.length < 2) return null;

  const handleMove = (e: React.MouseEvent<HTMLDivElement>) => {
    const rect = wrapRef.current?.getBoundingClientRect();
    if (!rect) return;
    const xPx = e.clientX - rect.left;
    const stepX = (w - padL - padR) / (data.length - 1);
    const idx = Math.max(0, Math.min(data.length - 1, Math.round((xPx - padL) / stepX)));
    setHoverIdx(idx);
  };

  const hoverPt = hoverIdx !== null ? chart.pts[hoverIdx] : null;
  const fmt = formatValue ?? ((v: number) => String(v));

  return (
    <div
      ref={wrapRef}
      className="relative"
      onMouseMove={handleMove}
      onMouseLeave={() => setHoverIdx(null)}
    >
      <svg
        viewBox={`0 0 ${w} ${height}`}
        width="100%"
        height={height}
        className="block overflow-visible"
      >
        {showAxis && (
          <>
            <path d={chart.solidGridPath} stroke="var(--border-color)" strokeWidth="1" fill="none" />
            <path d={chart.dashedGridPath} stroke="var(--border-color)" strokeWidth="1" strokeDasharray="3 4" fill="none" />
            {chart.grid.map((g, i) => (
              <text key={i} x={padL - 8} y={g.y + 4} textAnchor="end" fill="var(--text-muted)" fontSize="var(--text-caption)" fontFamily="JetBrains Mono, monospace">{g.val}</text>
            ))}
          </>
        )}
        {/* Flat semi-transparent fill of the series color — reads as a body under the line
            without the dimensional fade-to-transparent gradient. */}
        <path d={chart.areaPath} fill={color} fillOpacity={0.18} />
        <path
          d={chart.linePath}
          fill="none"
          stroke={color}
          strokeWidth="2"
          strokeLinecap="round"
          strokeLinejoin="round"
          className={drawIn ? 'chart-draw-in' : undefined}
        />
        {hoverPt && (
          <>
            <line
              x1={hoverPt.x} x2={hoverPt.x}
              y1={chart.plotT} y2={chart.plotB}
              stroke={color} strokeOpacity="0.4" strokeWidth="1" strokeDasharray="3 3"
              vectorEffect="non-scaling-stroke"
            />
            <circle cx={hoverPt.x} cy={hoverPt.y} r="6" fill={color} opacity="0.2" />
            <circle cx={hoverPt.x} cy={hoverPt.y} r="3.5" fill={color} />
            <circle cx={hoverPt.x} cy={hoverPt.y} r="1.5" fill="var(--bg-card)" />
          </>
        )}
        {showAxis && chart.xLabels.map((l, i) => (
          <text key={i} x={l.x} y={height - 6} textAnchor="middle" fill="var(--text-muted)" fontSize="var(--text-caption)" fontFamily="JetBrains Mono, monospace">{l.label}</text>
        ))}
      </svg>
      {hoverPt && hoverIdx !== null && (
        <ChartTooltip
          leftPct={(hoverPt.x / w) * 100}
          topPct={(hoverPt.y / height) * 100}
          label={tooltipLabelFn ? tooltipLabelFn(hoverIdx) : undefined}
          value={fmt(hoverPt.v)}
          color={color}
        />
      )}
    </div>
  );
}
