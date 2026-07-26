import { api, qs, type RequestOptions } from './client';
import type { PagedResult, SuiteRunStatsDto, TestSuiteDto, TestSuiteListItemDto, TestSuiteMessageDto } from './models';

export interface CreateTestSuitePayload {
  name: string;
  agentId: string;
  agentCallIds: string[];
  evaluatorIds?: string[];
}

/**
 * One test case in a generic suite create: a plain promotion, or — when `expectedOutput` is set —
 * a CORRECTION, the trace's input paired with the answer the agent should have given.
 */
export interface CreateTestCasePayload {
  fromAgentCallId: string;
  expectedOutput?: TestSuiteMessageDto;
}

export interface CreateTestSuiteWithCasesPayload {
  name: string;
  agentId: string;
  testCases: CreateTestCasePayload[];
  evaluatorIds?: string[];
}

export const testSuitesApi = {
  list: (params?: { agentId?: string; projectId?: string; page?: number; pageSize?: number }) =>
    api.get<PagedResult<TestSuiteListItemDto>>(`/api/test-suites${qs(params ?? {})}`),
  get: (id: string, opts?: RequestOptions) => api.get<TestSuiteDto>(`/api/test-suites/${id}`, opts),
  runStats: (id: string, params?: { from?: string; to?: string }) =>
    api.get<SuiteRunStatsDto>(`/api/test-suites/${id}/run-stats${qs(params ?? {})}`),
  create: (payload: CreateTestSuitePayload) =>
    api.post<TestSuiteDto>('/api/test-suites/from-traces', payload),
  /**
   * Generic create. Unlike {@link create} (`/from-traces`), each case may carry its own
   * `expectedOutput` — a correction. `/from-traces` hard-codes the case's expected output to the
   * response the agent actually recorded, so it cannot express a test that fails until a fix lands.
   */
  createWithCases: (payload: CreateTestSuiteWithCasesPayload) =>
    api.post<TestSuiteDto>('/api/test-suites', payload),
  updateEvaluators: (id: string, evaluatorIds: string[]) =>
    api.put<TestSuiteDto>(`/api/test-suites/${id}`, { evaluatorIds }),
  delete: (id: string) => api.del(`/api/test-suites/${id}`),
  addTestCase: (suiteId: string, fromAgentCallId: string, expectedOutput?: TestSuiteMessageDto) =>
    api.post<TestSuiteDto>(`/api/test-suites/${suiteId}/test-cases`, { fromAgentCallId, expectedOutput }),
  removeTestCase: (suiteId: string, caseId: string) =>
    api.del<TestSuiteDto>(`/api/test-suites/${suiteId}/test-cases/${caseId}`),
};
