import { api, qs, type RequestOptions } from './client';
import type { AgentCallDto, AgentCallListItemDto, AgentCallFilter, AgentCallSummaryDto, PagedResult, SynthesizeTestCasesRequest, TestCaseProposalSetDto, TracesOverviewDto, TraceHistogramBucket } from './models';

export const agentCallsApi = {
  list: (filter?: AgentCallFilter) =>
    api.get<PagedResult<AgentCallListItemDto>>(`/api/agent-calls${qs((filter ?? {}) as Record<string, unknown>)}`),
  /** Full (fat) trace list — same filters, complete request/response/tools per item. For bulk
   * full-data flows only (suite-creation test-case building, playground replay); the traces table
   * uses {@link list}. */
  listFull: (filter?: AgentCallFilter) =>
    api.get<PagedResult<AgentCallDto>>(`/api/agent-calls/full${qs((filter ?? {}) as Record<string, unknown>)}`),
  overview: (params?: { projectId?: string; agentId?: string; from?: string }) =>
    api.get<TracesOverviewDto>(`/api/agent-calls/overview${qs(params ?? {})}`),
  /** Aggregate over every trace matching the filter — backs the traces KPI band. Unpaged by design:
   * the list scrolls, so the band describes the whole filtered set rather than a slice. */
  summary: (filter?: AgentCallFilter) =>
    api.get<AgentCallSummaryDto>(`/api/agent-calls/summary${qs((filter ?? {}) as Record<string, unknown>)}`),
  histogram: (filter: AgentCallFilter & { buckets?: number }) =>
    api.get<TraceHistogramBucket[]>(`/api/agent-calls/histogram${qs(filter as Record<string, unknown>)}`),
  /** Distinct tool names requested by any trace in the project — backs the tool filter's picker.
   * When `agentId` is given (an agent filter is active), the list is scoped to that agent's traces. */
  toolNames: (projectId: string, agentId?: string) =>
    api.get<string[]>(`/api/agent-calls/tool-names${qs({ projectId, agentId })}`),
  get: (id: string, opts?: RequestOptions) => api.get<AgentCallDto>(`/api/agent-calls/${id}`, opts),
  /** Agent-proposed test cases for this trace's whole conversation. Read-only; writes nothing. */
  proposeTestCases: (id: string, body: SynthesizeTestCasesRequest, opts?: RequestOptions) =>
    api.post<TestCaseProposalSetDto>(`/api/agent-calls/${id}/test-case-proposals`, body, opts),
  delete: (id: string) => api.del(`/api/agent-calls/${id}`),
};
