import type { StatisticsBucket } from '../lib/time-range';

export enum TestRunStatus {
  Pending = 'Pending',
  Running = 'Running',
  Completed = 'Completed',
  Failed = 'Failed',
  Cancelled = 'Cancelled',
}

export enum EvaluationScore { Terrible = 'Terrible', Bad = 'Bad', Acceptable = 'Acceptable', Good = 'Good', Excellent = 'Excellent' }

export enum EvaluatorKind {
  Agentic = 'Agentic',
  ExactMatch = 'ExactMatch',
  NumericMatch = 'NumericMatch',
  JsonSchemaMatch = 'JsonSchemaMatch',
}

export interface AgenticEvaluatorPresetDto {
  key: string;
  name: string;
  systemPrompt: string;
}

export enum ModelProviderKind {
  Unknown = 'Unknown',
  OpenAi = 'OpenAi',
  OpenAiCompatible = 'OpenAiCompatible',
}

export interface PagedResult<T> {
  items: T[];
  total: number;
  page: number;
  pageSize: number;
}

/* ── Agent Calls ── */
export interface ToolRequestDto {
  id: string;
  name: string;
  arguments: string;
}
export interface MessageDto {
  role: string;
  content: string;
  toolRequests: ToolRequestDto[];
  toolCallId: string | null;
}
export interface ToolArgumentDto {
  name: string;
  description: string | null;
  type: string;
  isRequired: boolean;
  enumValues: string[] | null;
}
export interface ToolSpecDto {
  name: string;
  description: string;
  arguments: ToolArgumentDto[];
}
export interface ModelParametersDto {
  temperature: number | null;
  topP: number | null;
  reasoningEffort: string | null;
  frequencyPenalty: number | null;
  presencePenalty: number | null;
  maxTokens: number | null;
  seed: number | null;
  stop: string[] | null;
  n: number | null;
}
export interface AgentCallDto {
  id: string;
  agentId: string | null;
  agentName: string | null;
  model: string;
  provider: string;
  request: MessageDto[];
  /** Null when the captured call produced no completion (HTTP error, empty/dropped completion). */
  response: MessageDto | null;
  tools: ToolSpecDto[];
  inputTokens: number;
  outputTokens: number;
  /** Cached subset of {@link inputTokens} (cached ≤ input), served from the provider cache. */
  cachedInputTokens: number;
  durationMs: number;
  httpStatus: number;
  finishReason: string | null;
  errorMessage: string | null;
  costEur: number | null;
  modelParameters: ModelParametersDto;
  createdAt: string;
  updatedAt: string;
  conversationId: string | null;
  sessionId: string | null;
  /** Outlier characteristics flagged at ingestion (bitmask; 0 = not an outlier). See {@link lib/outliers}. */
  outlierFlags: number;
}

/** Lightweight agent-call projection for the traces table / dashboard live stream. Carries row
 * fields plus a precomputed first-user-message preview and response tool-request count; the full
 * {@link AgentCallDto} (request, response, tools, model parameters) is fetched per-selection via
 * `GET /api/agent-calls/{id}`. */
export interface AgentCallListItemDto {
  id: string;
  agentId: string | null;
  agentName: string | null;
  model: string;
  provider: string;
  /** First user message in the request, whitespace-collapsed; null when none. */
  messagePreview: string | null;
  /** Number of tool requests in the response. */
  toolCount: number;
  inputTokens: number;
  outputTokens: number;
  /** Cached subset of {@link inputTokens}. */
  cachedInputTokens: number;
  durationMs: number;
  httpStatus: number;
  finishReason: string | null;
  errorMessage: string | null;
  costEur: number | null;
  createdAt: string;
  updatedAt: string;
  conversationId: string | null;
  sessionId: string | null;
  /** Outlier characteristics flagged at ingestion (bitmask; 0 = not an outlier). See {@link lib/outliers}. */
  outlierFlags: number;
}

/* ── Agents ── */
export interface AgentDto {
  id: string;
  projectId: string;
  projectName: string;
  name: string;
  systemMessage: string;
  tools: ToolSpecDto[];
  endpointId: string;
  endpointName: string;
  modelParameters: ModelParametersDto;
  isSystemAgent: boolean;
  createdAt: string;
  updatedAt: string;
  lastUsedAt: string | null;
}

/** Lightweight agent projection for lists (agents grid, dashboard, traces filter). Row fields + a
 * tool count; the full {@link AgentDto} (system message, tool specs, model parameters) is fetched
 * per-selection via `GET /api/agents/{id}`. */
export interface AgentListItemDto {
  id: string;
  projectId: string;
  projectName: string;
  name: string;
  toolCount: number;
  endpointId: string;
  endpointName: string;
  isSystemAgent: boolean;
  createdAt: string;
  updatedAt: string;
  lastUsedAt: string | null;
}

export interface AgentVersionDto {
  id: string;
  agentId: string;
  versionNumber: number;
  systemMessage: string;
  tools: ToolSpecDto[];
  fingerprint: string;
  createdAt: string;
}

/* ── Sessions ── */
export interface SessionDto {
  id: string;
  projectId: string;
  externalKey: string;
  createdAt: string;
  lastActivityAt: string;
  traceCount: number;
  totalTokens: number;
}

