// The page-load profiles the HTTP perf scope exercises.
//
// A page visit is one k6 iteration: every request the page fires on mount, sent as a single
// concurrent batch (that is how a browser fetches them, and it is what makes the page's slowest
// request its time-to-data). Each request keeps its own `name` tag, so the per-endpoint p95s stay
// comparable with the budgets and with the DB-layer scope; the batch as a whole is reported as one
// `page_load_<page>` sample — the duration of its slowest mount request.
//
// The request sets mirror the real hooks — frontend/src/features/*/hooks — including the desktop
// default selection (the first agent/suite/evaluator/run group opens with its detail reads) and the
// default time windows the pages resolve to. A request is skipped when its discovered id is absent
// (a suite-less or evaluator-less seed), so a page still measures its list-shaped requests instead
// of failing on a 404.

const DAY_MS = 86400000;

// Query-string builder matching the frontend's `qs`: skips absent values, encodes the rest (ISO
// timestamps carry colons and would otherwise be ambiguous in a URL).
export function qs(params) {
  return Object.entries(params)
    .filter(([, value]) => value !== undefined && value !== null && value !== '')
    .map(([key, value]) => `${encodeURIComponent(key)}=${encodeURIComponent(value)}`)
    .join('&');
}

/** One measured request. `path` carries no base URL; the runner prefixes BASE_URL. */
function get(name, path, params) {
  const query = params ? qs(params) : '';
  return { name, path: query ? `${path}?${query}` : path };
}

// k6 v0.52's compiler does not enable object spread, so build extended param maps explicitly.
function extend(base, extra) {
  return Object.assign({}, base, extra);
}

/** A calendar-month window for the Costs page, which opens on the period budgets are measured over. */
function monthStartIso(nowMs) {
  const now = new Date(nowMs);
  return new Date(Date.UTC(now.getUTCFullYear(), now.getUTCMonth(), 1)).toISOString();
}

/**
 * Resolves the time windows once per iteration (so relative ranges track "now" for the whole batch,
 * exactly like the app recomputes them on render). `ids` is whatever `setup` discovered.
 */
export function windowContext(ids, nowMs = Date.now()) {
  return {
    token: ids.token,
    projectId: ids.projectId,
    agentId: ids.agentId,
    suiteId: ids.suiteId,
    suiteAgentId: ids.suiteAgentId,
    evaluatorId: ids.evaluatorId,
    runGroupId: ids.runGroupId,
    to: new Date(nowMs).toISOString(),
    from24h: new Date(nowMs - DAY_MS).toISOString(),
    from7d: new Date(nowMs - 7 * DAY_MS).toISOString(),
    from90d: new Date(nowMs - 90 * DAY_MS).toISOString(),
    monthStart: monthStartIso(nowMs),
  };
}

/**
 * Page name -> load profile. `weight` is the relative share of page visits in the mixed load
 * (traces is the workhorse; dashboard is the landing page; the rest are visited less often).
 */
