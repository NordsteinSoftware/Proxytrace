import http from 'k6/http';

// Window helpers shared by the load scripts. `isoDaysAgo` is the base primitive: everything else is
// derived from it so the page-load request sets and the discovery probes can never disagree about
// what "a 90-day window" means.
export function isoDaysAgo(days, nowMs = Date.now()) {
  return new Date(nowMs - days * 86400000).toISOString();
}

export function windowFrom() {
  return isoDaysAgo(90);
}

export function windowTo() {
  return new Date().toISOString();
}

// Bootstraps (first run) or logs in the perf admin and returns a bearer token. The seeder creates
// this account, so a seeded database normally takes the login branch; a database seeded without it
// (no users at all) falls back to /api/auth/setup.
export function authenticate(baseUrl, email, password) {
  const mode = http.get(`${baseUrl}/api/auth/mode`);
  const setupRequired = mode.status === 200 && JSON.parse(mode.body).setupRequired === true;

  const body = JSON.stringify({ email, password });
  const params = { headers: { 'Content-Type': 'application/json' } };
  const res = setupRequired
    ? http.post(`${baseUrl}/api/auth/setup`, body, params)
    : http.post(`${baseUrl}/api/auth/login`, body, params);

  if (res.status !== 200) {
    throw new Error(`auth failed (${setupRequired ? 'setup' : 'login'}): ${res.status} ${res.body}`);
  }
  const token = JSON.parse(res.body).token;
  if (!token) {
    throw new Error(`no token in auth response: ${res.body}`);
  }
  return token;
}

// Picks a real agent id (one with calls) from the dashboard breakdown, so the per-agent distribution
// endpoint exercises a populated agent rather than 404/empty.
export function discoverAgentId(baseUrl, token) {
  const res = http.get(
    `${baseUrl}/api/statistics/dashboard?from=${windowFrom()}&to=${windowTo()}`,
    { headers: { Authorization: `Bearer ${token}` } });
  if (res.status !== 200) {
    throw new Error(`dashboard probe failed: ${res.status} ${res.body}`);
  }
  const breakdown = JSON.parse(res.body).agentBreakdown || [];
  if (breakdown.length === 0) {
    throw new Error('dashboard returned no agents — seed the database before running the load test');
  }
  return breakdown[0].agentId;
}

// Fetches `path` with the bearer token and parses the JSON body, failing the setup run loudly when
// the API answers with anything but 200 (a silent `null` here would turn into missing page requests
// rather than a clear "the seed/API is broken" signal).
function getJson(baseUrl, token, path, what) {
  const res = http.get(`${baseUrl}${path}`, { headers: { Authorization: `Bearer ${token}` } });
  if (res.status !== 200) {
    throw new Error(`${what} probe failed: ${res.status} ${res.body}`);
  }
  return JSON.parse(res.body);
}

// Every page-load scenario needs the seeded project; the page scenarios can only be meaningful on
// real data, so a missing project is a hard setup failure, not a skipped page.
export function discoverProjectId(baseUrl, token) {
  const page = getJson(baseUrl, token, '/api/projects?page=1&pageSize=1', 'project');
  const items = page.items || [];
  const id = items.length > 0 ? items[0].id : null;
  if (!id) {
    throw new Error('no projects found — seed the database before running the load test');
  }
  return id;
}

// The suite detail page needs a real suite and its owning agent (the edit-traces and schedules reads
// are agent-scoped). Returns null when no suite is seeded, so the list-only part of the page still
// measures instead of the whole page being dropped.
export function discoverSuite(baseUrl, token, projectId) {
  const page = getJson(
    baseUrl, token, `/api/test-suites?projectId=${projectId}&page=1&pageSize=1`, 'test-suite');
  const items = page.items || [];
  return items.length > 0 ? { id: items[0].id, agentId: items[0].agentId } : null;
}

// First evaluator in the project, for the evaluators detail view. Null when none is seeded.
export function discoverEvaluatorId(baseUrl, token, projectId) {
  const evaluators = getJson(baseUrl, token, `/api/evaluators?projectId=${projectId}`, 'evaluator');
  return evaluators.length > 0 ? evaluators[0].id : null;
}

// The runs page's default-selected group (the first one, as the desktop layout selects it): its
// detail read carries the per-case matrix payload. Null when no group is seeded.
export function discoverRunGroupId(baseUrl, token, projectId) {
  const page = getJson(
    baseUrl, token, `/api/test-run-groups?projectId=${projectId}&page=1&pageSize=1`, 'test-run-group');
  const items = page.items || [];
  return items.length > 0 ? items[0].id : null;
}
