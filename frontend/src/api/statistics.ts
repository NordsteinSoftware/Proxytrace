import { api, qs, type RequestOptions } from './client';
import type {
  AgentDistributionsDto,
  AgentOverviewDto,
  AgentPassRatePointDto,
  AgentSuitePassRateDto,
  AgentTimeSeriesPointDto,
  CostEstimateDto,
  DashboardViewDto,
  ErrorRateDto,
  PassRateDto,
} from './models';
import type { StatisticsBucket } from '../lib/time-range';

type AgentRangeParams = { from: string; to: string; bucket: StatisticsBucket; [key: string]: string };

export const statisticsApi = {
  /** One-shot dashboard payload (replaces the per-widget request fan-out). */
  dashboard: (params?: { from?: string; to?: string; projectId?: string; scopeId?: string; excludeSystemAgents?: boolean; recentTraceCount?: number }) =>
    api.get<DashboardViewDto>(`/api/statistics/dashboard${qs(params ?? {})}`),
  passRates: (params?: { from?: string; to?: string; agentId?: string; projectId?: string; endpointId?: string }) =>
    api.get<PassRateDto[]>(`/api/statistics/pass-rates${qs(params ?? {})}`),
  errorRates: (params?: { from?: string; to?: string; agentId?: string; projectId?: string; endpointId?: string }) =>
    api.get<ErrorRateDto[]>(`/api/statistics/error-rates${qs(params ?? {})}`),
  costEstimate: (params?: { from?: string; to?: string; agentId?: string; projectId?: string; endpointId?: string }) =>
    api.get<CostEstimateDto[]>(`/api/statistics/cost-estimate${qs(params ?? {})}`),

  agentOverview: (agentId: string, params: AgentRangeParams, opts?: RequestOptions) =>
    api.get<AgentOverviewDto>(`/api/statistics/agents/${agentId}/overview${qs(params)}`, opts),
  agentDistributions: (agentId: string, params: { from: string; to: string }, opts?: RequestOptions) =>
    api.get<AgentDistributionsDto>(`/api/statistics/agents/${agentId}/distributions${qs(params)}`, opts),
  agentTimeSeries: (agentId: string, params: AgentRangeParams) =>
    api.get<AgentTimeSeriesPointDto[]>(`/api/statistics/agents/${agentId}/time-series${qs(params)}`),
  agentPassRateTrend: (agentId: string, params: AgentRangeParams) =>
    api.get<AgentPassRatePointDto[]>(`/api/statistics/agents/${agentId}/pass-rate-trend${qs(params)}`),
  agentSuitePassRates: (agentId: string) =>
    api.get<AgentSuitePassRateDto[]>(`/api/statistics/agents/${agentId}/suite-pass-rates`),
};