export const PAGES = {
  // Dashboard: one aggregate payload plus the draft-proposal count the fleet header shows.
  dashboard: {
    weight: 2,
    requests: c => [
      get('dashboard', '/api/statistics/dashboard', {
        from: c.from90d,
        to: c.to,
        projectId: c.projectId,
        recentTraceCount: 12,
      }),
      get('proposalsList', '/api/proposals', { projectId: c.projectId }),
    ],
  },

  // Traces: the list chunk + the filter-bar overview + the KPI summary + the histogram + the
  // tool/session pickers, plus the first-load "newest trace" probe that auto-picks the window.
  // includeSystemAgents=false matches the page's default toggle.
  traces: {
    weight: 4,
    requests: c => {
      const filtered = {
        from: c.from7d,
        to: c.to,
        projectId: c.projectId,
        includeSystemAgents: false,
      };
      return [
        get('agentCallsList', '/api/agent-calls', extend(filtered, { page: 1, pageSize: 50 })),
        get('agentCallsOverview', '/api/agent-calls/overview', {
          from: c.from7d,
          to: c.to,
          projectId: c.projectId,
        }),
        get('agentCallsSummary', '/api/agent-calls/summary', filtered),
        get('agentCallsHistogram', '/api/agent-calls/histogram', extend(filtered, { buckets: 120 })),
        get('agentCallsNewest', '/api/agent-calls', {
          projectId: c.projectId,
          includeSystemAgents: true,
          page: 1,
          pageSize: 1,
        }),
        get('agentCallToolNames', '/api/agent-calls/tool-names', { projectId: c.projectId }),
        get('sessionsRecent', '/api/sessions', { projectId: c.projectId, page: 1, pageSize: 50 }),
      ];
    },
  },

  // Agents: the list (which itself pays the last-call-times grouping) and the default-selected
  // agent's detail reads — full agent, versions, stats overview, distributions, recent traces and
  // recent outliers.
  agents: {
    weight: 2,
    requests: c => {
      const range = { from: c.from7d, to: c.to };
      return [
        get('agentsList', '/api/agents', { projectId: c.projectId, pageSize: 200 }),
        get('agentDetail', `/api/agents/${c.agentId}`),
        get('agentVersions', `/api/agents/${c.agentId}/versions`),
        get('agentOverview', `/api/statistics/agents/${c.agentId}/overview`, extend(range, { bucket: 'daily' })),
        get('agentDistributions', `/api/statistics/agents/${c.agentId}/distributions`, range),
        get('agentRecentTraces', '/api/agent-calls', {
          agentId: c.agentId,
          projectId: c.projectId,
          includeSystemAgents: true,
          page: 1,
          pageSize: 6,
        }),
        get('agentRecentOutliers', '/api/agent-calls', {
          agentId: c.agentId,
          projectId: c.projectId,
          includeSystemAgents: true,
          outlierOnly: true,
          page: 1,
          pageSize: 6,
        }),
      ];
    },
  },

  // Suites: the list + its filter dropdowns (agents, evaluators) and the default-selected suite's
  // detail reads — the suite (with its run stats), the run-stats strip (default "all" window), the
  // full traces the edit dialog offers, and the schedule badge read.
  suites: {
    weight: 2,
    requests: c => {
      const requests = [
        get('testSuitesList', '/api/test-suites', { projectId: c.projectId, pageSize: 200 }),
        get('agentsList', '/api/agents', { projectId: c.projectId, pageSize: 200 }),
        get('evaluatorsList', '/api/evaluators', { projectId: c.projectId }),
      ];
      if (c.suiteId) {
        requests.push(get('testSuiteDetail', `/api/test-suites/${c.suiteId}`));
        requests.push(get('testSuiteRunStats', `/api/test-suites/${c.suiteId}/run-stats`));
      }
      if (c.suiteAgentId) {
        requests.push(get('agentCallsFullByAgent', '/api/agent-calls/full', {
          agentId: c.suiteAgentId,
          pageSize: 50,
        }));
        requests.push(get('testRunSchedules', '/api/test-run-schedules', {
          agentId: c.suiteAgentId,
        }));
      }
      return requests;
    },
  },

  // Costs: the spend telemetry (month-to-date window, daily buckets), the budget list and status,
  // the agent names the chart legend uses, and the providers overview that feeds the budget scope
  // picker (admin-only, and the perf admin is one).
  costs: {
    weight: 1,
    requests: c => [
      get('costOverview', '/api/statistics/cost-overview', {
        projectId: c.projectId,
        from: c.monthStart,
        to: c.to,
        bucket: 'daily',
      }),
      get('costLimits', '/api/cost-limits', { projectId: c.projectId }),
      get('costBudgetStatus', '/api/cost-limits/status', { projectId: c.projectId }),
      get('agentsList', '/api/agents', { projectId: c.projectId, pageSize: 200 }),
      get('providersOverview', '/api/providers/overview'),
    ],
  },

  // Runs: the group rail (each list item carries the summaries of every run in its group) plus the
  // agent filter, and the default-selected group's detail read (the per-case matrix payload).
  runs: {
    weight: 1,
    requests: c => {
      const requests = [
        get('testRunGroups', '/api/test-run-groups', { projectId: c.projectId, page: 1, pageSize: 20 }),
        get('agentsList', '/api/agents', { projectId: c.projectId, pageSize: 200 }),
      ];
      if (c.runGroupId) {
        requests.push(get('testRunGroupDetail', `/api/test-run-groups/${c.runGroupId}`));
      }
      return requests;
    },
  },

  // Evaluators: the one-shot overview (evaluators + suite refs + 7d sparklines) and the default
  // selected evaluator's detail read.
  evaluators: {
    weight: 1,
    requests: c => {
      const range = { from: c.from7d, to: c.to, bucket: 'daily' };
      const requests = [
        get('evaluatorsOverview', '/api/evaluators/overview', extend({ projectId: c.projectId }, range)),
        get('agenticPresets', '/api/evaluators/agentic-presets'),
      ];
      if (c.evaluatorId) {
        requests.push(get('evaluatorDetail', `/api/evaluators/${c.evaluatorId}/detail`, extend(range, { recentCount: 8 })));
      }
      return requests;
    },
  },

  // Anomalies: the default 24h/hourly timeline and the recent flagged-calls list.
  anomalies: {
    weight: 1,
    requests: c => [
      get('anomalyTimeline', '/api/statistics/anomalies/timeline', {
        from: c.from24h,
        to: c.to,
        bucket: 'hourly',
        projectId: c.projectId,
      }),
      get('anomaliesRecent', '/api/anomalies/recent', {
        projectId: c.projectId,
        page: 1,
        pageSize: 20,
      }),
    ],
  },
};