/* ── Statistics ── */
/** Filter-bar metadata for the Traces page (agents, per-agent counts, latency). */
export interface TracesOverviewDto {
  agents: AgentListItemDto[];
  agentBreakdown: AgentBreakdownDto[];
  latency: LatencyStatDto[];
}
/**
 * One bucketed per-agent anomaly cell for the anomaly-dashboard timeline. The series is **sparse**
 * (only non-empty cells are returned, sorted by `bucketStart` then `agentId`): `staticCount` is
 * calls flagged by the statistical ingestion-time outlier detector, `customCount` calls flagged by
 * a custom detector (a call carrying both counts in both). See `GET /api/statistics/anomalies/timeline`.
 */
export interface AgentAnomalyStatDto {
  bucketStart: string;
  agentId: string;
  staticCount: number;
  customCount: number;
}

/** A custom detector's anomalous verdict on a call — attribution for the recent-anomalies list. */
export interface CustomAnomalyHitDto {
  detectorId: string;
  detectorName: string;
  matchedTrigger: string;
  reasoning: string | null;
}

/**
 * One recent-anomalies row: the flagged call in the traces list-item shape plus its
 * custom-detector attributions (empty for purely statistical outliers).
 */
export interface AnomalyListItemDto {
  call: AgentCallListItemDto;
  customAnomalies: CustomAnomalyHitDto[];
}

/* ── Custom anomaly detectors ── */
/** How a trigger pattern is matched against a conversation turn. Mirrors backend `TriggerKind`. */
export enum TriggerKind { Phrase = 'Phrase', Regex = 'Regex' }

/** One trigger gating a detector's LLM review: a phrase (case-insensitive substring) or a regex. */
export interface AnomalyTriggerDto {
  kind: TriggerKind;
  pattern: string;
}

/** A user-defined LLM-based anomaly detector. The list endpoint returns the full object (no separate
 * detail fetch) — it carries the review instructions, judge endpoint, triggers and agent scope. */
export interface CustomAnomalyDetectorDto {
  id: string;
  name: string;
  /** LLM review instructions (the hidden judge agent's system prompt). */
  instructions: string;
  projectId: string;
  endpointId: string;
  endpointName: string;
  triggers: AnomalyTriggerDto[];
  /** When true the detector reviews every agent's calls; otherwise only {@link agentIds}. */
  allAgents: boolean;
  agentIds: string[];
  isEnabled: boolean;
  /** When true the proxy rejects trigger-matching requests before they reach the provider. */
  blockUpstream: boolean;
  createdAt: string;
  updatedAt: string;
}

export interface CreateCustomAnomalyDetectorRequest {
  projectId: string;
  name: string;
  instructions: string;
  endpointId: string;
  triggers: AnomalyTriggerDto[];
  allAgents: boolean;
  agentIds?: string[];
  isEnabled: boolean;
  blockUpstream: boolean;
}

export interface UpdateCustomAnomalyDetectorRequest {
  name: string;
  instructions: string;
  /** Null/omitted keeps the detector's current judge endpoint. */
  endpointId?: string;
  triggers: AnomalyTriggerDto[];
  allAgents: boolean;
  agentIds?: string[];
  isEnabled: boolean;
  blockUpstream: boolean;
}
/** Single-call dashboard payload bundling every widget's data. */
export interface DashboardViewDto {
  summary: SummaryDto;
  liveTelemetry: LiveTelemetryDto;
  trends: DashboardTrendsDto;
  agentBreakdown: AgentBreakdownDto[];
  latency: LatencyStatDto[];
  modelBreakdown: ModelBreakdownDto[];
  tokenUsage: TokenUsageDto[];
  tokenUsageByAgent: AgentTokenUsageDto[];
  /** Bucket granularity used for the token series (backend-resolved; drives the chart's time axis). */
  tokenBucket: StatisticsBucket;
  recentTraces: AgentCallListItemDto[];
  agents: AgentListItemDto[];
  /** Per-minute call counts over the trailing hour (60 entries, oldest → newest). */
  pulse: number[];
}
export interface SummaryDto {
  totalCalls: number;
  totalInputTokens: number;
  totalOutputTokens: number;
  totalCachedInputTokens: number;
  avgLatencyMs: number;
  overallPassRate: number | null;
}
export interface ModelBreakdownDto {
  endpointId: string;
  modelName: string;
  callCount: number;
  totalInputTokens: number;
  totalOutputTokens: number;
  totalCachedInputTokens: number;
  avgDurationMs: number;
}
export interface AgentBreakdownDto {
  agentId: string;
  callCount: number;
}
export interface LiveTelemetryDto {
  tracesPerMinute: number;
  tokensPerSecond: number;
  queueDepth: number;
  errorRate: number;
  p95Ms: number;
}
export interface AgentTokenUsageDto {
  bucketStart: string;
  agentId: string;
  inputTokens: number;
  outputTokens: number;
  cachedInputTokens: number;
}
export interface DashboardTrendsDto {
  traces: number[];
  latencyMs: number[];
  throughput: number[];
  passRate: number[];
}
export interface LatencyStatDto {
  endpointId: string;
  p50Ms: number;
  p95Ms: number;
  p99Ms: number;
  minMs: number;
  maxMs: number;
  sampleCount: number;
}
export interface TokenUsageDto {
  bucketStart: string;
  endPointId: string;
  inputTokens: number;
  outputTokens: number;
  cachedInputTokens: number;
}
export interface PassRateDto {
  suiteId: string;
  runTimestamp: string;
  passCount: number;
  failCount: number;
}
export interface ErrorRateDto {
  endpointId: string;
  totalCalls: number;
  errorCalls: number;
  errorRate: number;
}
export interface CostEstimateDto {
  endpointId: string;
  inputCostEur: number | null;
  outputCostEur: number | null;
  totalCostEur: number | null;
}

