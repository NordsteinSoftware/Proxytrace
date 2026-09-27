import http from 'k6/http';
import { check } from 'k6';
import { Trend } from 'k6/metrics';
import {
  authenticate,
  discoverAgentId,
  discoverEvaluatorId,
  discoverProjectId,
  discoverRunGroupId,
  discoverSuite,
} from './helpers/auth.js';
import { PAGES, windowContext } from './helpers/pages.js';

// Page-load load test: each iteration is one visit to one of the product's main pages, firing that
// page's mount requests as a single concurrent batch (see helpers/pages.js for the profiles).
//
// Budgets come from the same perf-budgets.json the DB-layer + benchmark scopes use. Only budgets
// that exist are enforced — a missing key means "measure but never fail", so a new page/endpoint
// lands with numbers before it gets a threshold. `httpP95Ms` keys per-request thresholds by the
// request's `name` tag; the optional `httpPageP95Ms` keys composite page budgets by page name.
const budgets = JSON.parse(open('../perf-budgets.json'));

const BASE = __ENV.BASE_URL || 'http://localhost:5230';
const EMAIL = __ENV.ADMIN_EMAIL || 'perf-admin@proxytrace.dev';
const PASSWORD = __ENV.ADMIN_PASSWORD || 'PerfAdmin123!';

// Comma-separated page names to exercise (default: every page), e.g. PAGES=traces,agents.
const only = (__ENV.PAGES || '').split(',').map(s => s.trim()).filter(Boolean);
const selected = Object.entries(PAGES).filter(([name]) => only.length === 0 || only.includes(name));
if (selected.length === 0) {
  throw new Error(`PAGES matched no known page; known pages: ${Object.keys(PAGES).join(', ')}`);
}

// One time-to-data metric per page (not one tagged metric: k6 only splits a tagged custom metric
// into per-tag submetrics when a threshold references that tag, and the page budgets are optional).
// A sample is the visit's slowest mount request; the metric is read by name in the summary.
const pageLoads = {};
for (const name of Object.keys(PAGES)) {
  pageLoads[name] = new Trend(`page_load_${name}`, true);
}

// Same reasoning for the per-request series: a request tag is only materialized as an
// http_req_duration submetric when an httpP95Ms threshold references it, so an unbudgeted endpoint
// would vanish from the summary. Mirror every request into its own Trend instead; the tagged
// http_req_duration metric (and its budgets) stays exactly as it was.
const endpointNames = collectRequestNames();
const endpointTrends = {};
for (const name of endpointNames) {
  endpointTrends[name] = new Trend(`endpoint_${name}`, true);
}

// The request names every profile can emit, discovered from the profiles themselves so a new
// request in helpers/pages.js needs no change here. Placeholder ids just make the conditional
// requests visible; values never reach the wire from this context.
function collectRequestNames() {
  const ctx = windowContext({
    token: 'name-discovery',
    projectId: 'name-discovery',
    agentId: 'name-discovery',
    suiteId: 'name-discovery',
    suiteAgentId: 'name-discovery',
    evaluatorId: 'name-discovery',
    runGroupId: 'name-discovery',
  });
  const names = {};
  for (const page of Object.values(PAGES)) {
    for (const request of page.requests(ctx)) {
      names[request.name] = true;
    }
  }
  return Object.keys(names).sort();
}

export const options = {
  scenarios: {
    pages: {
      executor: 'constant-vus',
      vus: Number(__ENV.VUS || 10),
      duration: __ENV.DURATION || '30s',
    },
  },
  thresholds: buildThresholds(),
  // `count` is not in k6's default trend stats; the summary needs it to tell a measured series
  // apart from a declared-but-skipped one (an optional detail request whose id was not discovered).
  summaryTrendStats: ['count', 'avg', 'min', 'med', 'max', 'p(90)', 'p(95)'],
};

function buildThresholds() {
  const thresholds = { http_req_failed: ['rate<0.01'] };
  for (const [name, budget] of Object.entries(budgets.httpP95Ms || {})) {
    thresholds[`http_req_duration{name:${name}}`] = [`p(95)<${budget}`];
  }
  for (const [page, budget] of Object.entries(budgets.httpPageP95Ms || {})) {
    thresholds[`page_load_${page}`] = [`p(95)<${budget}`];
  }
  return thresholds;
}

