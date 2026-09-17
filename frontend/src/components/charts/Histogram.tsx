import { useMemo, useState } from 'react';
import { computeHistogram } from './chart-math';
import { ChartTooltip } from './ChartTooltip';
import { useElementWidth } from '../../hooks/useElementWidth';

interface HistogramProps {
  data: number[];
  width?: number;
  height?: number;
  color: string;
  labels?: string[];
  formatValue?: (v: number) => string;
}

export function Histogram({
  data,
  width = 360,
  height = 200,
  color,
  labels,
  formatValue,
}: HistogramProps) {
  const [ref, measuredWidth] = useElementWidth<HTMLDivElement>(width);
  const w = measuredWidth || width;
  const hist = useMemo(() => computeHistogram(data, w, height, labels), [data, w, height, labels]);
  const [hoverIdx, setHoverIdx] = useState<number | null>(null);

  const handleMove = (e: React.MouseEvent<HTMLDivElement>) => {
    const rect = ref.current?.getBoundingClientRect();
    if (!rect || hist.rects.length === 0) return;
    const xVb = ((e.clientX - rect.left) / rect.width) * w;
    let best = 0, bestDist = Infinity;
    hist.rects.forEach((r, i) => {
      const dist = Math.abs(r.x + r.w / 2 - xVb);
      if (dist < bestDist) { bestDist = dist; best = i; }
    });
    setHoverIdx(best);
  };

  const hoverRect = hoverIdx !== null ? hist.rects[hoverIdx] : null;
  const fmt = formatValue ?? ((v: number) => String(v));

  return (
    <div ref={ref} className="relative" onMouseMove={handleMove} onMouseLeave={() => setHoverIdx(null)}>
      <svg viewBox={`0 0 ${w} ${height}`} width="100%" height={height} className="block">
        <line x1="38" x2={w - 10} y1={hist.baselineY} y2={hist.baselineY} stroke="var(--border-color)" />
        <path d={hist.barsPath} fill={color} opacity="0.85" />
        {hoverRect && hoverRect.h > 0 && (
          <rect x={hoverRect.x} y={hoverRect.y} width={hoverRect.w} height={hoverRect.h} fill={color} />
        )}
        {hist.rects.map((r, i) => (
          <text key={i} x={r.labelX} y={height - 8} textAnchor="middle" fill="var(--text-muted)" fontSize="var(--text-caption)" fontFamily="JetBrains Mono, monospace">{r.label}</text>
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