/* ── Agent Statistics ── */
export interface AgentTimeSeriesPointDto {
  bucketStart: string;
  traceCount: number;
  inputTokens: number;
  outputTokens: number;
  cachedInputTokens: number;
  costEur: number;
  avgLatencyMs: number;
}
export interface AgentPassRatePointDto {
  bucketStart: string;
  passed: number;
  testCases: number;
}
export interface AgentSuitePassRateDto {
  suiteId: string;
  suiteName: string;
  latestRunAt: string;
  passed: number;
  testCases: number;
}
export interface AgentEntityCountsDto {
  suiteCount: number;
  testCaseCount: number;
  openProposalCount: number;
  totalProposalCount: number;
}
export interface AgentTimeSummaryDto {
  totalTraces: number;
  totalInputTokens: number;
  totalOutputTokens: number;
  totalCachedInputTokens: number;
  totalCostEur: number;
  avgLatencyMs: number;
}
export interface AgentOverviewDto {
  summary: AgentTimeSummaryDto;
  timeSeries: AgentTimeSeriesPointDto[];
  passRateTrend: AgentPassRatePointDto[];
  suitePassRates: AgentSuitePassRateDto[];
  counts: AgentEntityCountsDto;
}
/** One equal-width histogram bin: the value range and how many samples fell in it. */
export interface HistogramBinDto {
  start: number;
  end: number;
  count: number;
}
/** Mean ± sample std-dev of a single metric, its min/max, and a histogram of the samples. */
export interface MetricDistributionDto {
  mean: number;
  stdDev: number;
  sampleCount: number;
  min: number;
  max: number;
  histogram: HistogramBinDto[];
}
/** Distribution of an agent's successful calls: tokens/latency per call, cost/cache/tools per conversation. */
export interface AgentDistributionsDto {
  inputTokensPerCall: MetricDistributionDto;
  outputTokensPerCall: MetricDistributionDto;
  latencyMsPerCall: MetricDistributionDto;
  costPerConversationEur: MetricDistributionDto;
  cacheHitRatePerConversation: MetricDistributionDto;
  toolCallsPerConversation: MetricDistributionDto;
}

/* ── Test Suites ── */
export interface ToolRequestInputDto { name: string; arguments: string; id?: string | null; }
export interface TestSuiteMessageDto {
  role: string;
  content: string;
  toolRequests?: ToolRequestInputDto[] | null;
  toolCallId?: string | null;
}
export interface TestCaseDto {
  id: string;
  input: TestSuiteMessageDto[];
  expectedOutput: TestSuiteMessageDto;
  /** The trace this case was promoted or corrected from, if any. Null for synthetic cases. */
  sourceAgentCallId?: string | null;
}
export interface SuiteEvaluatorDto { id: string; kind: EvaluatorKind; }
export interface TestSuiteDto {
  id: string;
  name: string;
  agentId: string;
  agentName: string;
  evaluators: SuiteEvaluatorDto[];
  testCases: TestCaseDto[];
  description: string | null;
  tags: string[];
  totalRuns: number;
  passRate: number | null;
  prevPassRate: number | null;
  passRateTrend: number[];
  lastRunAt: string | null;
  lastRunGroupId: string | null;
  createdAt: string;
  updatedAt: string;
}

/** Lightweight suite projection for the suites grid. Keeps evaluator refs + run aggregates but
 * replaces the fat `testCases` list with a `testCaseCount`; the full {@link TestSuiteDto} is fetched
 * per-selection via `GET /api/test-suites/{id}`. */
export interface TestSuiteListItemDto {
  id: string;
  name: string;
  agentId: string;
  agentName: string;
  evaluators: SuiteEvaluatorDto[];
  testCaseCount: number;
  description: string | null;
  tags: string[];
  totalRuns: number;
  passRate: number | null;
  prevPassRate: number | null;
  passRateTrend: number[];
  lastRunAt: string | null;
  lastRunGroupId: string | null;
  createdAt: string;
  updatedAt: string;
}

/** Aggregated run stats for a suite over a time window ("bucket"). See GET /api/test-suites/{id}/run-stats. */
export interface SuiteRunStatsDto {
  runCount: number;
  passRate: number | null;
  avgDurationMs: number | null;
  totalCost: number | null;
}

/* ── Test Runs ── */
export interface EvaluatorSummaryDto {
  totalEvaluations: number;
  avgScore: number | null;
  overallPassRate: number | null;
  inputTokens: number | null;
  outputTokens: number | null;
  cachedInputTokens: number | null;
  totalCost: number | null;
  avgLatencyMs: number | null;
}

export interface EvaluatorCostPointDto {
  bucketStart: string;
  inputTokens: number;
  outputTokens: number;
  cachedInputTokens: number;
  cost: number;
  avgLatencyMs: number;
}

export interface EvaluatorPassRatePointDto {
  bucketStart: string;
  passed: number;
  total: number;
}

export interface EvaluatorScoreBucketDto {
  score: EvaluationScore;
  count: number;
}

export interface EvaluatorOverviewDto {
  summary: EvaluatorSummaryDto;
  passRateTrend: EvaluatorPassRatePointDto[];
  scoreDistribution: EvaluatorScoreBucketDto[];
  costTrend: EvaluatorCostPointDto[];
}

export interface EvaluatorSparklineDto {
  evaluatorId: string;
  points: EvaluatorPassRatePointDto[];
}

