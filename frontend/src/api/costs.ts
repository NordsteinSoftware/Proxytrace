import { api, qs } from './client';
import type { StatisticsBucket } from '../lib/time-range';

/**
 * One configured monthly budget. Mirrors backend `CostLimitDto`. At most one of `agentId` /
 * `apiKeyId` is set — both null is the project-wide budget.
 */
export interface CostLimitDto {
  id: string;
  projectId: string;
  /** Null unless this is an agent override. */
  agentId: string | null;
  agentName: string | null;
  /** Null unless this is an API key override. */
  apiKeyId: string | null;
  apiKeyName: string | null;
  softLimitEur: number | null;
  hardLimitEur: number | null;
  enabled: boolean;
  createdAt: string;
  updatedAt: string;
}

export interface CreateCostLimitRequest {
  projectId: string;
  agentId: string | null;
  apiKeyId?: string | null;
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

/**
 * Derived spend of one inbound API key in one time bucket. A null `apiKeyId` is the unattributed
 * series — traffic authenticated with the provider's own upstream key, and traces ingested before
 * key attribution existed.
 */
export interface ApiKeyCostPointDto {
  bucketStart: string;
  apiKeyId: string | null;
  costEur: number;
}

/** Window spend attributed to one inbound API key; null `apiKeyId` is the unattributed remainder. */
export interface ApiKeyCostTotalDto {
  apiKeyId: string | null;
  apiKeyName: string | null;
  keyPrefix: string | null;
  costEur: number;
}

/**
 * A budget joined with this month's spend and breach state — the payload of
 * `GET /api/cost-limits/status`. Deliberately not part of the cost overview: a budget change
 * invalidates this list, and re-reading it costs one or two aggregate scans instead of the
 * overview's seven.
 */
export interface CostBudgetStatusDto {
  costLimitId: string;
  agentId: string | null;
  agentName: string | null;
  apiKeyId: string | null;
  apiKeyName: string | null;
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
  apiKeySeries: ApiKeyCostPointDto[];
  apiKeyTotals: ApiKeyCostTotalDto[];
  /**
   * True when some traffic in the window ran on an endpoint with no configured price. Those calls
   * contribute nothing to any figure here, so the numbers are an *incomplete* estimate.
   */
  hasUnpricedEndpoints: boolean;
  /**
   * The granularity the series was actually aggregated at — the requested bucket, coarsened
   * server-side when the window would produce more cells than the chart draws. Densify and label
   * against this, never against what was asked for.
   */
  bucket: StatisticsBucket;
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
    status: (projectId: string) =>
      api.get<CostBudgetStatusDto[]>(`/api/cost-limits/status${qs({ projectId })}`),
    create: (body: CreateCostLimitRequest) => api.post<CostLimitDto>('/api/cost-limits', body),
    update: (id: string, body: UpdateCostLimitRequest) =>
      api.put<CostLimitDto>(`/api/cost-limits/${id}`, body),
    remove: (id: string) => api.del<void>(`/api/cost-limits/${id}`),
  },
};