// The ids every page profile may need, discovered once. Required ids (project, agent) fail setup
// loudly; optional ones (suite, evaluator) come back null on a sparse seed and only drop the
// detail-shaped requests from their page.
export function setup() {
  const token = authenticate(BASE, EMAIL, PASSWORD);
  const projectId = discoverProjectId(BASE, token);
  const agentId = discoverAgentId(BASE, token);
  const suite = discoverSuite(BASE, token, projectId);
  return {
    token,
    projectId,
    agentId,
    suiteId: suite ? suite.id : null,
    suiteAgentId: suite ? suite.agentId : null,
    evaluatorId: discoverEvaluatorId(BASE, token, projectId),
    runGroupId: discoverRunGroupId(BASE, token, projectId),
  };
}

export default function (data) {
  const page = pickPage();
  const ctx = windowContext(data);
  const requests = PAGES[page].requests(ctx);
  const responses = http.batch(requests.map(request => [
    'GET',
    `${BASE}${request.path}`,
    null,
    {
      headers: { Authorization: `Bearer ${ctx.token}` },
      tags: { name: request.name },
    },
  ]));

  let slowestMs = 0;
  responses.forEach((response, index) => {
    if (!response || !response.timings) {
      return;
    }
    const name = requests[index].name;
    check(response, { [`${name} ok`]: r => r.status >= 200 && r.status < 300 });
    endpointTrends[name].add(response.timings.duration);
    if (response.timings.duration > slowestMs) {
      slowestMs = response.timings.duration;
    }
  });
  pageLoads[page].add(slowestMs);
}

// Weighted pick over the selected pages; traces carries the largest weight because it is the page
// operators live on.
function pickPage() {
  const total = selected.reduce((sum, [, page]) => sum + page.weight, 0);
  let roll = Math.random() * total;
  for (const [name, page] of selected) {
    roll -= page.weight;
    if (roll <= 0) return name;
  }
  return selected[selected.length - 1][0];
}

// Persist a machine-readable summary next to the other scopes' results (path is relative to the repo
// root, where run.sh / the workflow invoke k6) and print a page-first table: each page's
// time-to-data p95 followed by the per-endpoint p95s, annotated with their budget when one exists.
export function handleSummary(data) {
  const metrics = data.metrics || {};
  const lines = ['', 'k6 page-load summary', '='.repeat(64)];

  lines.push('pages (time to every mount request returned)');
  for (const [name] of selected) {
    lines.push(line(`  page:${name}`, metrics[`page_load_${name}`], budgets.httpPageP95Ms));
  }

  lines.push('', 'endpoints');
  for (const name of endpointNames) {
    const metric = metrics[`endpoint_${name}`];
    if (!hasSamples(metric)) {
      continue;
    }
    lines.push(line(`  ${name}`, metric, budgets.httpP95Ms));
  }

  const failed = metrics.http_req_failed;
  if (failed && failed.values) {
    lines.push('', `error rate  ${(failed.values.rate * 100).toFixed(2)}%`);
  }
  lines.push('='.repeat(64), '');
  return {
    'perf/results/k6-summary.json': JSON.stringify(data, null, 2),
    stdout: lines.join('\n'),
  };
}

function line(label, metric, budgetSet) {
  const values = metric && metric.values ? metric.values : null;
  const sampled = hasSamples(metric);
  const p95 = sampled ? values['p(95)'] : undefined;
  const value = p95 === undefined ? 'n/a' : `${p95.toFixed(1)}ms`;
  const name = label.replace(/^  (page:)?/, '');
  const budget = budgetSet ? budgetSet[name] : undefined;
  return `${label.padEnd(34)} p95=${value.padStart(10)}${budget ? `  budget=${budget}ms` : ''}`;
}

// A declared request is not necessarily a measured one — the detail-shaped requests are skipped when
// their id was not discovered — so zero-sample series are excluded from the printed table.
function hasSamples(metric) {
  return !!metric && !!metric.values && metric.values.count > 0;
}