export interface EvaluationResultDto {
  evaluatorId: string;
  evaluatorKind: EvaluatorKind;
  evaluatorName: string;
  score: EvaluationScore | null;
  reasoning: string | null;
  errorMessage: string | null;
}
export interface TestResultDto {
  id: string;
  testCaseId: string;
  testCaseSummary: string;
  actualResponse: string;
  evaluations: EvaluationResultDto[];
  durationMs: number;
  /** Per-case cost/token usage, endpoint-priced. Null when the provider reported no usage; optional
   * because lightweight client-built results (Tracey, fixtures) may omit it. Summing these across a
   * run's results yields the run-level totals live. */
  costEur?: number | null;
  tokensIn?: number | null;
  tokensOut?: number | null;
  cachedTokensIn?: number | null;
}
export interface TestCaseRowDto { id: string; summary: string; }
export interface RunEvaluatorDto { id: string; kind: EvaluatorKind; name: string; }
export interface TestRunDto {
  id: string;
  groupId: string;
  suiteId: string | null;
  suiteName: string | null;
  agentId: string;
  agentName: string;
  endpointId: string;
  endpointName: string;
  /** Zero-based index of this run within its endpoint's sample cohort (0 for single-sample runs). */
  sampleIndex: number;
  status: TestRunStatus;
  totalCases: number;
  passedCases: number;
  failedCases: number;
  passRate: number;
  /** Run-level totals aggregated across every test result (null until completed). */
  costEur: number | null;
  tokensIn: number | null;
  tokensOut: number | null;
  cachedTokensIn: number | null;
  evaluators: RunEvaluatorDto[];
  startedAt: string;
  completedAt: string | null;
  /** Average per-case model inference latency over the run's results (aggregated inference latency,
   * not a wall-clock run timer). Null until the run has at least one result. */
  durationMs: number | null;
  testCases: TestCaseRowDto[];
  results: TestResultDto[];
  createdAt: string;
  updatedAt: string;
}
export interface TestRunGroupDto {
  id: string;
  suiteId: string;
  suiteName: string;
  agentId: string;
  agentName: string;
  status: TestRunStatus;
  /** True for ephemeral A/B validation runs; hidden from the runs list unless explicitly shown. */
  isSystemRun: boolean;
  /** Samples run per endpoint (1..5). Endpoints with >1 run form a cohort averaged in the UI. */
  sampleCount: number;
  completedAt: string | null;
  runs: TestRunDto[];
  createdAt: string;
  updatedAt: string;
}

/** Lightweight per-run projection for the run-group list cards (no per-case results/evaluations). */
export interface TestRunSummaryDto {
  id: string;
  endpointId: string;
  endpointName: string;
  /** Zero-based index of this run within its endpoint's sample cohort (0 for single-sample runs). */
  sampleIndex: number;
  status: TestRunStatus;
  totalCases: number;
  passedCases: number;
  failedCases: number;
  passRate: number;
}

/** Lightweight run-group projection for the runs list. The full {@link TestRunGroupDto} (with nested
 * per-case results) is fetched per-selection via `GET /api/test-run-groups/{id}`. */
export interface TestRunGroupListItemDto {
  id: string;
  suiteId: string;
  suiteName: string;
  agentId: string;
  agentName: string;
  status: TestRunStatus;
  isSystemRun: boolean;
  /** Samples run per endpoint (1..5). Endpoints with >1 run form a cohort averaged in the UI. */
  sampleCount: number;
  completedAt: string | null;
  runs: TestRunSummaryDto[];
  createdAt: string;
  updatedAt: string;
}

/* ── Test-run schedules ── */
/** A model endpoint referenced by a schedule (id + display name only). */
export interface ScheduleEndpointDto {
  id: string;
  name: string;
}

/** A periodic test-run schedule. `recentRuns` are the light run-group rows the schedule has produced. */
export interface TestRunScheduleDto {
  id: string;
  name: string;
  suiteId: string;
  suiteName: string;
  agentId: string;
  agentName: string;
  endpoints: ScheduleEndpointDto[];
  intervalMinutes: number;
  isEnabled: boolean;
  /** Recurrence phase: the schedule fires at `anchorAt + k·interval`. Drives the time-of-day. */
  anchorAt: string;
  nextRunAt: string;
  lastRunAt: string | null;
  recentRuns: TestRunGroupListItemDto[];
  createdAt: string;
  updatedAt: string;
}

export interface CreateTestRunScheduleRequest {
  name: string;
  testSuiteId: string;
  modelEndpointIds: string[];
  intervalMinutes: number;
  enabled: boolean;
  /** ISO instant the recurrence is phased to (the run time). Omitted → server anchors to "now". */
  anchorAt?: string;
}

export interface UpdateTestRunScheduleRequest {
  name: string;
  modelEndpointIds: string[];
  intervalMinutes: number;
  enabled: boolean;
  /** ISO instant the recurrence is phased to (the run time). Omitted → keeps the current anchor. */
  anchorAt?: string;
}

/* ── Evaluators ── */
/** Lightweight evaluator projection for pickers / select lists (id, kind, name only). */
export interface EvaluatorListItemDto {
  id: string;
  kind: EvaluatorKind;
  name: string;
}

export interface EvaluatorDetailDto {
  id: string;
  kind: EvaluatorKind;
  name: string;
  systemMessage: string | null;
  projectId: string;
  projectName: string;
  endpointId: string | null;
  endpointName: string | null;
  agentId: string | null;
  jsonSchema: string | null;
  extractionPattern: string | null;
  tolerance: number | null;
  createdAt: string;
  updatedAt: string;
}
export type CreateEvaluatorPayload =
  | { kind: EvaluatorKind.Agentic; projectId: string; name: string; systemMessage: string }
  | { kind: EvaluatorKind.ExactMatch; projectId: string }
  | { kind: EvaluatorKind.NumericMatch; projectId: string; extractionPattern: string; tolerance: number }
  | { kind: EvaluatorKind.JsonSchemaMatch; projectId: string; jsonSchema: string };

