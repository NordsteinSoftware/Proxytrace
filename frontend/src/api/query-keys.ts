const TEST_RUN_GROUPS = 'test-run-groups';

export const QUERY_KEYS = {
  agents: (projectId?: string) => ['agents', projectId ?? null] as const,
  agent: (id: string | null) => ['agent', id ?? null] as const,
  agentVersions: (agentId: string) => ['agent', agentId, 'versions'] as const,
  agentVersion: (versionId: string) => ['agent-version', versionId] as const,
  agentCalls: (filter: object) => ['agent-calls', filter] as const,
  /** A single trace fetched by id (detail-panel deep-link). 'detail' segment lets list-cache invalidation exclude it. */
  agentCall: (id?: string) => ['agent-calls', 'detail', id ?? null] as const,
  /** Prefix matching every agent-calls query — use for invalidation. */
  agentCallsRoot: ['agent-calls'] as const,
  agentCallsOverview: (from?: string, agentId?: string, projectId?: string) => ['agent-calls', 'overview', from, agentId, projectId ?? null] as const,
  agentCallsHistogram: (filter: object) => ['agent-calls', 'histogram', filter] as const,
  agentCallToolNames: (projectId?: string, agentId?: string) => ['agent-calls', 'tool-names', projectId ?? null, agentId ?? null] as const,
  agentCallsForSuiteCreate: (agentId: string, from?: string) => ['agent-calls', 'suite-create', agentId, from ?? null] as const,
  agentCallsForSuiteEdit: (agentId?: string) => ['agent-calls', 'suite-edit', agentId] as const,

  statisticsDashboard: (from: string | undefined, projectId?: string) => ['statistics-dashboard', from ?? null, projectId ?? null] as const,
  statisticsPassRates: (from?: string, agentId?: string, projectId?: string) => ['statistics-pass-rates', from, agentId, projectId ?? null] as const,
  statisticsErrorRates: (from?: string, agentId?: string, projectId?: string) => ['statistics-error-rates', from, agentId, projectId ?? null] as const,
  statisticsCostEstimate: (from?: string, agentId?: string, projectId?: string) => ['statistics-cost-estimate', from, agentId, projectId ?? null] as const,

  agentStatsOverview: (agentId: string, rangeKey: string) => ['agent-stats-overview', agentId, rangeKey] as const,
  agentStatsDistributions: (agentId: string, rangeKey: string) => ['agent-stats-distributions', agentId, rangeKey] as const,
  agentSuitePassRates: (agentId: string) => ['agent-suite-pass-rates', agentId] as const,
  agentCounts: (agentId: string) => ['agent-counts', agentId] as const,

  evaluators: (projectId?: string) => ['evaluators', projectId ?? null] as const,
  evaluatorSummaries: (projectId?: string) => ['evaluators', 'summaries', projectId ?? null] as const,
  evaluatorsOverview: (projectId: string, rangeKey: string) => ['evaluators', 'overview', projectId, rangeKey] as const,
  evaluatorDetail: (evaluatorId: string, rangeKey: string) => ['evaluators', 'detail', evaluatorId, rangeKey] as const,
  evaluatorRecent: (evaluatorId: string, score: string, count: number) => ['evaluators', 'recent', evaluatorId, score, count] as const,
  agenticEvaluatorPresets: ['evaluators', 'agentic-presets'] as const,
  modelEndpoints: ['model-endpoints'] as const,

  license: ['license'] as const,
  updates: ['updates'] as const,
  health: ['health'] as const,
  authMode: ['auth-mode'] as const,
  appConfig: ['app-config'] as const,
  /** The current authenticated user (id, email, role, UI language). */
  me: ['me'] as const,
  setupStatus: ['setup-status'] as const,
  invites: ['invites'] as const,
  /** Public invite preview on the signup page, keyed by the invite token. */
  invitePreview: (token: string) => ['invite', token] as const,
  providers: ['providers'] as const,
  providersOverview: ['providers', 'overview'] as const,
  projects: ['projects'] as const,
  project: (id: string) => ['project', id] as const,
  users: ['users'] as const,
  /** Projects a single user is a member of (admin user-management editor). */
  userProjects: (id: string) => ['users', id, 'projects'] as const,
  providerAvailableModels: (providerId: string | null) => ['provider-available-models', providerId] as const,

  testRunGroups: (agentFilter?: string, projectId?: string, includeSystem?: boolean) =>
    [TEST_RUN_GROUPS, agentFilter, projectId ?? null, includeSystem ?? false] as const,
  /** Run groups for a single suite (suite detail's History tab). Shares the {@link TEST_RUN_GROUPS}
   * prefix so {@link testRunGroupsRoot} invalidation also refreshes a suite's history. */
  testRunGroupsBySuite: (suiteId: string, includeSystem?: boolean) =>
    [TEST_RUN_GROUPS, 'suite', suiteId, includeSystem ?? false] as const,
  /** A single test-run group — used by Tracey's live run-progress card. */
  testRunGroup: (id: string) => [TEST_RUN_GROUPS, 'detail', id] as const,
  /** Prefix matching every test-run-groups query — use for invalidation. */
  testRunGroupsRoot: [TEST_RUN_GROUPS] as const,
  testRunSchedules: (agentFilter?: string, projectId?: string) =>
    ['test-run-schedules', agentFilter, projectId ?? null] as const,
  /** Prefix matching every test-run-schedules query — use for invalidation. */
  testRunSchedulesRoot: ['test-run-schedules'] as const,
  testSuites: (agentFilter?: string, projectId?: string) => ['test-suites', agentFilter, projectId ?? null] as const,
  /** Prefix matching every test-suites query — use for invalidation. */
  testSuitesRoot: ['test-suites'] as const,
  /** A single (fat) test suite by id — full test cases for the edit dialog. 'detail' segment lets list invalidation cover it by prefix. */
  testSuite: (id: string) => ['test-suites', 'detail', id] as const,
  /** Bucket-windowed run stats for a suite. */
  testSuiteRunStats: (id: string, from?: string, to?: string) =>
    ['test-suites', 'detail', id, 'run-stats', from ?? null, to ?? null] as const,
  proposals: (agentId?: string, projectId?: string) => ['proposals', agentId, projectId ?? null] as const,
  /** A single proposal by id — the notification drawer's target preview. */
  proposal: (id: string) => ['proposals', 'detail', id] as const,
  theories: (agentId?: string, projectId?: string, status?: string) => ['theories', agentId, projectId ?? null, status ?? null] as const,
  theory: (id: string) => ['theory', id] as const,
  fixture: (runId: string, caseId: string) => ['fixture', runId, caseId] as const,
  fixtureRequest: (runId: string, caseId: string) => ['fixture-request', runId, caseId] as const,

  search: (projectId: string, q: string) => ['search', projectId, q] as const,
  searchRecent: (projectId: string, kinds: string[], limit: number) =>
    ['search-recent', projectId, [...kinds].sort().join(','), limit] as const,
  searchSettings: (projectId: string) => ['search-settings', projectId] as const,
  searchStatus: (projectId: string) => ['search-status', projectId] as const,

  evaluatorTestBench: (evaluatorId: string, testCaseId: string) =>
    ['evaluator-test-bench', evaluatorId, testCaseId] as const,
  evaluatorTestBenchDefault: (evaluatorId: string) =>
    ['evaluator-test-bench-default', evaluatorId] as const,
  evaluatorTestBenchRecent: (evaluatorId: string, count: number) =>
    ['evaluator-test-bench-recent', evaluatorId, count] as const,
  evaluatorTestBenchSearch: (evaluatorId: string, query: string, count: number) =>
    ['evaluator-test-bench-search', evaluatorId, query, count] as const,

  traceySession: (projectId?: string) => ['tracey-session', projectId ?? null] as const,

  /** Paged session list for a project. Page/size are part of the key so each page caches
   * separately (a project-only key would collide across pages). */
  sessions: (projectId: string, page?: number, pageSize?: number) =>
    ['sessions', projectId, page ?? null, pageSize ?? null] as const,
  session: (id: string) => ['session', id] as const,

  errorLog: (filter: object) => ['error-log', filter] as const,
  /** A single captured error by id (deep-link target from an error toast). */
  errorLogEntry: (id: string) => ['error-log', 'entry', id] as const,
  /** Prefix matching every error-log query — use for invalidation. */
  errorLogRoot: ['error-log'] as const,

  auditLog: (filter: object) => ['audit-log', filter] as const,
  auditLogEntry: (id: string) => ['audit-log', 'entry', id] as const,
  /** Prefix matching every audit-log query — use for invalidation. */
  auditLogRoot: ['audit-log'] as const,

  /** Operator email/SMTP settings (admin). */
  emailSettings: ['email-settings'] as const,

  /** Operator outlier-detection sensitivity (admin). */
  outlierSettings: ['outlier-settings'] as const,

  notifications: (projectId?: string) => ['notifications', projectId ?? null] as const,
  /** A single notification by id (the `?notification=` deep-link, e.g. from a notification email).
   * Shares the {@link notificationsRoot} prefix so a mark-read/dismiss also refreshes the drawer. */
  notification: (id?: string) => ['notifications', 'detail', id ?? null] as const,
  /** Prefix matching every notifications query — use for invalidation. */
  notificationsRoot: ['notifications'] as const,

  /** Bucketed per-agent anomaly timeline for the anomaly dashboard. */
  anomalyTimeline: (filter: object) => ['anomaly-timeline', filter] as const,
  /** Prefix matching every anomaly-timeline query — use for SSE invalidation. */
  anomalyTimelineRoot: ['anomaly-timeline'] as const,
  /** Paged recent flagged calls for the anomaly dashboard. */
  anomaliesRecent: (filter: object) => ['anomalies-recent', filter] as const,
  /** Prefix matching every recent-anomalies query — use for SSE invalidation. */
  anomaliesRecentRoot: ['anomalies-recent'] as const,

  /** Custom-detector attributions for one trace (the detail drawer's anomaly banner). */
  anomalyHits: (callId?: string) => ['anomaly-hits', callId ?? null] as const,

  /** Custom anomaly detectors for a project (the list carries full detectors — no detail fetch). */
  anomalyDetectors: (projectId?: string) => ['anomaly-detectors', projectId ?? null] as const,
  /** Prefix matching every anomaly-detectors query — use for invalidation. */
  anomalyDetectorsRoot: ['anomaly-detectors'] as const,
};
