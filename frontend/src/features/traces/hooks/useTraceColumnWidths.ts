import type { CSSProperties } from 'react';
import { useLocalStorageState } from '../../../hooks/useLocalStorageState';
import { COL_MIN_WIDTHS, COL_MOBILE_VISIBLE, COL_WIDTHS } from '../tracesMeta';

const DEFAULT_WIDTHS = COL_WIDTHS.map(() => null);
const MAX_WIDTH = 2000;

export function useTraceColumnWidths() {
  const [stored, setStored] = useLocalStorageState<unknown>('traces.columnWidths', DEFAULT_WIDTHS);
  const widths = COL_WIDTHS.map((_, i) => {
    const width: unknown = Array.isArray(stored) ? stored[i] : null;
    return typeof width === 'number' && Number.isFinite(width)
      ? Math.max(COL_MIN_WIDTHS[i], Math.min(MAX_WIDTH, width))
      : null;
  });
  const tracks = widths.map((width, i) => width === null ? COL_WIDTHS[i] : `${width}px`);
  const minimums = widths.map((width, i) => width ?? COL_MIN_WIDTHS[i]);

  return {
    style: {
      '--trace-grid': tracks.join(' '),
      '--trace-grid-narrow': tracks.filter((_, i) => COL_MOBILE_VISIBLE[i]).join(' '),
      '--trace-min-width': `${32 + minimums.reduce((sum, width) => sum + width, 0)}px`,
      '--trace-min-width-narrow': `${32 + minimums.reduce((sum, width, i) => sum + (COL_MOBILE_VISIBLE[i] ? width : 0), 0)}px`,
    } as CSSProperties,
    resizeColumn: (index: number, width: number, measuredWidths: number[]) => {
      if (!Number.isFinite(width)) return;
      setStored(widths.map((current, i) => i === index
        ? Math.max(COL_MIN_WIDTHS[i], Math.min(MAX_WIDTH, Math.round(width)))
        : measuredWidths[i] > 0 ? measuredWidths[i] : current));
    },
  };
}