export type UpdateEvaluatorPayload =
  | { kind: EvaluatorKind.Agentic; name?: string; systemMessage?: string }
  | { kind: EvaluatorKind.ExactMatch }
  | { kind: EvaluatorKind.NumericMatch; extractionPattern?: string; tolerance?: number }
  | { kind: EvaluatorKind.JsonSchemaMatch; jsonSchema?: string };

/* ── Providers ── */
export interface ProviderDto {
  id: string;
  name: string;
  endpoint: string;
  upstreamApiKey: string;
  kind: ModelProviderKind;
  createdAt: string;
  updatedAt: string;
}
/** A provider with its model endpoints and API keys embedded. */
export interface ProviderWithDetailsDto {
  provider: ProviderDto;
  models: ModelEndpointDto[];
  keys: ApiKeyDto[];
}
/** Single-call payload for the Providers page. */
export interface ProvidersOverviewDto {
  providers: ProviderWithDetailsDto[];
  projects: ProjectDto[];
}
export interface ModelEndpointDto {
  id: string;
  modelName: string;
  providerId: string;
  providerName: string;
  inputTokenCost: number | null;
  outputTokenCost: number | null;
  /** Cached-input price (EUR / 1M tokens); auto-fetched from the LiteLLM catalog, read-only. */
  cachedInputTokenCost: number | null;
  createdAt: string;
  updatedAt: string;
}
export type ApiKeyScope = 'Ingestion' | 'McpRead' | 'McpWrite' | 'ApiRead' | 'ApiWrite';
export interface ApiKeyDto {
  id: string;
  name: string;
  /** Non-secret leading slice of the key, for identifying it in lists. */
  keyPrefix: string;
  /** The full key, returned only in the response to creating it (hashed at rest; never shown again). */
  plaintextKey?: string | null;
  projectId: string;
  projectName: string;
  providerId: string;
  providerName: string;
  scopes: ApiKeyScope[];
  ownerId: string;
  ownerEmail: string;
  createdAt: string;
}
export interface ProjectMemberDto {
  id: string;
  email: string;
}
export interface ProjectDto {
  id: string;
  name: string;
  systemEndpointId: string;
  members: ProjectMemberDto[];
  createdAt: string;
  updatedAt: string;
}

/** Lightweight project projection for the projects list / app-wide selector. Member list replaced by
 * a count; full members come from the detail fetch `GET /api/projects/{id}`. */
export interface ProjectListItemDto {
  id: string;
  name: string;
  systemEndpointId: string;
  memberCount: number;
  createdAt: string;
  updatedAt: string;
}
export type UserRole = 'Member' | 'Admin';

export interface UserDto {
  id: string;
  email: string;
  role: UserRole;
  /** True for OIDC-provisioned users (no local password); false for local-auth users. */
  isExternal: boolean;
  createdAt: string;
  updatedAt: string;
  /** Whether the user has confirmed TOTP two-factor authentication. */
  mfaEnabled: boolean;
}

/** Lightweight project reference for the user-centric project assignment editor. */
export interface UserProjectDto {
  id: string;
  name: string;
}
export interface CreateModelEndpointRequest {
  modelName: string;
  inputTokenCost: number | null;
  outputTokenCost: number | null;
}
export interface UpdateModelEndpointPricingRequest {
  inputTokenCost: number | null;
  outputTokenCost: number | null;
}
export interface CreateProviderRequest {
  name: string;
  endpoint: string;
  upstreamApiKey: string;
  kind: ModelProviderKind;
}
export interface CreateApiKeyRequest { name: string; projectId: string; scopes?: ApiKeyScope[]; userId?: string; }

/* ── Fixture ── */
export interface TestCaseMessageFixtureDto {
  role: string;
  content: string;
  toolRequests?: ToolRequestDto[] | null;
  toolCallId?: string | null;
}
export interface ToolCallInfoDto { name: string; arguments: unknown; }
export interface OutputValueDto {
  kind: 'message' | 'tool_call';
  content?: string | null;
  tool?: ToolCallInfoDto | null;
  name?: string | null;
  arguments?: unknown;
}
export interface RequestToolCallDto { id: string; name: string; arguments: string; }
export interface RequestMessageDto {
  role: string;
  content: string | null;
  toolCalls: RequestToolCallDto[];
  toolCallId: string | null;
}
export interface RequestToolDto { name: string; description: string; jsonSchema: unknown; }
export interface ModelRequestPreviewDto {
  model: string;
  messages: RequestMessageDto[];
  tools: RequestToolDto[];
}

export interface BreakdownItemDto { k: string; v: string; match: boolean; }
export interface EvaluatorFixtureResultDto {
  evaluatorId: string;
  evaluatorKind: string;
  evaluatorName: string;
  desc?: string | null;
  score: number;
  pass: boolean;
  breakdown: BreakdownItemDto[];
  note: string;
}
export interface RuntimeBreakdownDto {
  total: number;
  ttft: number;
  gen: number;
  tools: number;
  judge?: number | null;
}
export interface EndpointUsageDto {
  id: string;
  label: string;
  color: string;
  region: string;
  pricingIn: number;
  pricingOut: number;
  tokIn: number;
  tokOut: number;
  cachedTokIn: number;
  calls: number;
  latency: number;
  costEur: number;
}
export interface TestCaseFixtureDto {
  input: { messages: TestCaseMessageFixtureDto[] };
  expected: OutputValueDto;
  actual: OutputValueDto;
  evaluators: EvaluatorFixtureResultDto[];
  runtime: RuntimeBreakdownDto;
  endpoints: EndpointUsageDto[];
}

