import { api, qs } from './client';
import type { StatisticsBucket } from '../lib/time-range';

/** One configured monthly budget. Mirrors backend `CostLimitDto`. */
export interface CostLimitDto {
  id: string;
  projectId: string;
  /** Null for the project-wide budget; set for an agent override. */
  agentId: string | null;
  agentName: string | null;
  softLimitEur: number | null;
  hardLimitEur: number | null;
  enabled: boolean;
  createdAt: string;
  updatedAt: string;
}

export interface CreateCostLimitRequest {
  projectId: string;
  agentId: string | null;
  softLimitEur: number | null;
  hardLimitEur: number | null;
  enabled: boolean;
}

export interface UpdateCostLimitRequest {
  softLimitEur: number | null;
  hardLimitEur: number | null;
  enabled: boolean;
}

/** Derived spend of one agent in one time bucket. */
export interface AgentCostPointDto {
  bucketStart: string;
  agentId: string;
  costEur: number;
}

export interface AgentCostTotalDto {
  agentId: string;
  agentName: string;
  costEur: number;
}

/** A budget joined with this month's spend and breach state. */
export interface CostBudgetStatusDto {
  costLimitId: string;
  agentId: string | null;
  agentName: string | null;
  softLimitEur: number | null;
  hardLimitEur: number | null;
  enabled: boolean;
  monthToDateSpendEur: number;
  softBreached: boolean;
  /** True while the proxy is rejecting this scope's calls for the rest of the month. */
  hardBreached: boolean;
}

export interface CostOverviewDto {
  monthToDateSpendEur: number;
  previousMonthSpendEur: number;
  series: AgentCostPointDto[];
  agentTotals: AgentCostTotalDto[];
  budgets: CostBudgetStatusDto[];
  /**
   * True when some traffic in the window ran on an endpoint with no configured price. Those calls
   * contribute nothing to any figure here, so the numbers are an *incomplete* estimate.
   */
  hasUnpricedEndpoints: boolean;
  bucket: string;
}

export type CostOverviewParams = {
  projectId: string;
  from: string;
  to: string;
  bucket?: StatisticsBucket;
};

export const costsApi = {
  overview: (params: CostOverviewParams) =>
    api.get<CostOverviewDto>(`/api/statistics/cost-overview${qs(params as Record<string, unknown>)}`),
  limits: {
    list: (projectId: string) =>
      api.get<CostLimitDto[]>(`/api/cost-limits${qs({ projectId })}`),
    create: (body: CreateCostLimitRequest) => api.post<CostLimitDto>('/api/cost-limits', body),
    update: (id: string, body: UpdateCostLimitRequest) =>
      api.put<CostLimitDto>(`/api/cost-limits/${id}`, body),
    remove: (id: string) => api.del<void>(`/api/cost-limits/${id}`),
  },
};
