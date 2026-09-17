import { useMemo, useState } from 'react';
import { computeStackedBar, type StackedDatum, type StackedRect } from './chart-math';
import { ChartTooltip } from './ChartTooltip';
import { useElementWidth } from '../../hooks/useElementWidth';

interface StackedBarProps {
  data: StackedDatum[];
  width?: number;
  height?: number;
  formatValue?: (v: number) => string;
  /** Axis-tick formatter (raw tick value). Defaults to the `k`-suffixed thousands format. */
  formatAxisTick?: (v: number) => string;
  /** Snap the y-axis to whole-number ticks — for count series (see {@link computeStackedBar}). */
  integerTicks?: boolean;
}

export function StackedBar({ data, width = 640, height = 200, formatValue, formatAxisTick, integerTicks = false }: StackedBarProps) {
  const [ref, measuredWidth] = useElementWidth<HTMLDivElement>(width);
  const w = measuredWidth || width;
  const chart = useMemo(
    () => computeStackedBar(data, w, height, formatAxisTick, integerTicks),
    [data, w, height, formatAxisTick, integerTicks],
  );
  const [hover, setHover] = useState<StackedRect | null>(null);

  const handleMove = (e: React.MouseEvent<HTMLDivElement>) => {
    const box = ref.current?.getBoundingClientRect();
    if (!box || chart.bars.length === 0) return;
    const xVb = ((e.clientX - box.left) / box.width) * w;
    const yVb = ((e.clientY - box.top) / box.height) * height;
    let best: typeof chart.bars[number] | null = null, bestDist = Infinity;
    for (const bar of chart.bars) {
      const dist = Math.abs(bar.centerX - xVb);
      if (dist < bestDist) { bestDist = dist; best = bar; }
    }
    if (!best) return;
    const seg = best.rects.find(r => yVb >= r.y && yVb <= r.y + r.h) ?? best.rects[0] ?? null;
    setHover(seg);
  };

  const fmt = formatValue ?? ((v: number) => String(v));

  return (
    <div ref={ref} className="relative" onMouseMove={handleMove} onMouseLeave={() => setHover(null)}>
      <svg viewBox={`0 0 ${w} ${height}`} width="100%" height={height} className="block">
        <path d={chart.solidGridPath} stroke="var(--border-color)" strokeWidth="1" fill="none" />
        <path d={chart.dashedGridPath} stroke="var(--border-color)" strokeWidth="1" strokeDasharray="3 4" fill="none" />
        {chart.grid.map((g, i) => (
          <text key={i} x={chart.plotL - 8} y={g.y + 4} textAnchor="end" fill="var(--text-muted)" fontSize="var(--text-caption)" fontFamily="JetBrains Mono, monospace">{g.val}</text>
        ))}
        {chart.bars.map((bar, i) => (
          <g key={i}>
            {/* Square bars — the topmost segment is no longer rounded. */}
            {bar.rects.map((r, j) => {
              const dim = hover !== null && hover !== r;
              return <rect key={j} x={r.x} y={r.y} width={r.w} height={r.h} fill={r.color} opacity={dim ? 0.5 : 1} />;
            })}
            {i % chart.labelStep === 0 && (
              <text x={bar.centerX} y={height - 10} textAnchor="middle" fill="var(--text-muted)" fontSize="var(--text-caption)" fontFamily="JetBrains Mono, monospace">{bar.label}</text>
            )}
          </g>
        ))}
      </svg>
      {hover && (
        <ChartTooltip
          leftPct={((hover.x + hover.w / 2) / w) * 100}
          topPct={(hover.y / height) * 100}
          label={hover.label}
          value={fmt(hover.value)}
          color={hover.color}
        />
      )}
    </div>
  );
}