export interface TraceHistogramBucket {
  start: string;
  total: number;
  errors: number;
}

/* ── Filters ── */
export interface AgentCallFilter {
  projectId?: string;
  agentId?: string;
  model?: string;
  provider?: string;
  from?: string;
  to?: string;
  httpStatus?: number;
  includeSystemAgents?: boolean;
  q?: string;
  conversationId?: string;
  sessionId?: string;
  /** When true, return only calls flagged as outliers (any {@link AgentCallListItemDto.outlierFlags} bit set). */
  outlierOnly?: boolean;
  /** OutlierFlags bitmask; matches calls with ANY of the requested anomaly bits set. */
  anomalyFlags?: number;
  /** Status class (2/4/5) matching the whole 2xx/4xx/5xx range, unlike the exact {@link httpStatus}. */
  httpStatusClass?: number;
  /** Only calls whose response requested this tool (exact name). */
  toolName?: string;
  minTokens?: number;
  maxTokens?: number;
  minLatencyMs?: number;
  maxLatencyMs?: number;
  /** Backend AgentCallSortField enum member name; omitted = createdAt. */
  sortBy?: string;
  /** Sort direction; omitted = descending. */
  sortDesc?: boolean;
  page?: number;
  pageSize?: number;
}

/* ── Optimization ── */
export enum ProposalKind { SystemPrompt = 'SystemPrompt', Tool = 'Tool', ModelSwitch = 'ModelSwitch' }
export enum ProposalStatus { Draft = 'Draft', Accepted = 'Accepted', Rejected = 'Rejected', Adopted = 'Adopted' }
export enum Priority { Low = 'Low', Medium = 'Medium', High = 'High', Critical = 'Critical' }

export interface ModelSwitchDetailsDto {
  kind: 'ModelSwitch';
  endpointId: string;
  currentModelName: string;
  proposedModelName: string;
  expectedCostDelta: number | null;
  expectedLatencyMs: number | null;
}

export interface SystemPromptDetailsDto {
  kind: 'SystemPrompt';
  currentSystemMessage: string;
  proposedSystemMessage: string;
}

export interface ToolDetailsDto {
  kind: 'Tool';
  currentTools: ToolSpecDto[];
  proposedTools: ToolSpecDto[];
}

export type ProposalDetailsDto = ModelSwitchDetailsDto | SystemPromptDetailsDto | ToolDetailsDto;

export interface AbTestRunSummaryDto {
  id: string;
  groupId: string;
  status: TestRunStatus;
  totalCases: number;
  completedCases: number;
  passedCases: number;
  failedCases: number;
  passRate: number;
  startedAt: string;
  completedAt: string | null;
  /** Average per-case model inference latency over the run's results (aggregated inference latency,
   * not a wall-clock run timer). Null until the run has at least one result. */
  durationMs: number | null;
}

export interface OptimizationProposalDto {
  id: string;
  kind: ProposalKind;
  status: ProposalStatus;
  agentId: string;
  agentName: string;
  priority: Priority;
  rationale: string;
  details: ProposalDetailsDto;
  evidenceTestRunIds: string[];
  abTestRun: AbTestRunSummaryDto | null;
  currentPassRate: number | null;
  proposedPassRate: number | null;
  expectedPassRateDelta: number | null;
  adoptedAt: string | null;
  adoptedAgentVersionId: string | null;
  adoptedAgentVersionNumber: number | null;
  adoptedManually: boolean | null;
  createdAt: string;
  updatedAt: string;
}

export interface ProposalCreatedEvent {
  type: 'proposal-created';
  id: string;
  agentId: string;
  kind: ProposalKind;
  priority: Priority;
  rationale: string;
  createdAt: string;
}

export interface ProposalStatusChangedEvent {
  type: 'proposal-status-changed';
  id: string;
  agentId: string;
  kind: ProposalKind;
  status: ProposalStatus;
  adoptedAt: string | null;
  adoptedAgentVersionId: string | null;
  adoptedAgentVersionNumber: number | null;
  adoptedManually: boolean | null;
  updatedAt: string;
}

export type ProposalEvent = ProposalCreatedEvent | ProposalStatusChangedEvent;

/** Machine-readable handoff package from GET /api/proposals/{id}/artifact. */
export interface ProposalArtifactDto {
  schemaVersion: number;
  proposalId: string;
  kind: ProposalKind;
  status: ProposalStatus;
  generatedAt: string;
  agent: { id: string; name: string };
  priority: Priority;
  rationale: string;
  change: ProposalDetailsDto;
  evidence: {
    currentPassRate: number | null;
    proposedPassRate: number | null;
    expectedPassRateDelta: number | null;
    evidenceTestRunIds: string[];
    abTestRun: AbTestRunSummaryDto | null;
  };
  adoption: {
    adoptedAt: string | null;
    adoptedAgentVersionId: string | null;
    adoptedAgentVersionNumber: number | null;
    adoptedManually: boolean | null;
  };
}

