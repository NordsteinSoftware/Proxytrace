import { useEffect, useState } from 'react';
import type { AgentCallListItemDto, AgentCallSummaryDto } from '../../../api/models';

export interface TraceStatsSelection {
  ids: ReadonlySet<string>;
  onChange: (traces: AgentCallListItemDto[], checked: boolean) => void;
}

/** Selection belongs to the current filter scope and survives navigation away and back. */
export function useTraceSelection(traces: AgentCallListItemDto[], scope: string) {
  const [selection, setSelection] = useState(() => {
    try {
      const saved = JSON.parse(localStorage.getItem('traces.selection') ?? 'null');
      if (saved?.scope === scope && Array.isArray(saved.ids) && saved.ids.every((id: unknown) => typeof id === 'string')) {
        return { scope, ids: new Set<string>(saved.ids) };
      }
    } catch { /* Storage unavailable or invalid — start with no selection. */ }
    return { scope, ids: new Set<string>() };
  });
  if (selection.scope !== scope) setSelection({ scope, ids: new Set() });

  useEffect(() => {
    try {
      localStorage.setItem('traces.selection', JSON.stringify({ scope: selection.scope, ids: [...selection.ids] }));
    } catch { /* Keep the in-memory selection when storage is unavailable. */ }
  }, [selection]);

  const selected = traces.filter(trace => selection.ids.has(trace.id));
  const count = selected.length;
  const avgLatencyMs = count ? selected.reduce((sum, trace) => sum + trace.durationMs, 0) / count : 0;
  const priced = selected.filter(trace => trace.costEur !== null);
  const summary: AgentCallSummaryDto | null = count ? {
    count,
    inputTokens: selected.reduce((sum, trace) => sum + trace.inputTokens, 0),
    outputTokens: selected.reduce((sum, trace) => sum + trace.outputTokens, 0),
    cachedInputTokens: selected.reduce((sum, trace) => sum + trace.cachedInputTokens, 0),
    totalCostEur: priced.length ? priced.reduce((sum, trace) => sum + (trace.costEur ?? 0), 0) : null,
    avgLatencyMs,
    latencyStdDevMs: Math.sqrt(selected.reduce((sum, trace) => sum + (trace.durationMs - avgLatencyMs) ** 2, 0) / count),
    errorCount: selected.filter(trace => trace.httpStatus < 200 || trace.httpStatus >= 300).length,
  } : null;

  const onChange: TraceStatsSelection['onChange'] = (changed, checked) => {
    setSelection(previous => {
      const ids = new Set(previous.ids);
      for (const trace of changed) {
        if (checked) ids.add(trace.id);
        else ids.delete(trace.id);
      }
      return { scope, ids };
    });
  };

  return {
    selection: { ids: selection.ids, onChange },
    summary,
    clear: () => setSelection({ scope, ids: new Set() }),
  };
}
