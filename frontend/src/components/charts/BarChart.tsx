import { useMemo, useState } from 'react';
import { computeModelBars } from './chart-math';
import { ChartTooltip } from './ChartTooltip';
import { useElementWidth } from '../../hooks/useElementWidth';

interface BarChartProps {
  data: { label: string; value: number }[];
  width?: number;
  height?: number;
  color: string;
  truncateAt?: number;
  formatValue?: (v: number) => string;
}

export function BarChart({
  data,
  width = 820,
  height = 220,
  color,
  truncateAt = 10,
  formatValue,
}: BarChartProps) {
  const [wrapRef, measuredWidth] = useElementWidth<HTMLDivElement>(width);
  const w = measuredWidth || width;
  const bars = useMemo(() => computeModelBars(data, w, height, truncateAt), [data, w, height, truncateAt]);
  const [hoverIdx, setHoverIdx] = useState<number | null>(null);

  if (bars.rects.length === 0) return null;

  const handleMove = (e: React.MouseEvent<HTMLDivElement>) => {
    const rect = wrapRef.current?.getBoundingClientRect();
    if (!rect) return;
    const xVb = e.clientX - rect.left;
    let bestIdx = 0;
    let bestDist = Infinity;
    bars.rects.forEach((r, i) => {
      const cx = r.x + r.w / 2;
      const dist = Math.abs(cx - xVb);
      if (dist < bestDist) { bestDist = dist; bestIdx = i; }
    });
    setHoverIdx(bestIdx);
  };

  const hoverRect = hoverIdx !== null ? bars.rects[hoverIdx] : null;
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
        className="block"
      >
        <line x1="38" x2={w - 10} y1={bars.baselineY} y2={bars.baselineY} stroke="var(--border-color)" />
        <path d={bars.barsPath} fill={color} opacity="0.85" />
        {hoverRect && (
          <rect x={hoverRect.x} y={hoverRect.y} width={hoverRect.w} height={hoverRect.h} fill={color} />
        )}
        {bars.rects.map((r, i) => (
          <text key={i} x={r.labelX} y={bars.baselineY + 14} textAnchor="middle" fill="var(--text-muted)" fontSize="var(--text-caption)" fontFamily="JetBrains Mono, monospace">{r.label}</text>
        ))}
      </svg>
      {hoverRect && (
        <ChartTooltip
          leftPct={((hoverRect.x + hoverRect.w / 2) / w) * 100}
          topPct={(hoverRect.y / height) * 100}
          label={hoverRect.fullLabel}
          value={fmt(hoverRect.value)}
          color={color}
        />
      )}
    </div>
  );
}