export enum TheoryStatus { Proposed = 'Proposed', Validating = 'Validating', Validated = 'Validated', Invalidated = 'Invalidated', Failed = 'Failed' }
export enum TheorySource { Optimizer = 'Optimizer', User = 'User', TraceyAi = 'TraceyAi', External = 'External' }

export interface TheoryDto {
  id: string;
  kind: ProposalKind;
  status: TheoryStatus;
  source: TheorySource;
  agentId: string;
  agentName: string;
  suiteId: string;
  priority: Priority;
  rationale: string;
  details: ProposalDetailsDto;
  evidenceTestRunIds: string[];
  resultingProposalId: string | null;
  baselinePassRate: number | null;
  projectedPassRate: number | null;
  pValue: number | null;
  /** Candidate A/B run id, recorded once validated/invalidated. Null until then. */
  abTestRunId: string | null;
  createdAt: string;
  updatedAt: string;
}

/** Seed-style proposed-change payloads accepted by POST /api/theories. */
export type SubmitTheoryDetails =
  | { kind: 'SystemPrompt'; currentSystemMessage: string; proposedSystemMessage: string }
  | { kind: 'ModelSwitchSeed'; proposedEndpointId: string }
  | { kind: 'ToolUpdateSeed'; proposedTools: { name: string; description: string; parametersJson: string | null }[] };

export interface SubmitTheoryRequest {
  agentId: string;
  suiteId: string;
  priority: Priority;
  rationale: string;
  source: TheorySource;
  details: SubmitTheoryDetails;
}

export interface TheoryStatusChangedEvent {
  type: 'theory-changed';
  id: string;
  agentId: string;
  kind: ProposalKind;
  status: TheoryStatus;
  source: TheorySource;
  priority: Priority;
  rationale: string;
  resultingProposalId: string | null;
  updatedAt: string;
}

/* ── SSE Events ── */
export interface TraceCreatedEvent {
  id: string;
  agentId: string;
  projectId: string;
  agentName: string;
  model: string;
  provider: string;
  createdAt: string;
  conversationId: string | null;
  sessionId: string | null;
}
/** Emitted on `GET /api/anomalies/stream` when a custom detector flags a call (event `anomaly-flagged`). */
export interface AnomalyFlaggedEvent {
  agentCallId: string;
  agentId: string;
  projectId: string;
  detectorId: string;
  detectorName: string;
  /** True when the proxy rejected the call before it reached the provider (blocking detector). */
  blocked: boolean;
}
export interface TestCaseStartedEvent { type: 'test-case-started'; runId: string; groupId: string; testCaseId: string; }
export interface InferenceDoneEvent { type: 'inference-done'; runId: string; groupId: string; testCaseId: string; }
export interface EvaluationArrivedEvent { type: 'evaluation-arrived'; runId: string; groupId: string; testCaseId: string; evaluation: EvaluationResultDto; }
export interface TestResultArrivedEvent { type: 'test-result-arrived'; runId: string; groupId: string; testCaseId: string; overallScore: EvaluationScore | null; evaluations: EvaluationResultDto[]; durationMs: number; costEur?: number | null; tokensIn?: number | null; tokensOut?: number | null; cachedTokensIn?: number | null; }
export interface RunCompleteEvent { type: 'run-complete'; runId: string; groupId: string; status: TestRunStatus; completedAt: string | null; }
export interface GroupRunCompleteEvent { type: 'group-run-complete'; runId: string; groupId: string; groupStatus: TestRunStatus; groupCompletedAt: string | null; }
export type TestRunEvent =
  | TestCaseStartedEvent
  | InferenceDoneEvent
  | EvaluationArrivedEvent
  | TestResultArrivedEvent
  | RunCompleteEvent
  | GroupRunCompleteEvent;

/* ── Notifications ── */
export enum NotificationKind { Anomaly = 'Anomaly', ProposalReady = 'ProposalReady' }
export enum NotificationSeverity { Info = 'Info', Warning = 'Warning', Critical = 'Critical' }
export enum NotificationStatus { Unread = 'Unread', Read = 'Read', Dismissed = 'Dismissed' }
export enum NotificationTargetKind {
  TestRunGroup = 'TestRunGroup',
  Agent = 'Agent',
  OptimizationProposal = 'OptimizationProposal',
  AgentCall = 'AgentCall',
}

