import { useState } from 'react';
import type { AgentCallListItemDto, AgentCallSummaryDto } from '../../../api/models';

export interface TraceStatsSelection {
  ids: ReadonlySet<string>;
  onChange: (traces: AgentCallListItemDto[], checked: boolean) => void;
}

/** Selection belongs to the current query; scrolling and live updates keep it intact. */
export function useTraceSelection(traces: AgentCallListItemDto[], scope: string) {
  const [selection, setSelection] = useState({ scope, ids: new Set<string>() });
  if (selection.scope !== scope) setSelection({ scope, ids: new Set() });

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