export interface NotificationDto {
  id: string;
  kind: NotificationKind;
  severity: NotificationSeverity;
  title: string;
  message: string;
  status: NotificationStatus;
  projectId: string | null;
  targetKind: NotificationTargetKind | null;
  targetId: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface NotificationCreatedEvent {
  type: 'notification-created';
  id: string;
  projectId: string | null;
  kind: NotificationKind;
  severity: NotificationSeverity;
  title: string;
  message: string;
  status: NotificationStatus;
  targetKind: NotificationTargetKind | null;
  targetId: string | null;
  createdAt: string;
}

export interface NotificationStatusChangedEvent {
  type: 'notification-status-changed';
  id: string;
  projectId: string | null;
  status: NotificationStatus;
  updatedAt: string;
}

export type NotificationEvent = NotificationCreatedEvent | NotificationStatusChangedEvent;

/* ── Application Errors (Error Log) ── */
export enum ApplicationErrorLevel {
  Error = 'Error',
  Critical = 'Critical',
}

export interface ApplicationErrorDto {
  id: string;
  message: string;
  level: ApplicationErrorLevel;
  category: string;
  exceptionType: string | null;
  stackTrace: string | null;
  createdAt: string;
}

export interface ApplicationErrorFilter {
  page: number;
  pageSize: number;
  level?: ApplicationErrorLevel;
  search?: string;
  /** Inclusive lower time bound (ISO 8601). Errors at or after this instant. */
  from?: string;
  /** Inclusive upper time bound (ISO 8601). Errors at or before this instant. */
  to?: string;
}

/* ── Audit Log ── */
export enum AuditAction {
  TestRunStarted = 'TestRunStarted',
  ApiKeyMinted = 'ApiKeyMinted',
  ApiKeyDeleted = 'ApiKeyDeleted',
  ProjectDeleted = 'ProjectDeleted',
  ProjectMemberAdded = 'ProjectMemberAdded',
  ProjectMemberRemoved = 'ProjectMemberRemoved',
  LicenseSet = 'LicenseSet',
  LicenseRemoved = 'LicenseRemoved',
  TestSuiteDeleted = 'TestSuiteDeleted',
  EvaluatorDeleted = 'EvaluatorDeleted',
  TestCaseDeleted = 'TestCaseDeleted',
  ProviderConfigCreated = 'ProviderConfigCreated',
  ProviderConfigUpdated = 'ProviderConfigUpdated',
  EndpointConfigCreated = 'EndpointConfigCreated',
  EndpointConfigUpdated = 'EndpointConfigUpdated',
  ProviderConfigDeleted = 'ProviderConfigDeleted',
  EndpointConfigDeleted = 'EndpointConfigDeleted',
  UserRoleChanged = 'UserRoleChanged',
  UserDeleted = 'UserDeleted',
  AgentDeleted = 'AgentDeleted',
  ProjectCreated = 'ProjectCreated',
  ProjectRenamed = 'ProjectRenamed',
  AgentEndpointChanged = 'AgentEndpointChanged',
  EvaluatorCreated = 'EvaluatorCreated',
  EvaluatorUpdated = 'EvaluatorUpdated',
  TestSuiteCreated = 'TestSuiteCreated',
  TestSuiteUpdated = 'TestSuiteUpdated',
  TestCaseCreated = 'TestCaseCreated',
  TestCaseUpdated = 'TestCaseUpdated',
  UserInvited = 'UserInvited',
  InviteRevoked = 'InviteRevoked',
  UserSignedUp = 'UserSignedUp',
  UserLoggedIn = 'UserLoggedIn',
  LoginFailed = 'LoginFailed',
  UserLoggedOut = 'UserLoggedOut',
  AdminBootstrapped = 'AdminBootstrapped',
  LegacyAccountClaimed = 'LegacyAccountClaimed',
  ProposalStatusChanged = 'ProposalStatusChanged',
  EmailSettingsUpdated = 'EmailSettingsUpdated',
  PasswordResetRequested = 'PasswordResetRequested',
  PasswordResetCompleted = 'PasswordResetCompleted',
  PasswordResetLinkIssued = 'PasswordResetLinkIssued',
  TheorySubmitted = 'TheorySubmitted',
  TheoryReset = 'TheoryReset',
  TheoryRejected = 'TheoryRejected',
  TheoryValidated = 'TheoryValidated',
  TheoryInvalidated = 'TheoryInvalidated',
  TheoryValidationFailed = 'TheoryValidationFailed',
  ProposalGenerated = 'ProposalGenerated',
  ProposalAutoAdopted = 'ProposalAutoAdopted',
  TestRunGroupOptimizeRequested = 'TestRunGroupOptimizeRequested',
  TestRunGroupCancelled = 'TestRunGroupCancelled',
  TestRunGroupDeleted = 'TestRunGroupDeleted',
  TestRunDeleted = 'TestRunDeleted',
  TestRunScheduleCreated = 'TestRunScheduleCreated',
  TestRunScheduleUpdated = 'TestRunScheduleUpdated',
  TestRunScheduleDeleted = 'TestRunScheduleDeleted',
  TestRunScheduleRunNow = 'TestRunScheduleRunNow',
  AgentCallDeleted = 'AgentCallDeleted',
  AgentVersionMoved = 'AgentVersionMoved',
  SetupCleanupPurged = 'SetupCleanupPurged',
  SecretsBackfilled = 'SecretsBackfilled',
  AccessDenied = 'AccessDenied',
  OutlierSettingsUpdated = 'OutlierSettingsUpdated',
  CustomAnomalyDetectorCreated = 'CustomAnomalyDetectorCreated',
  CustomAnomalyDetectorUpdated = 'CustomAnomalyDetectorUpdated',
  CustomAnomalyDetectorDeleted = 'CustomAnomalyDetectorDeleted',
  ProviderUpstreamKeyRotated = 'ProviderUpstreamKeyRotated',
}

export enum AuditActorType {
  User = 'User',
  ApiKey = 'ApiKey',
  System = 'System',
}

export enum AuditOutcome {
  Success = 'Success',
  Failure = 'Failure',
}

export interface AuditLogEntryDto {
  id: string;
  action: AuditAction;
  actorType: AuditActorType;
  actorUserId: string | null;
  actorEmail: string | null;
  actorApiKeyId: string | null;
  projectId: string | null;
  targetType: string;
  targetId: string | null;
  targetLabel: string | null;
  details: string | null;
  outcome: AuditOutcome;
  createdAt: string;
}

export interface AuditLogFilter {
  page: number;
  pageSize: number;
  action?: AuditAction;
  actor?: string;
  projectId?: string;
  targetType?: string;
  targetId?: string;
  /** Inclusive lower time bound (ISO 8601). */
  from?: string;
  /** Inclusive upper time bound (ISO 8601). */
  to?: string;
}
