import { randomUUID } from 'node:crypto';
import { test, expect } from '../helpers/fixtures';
import type { APIRequestContext, Page } from '@playwright/test';
import { ProxytraceApiClient } from '../helpers/api-client';
import { addTraceFilter } from '../helpers/traces-ui';

// End-to-end coverage for trace sessions: a client session key stamped on seeded traces groups them
// into a session that surfaces on the sessions list, the session detail page (header + live trace
// timeline), the trace-detail session link, and the traces-page session filter. All seed-based — no
// LLM round-trip — so it runs in the `core` project.

function unique(prefix: string): string {
  return `${prefix}-${Date.now()}-${randomUUID().slice(0, 8)}`;
}

async function makeClient(request: APIRequestContext): Promise<ProxytraceApiClient> {
  const client = new ProxytraceApiClient(request);
  const { token } = await client.login('admin@e2e.test', 'E2ePassword1!');
  client.setToken(token);
  return client;
}

/** How many trace rows the table currently renders (session timeline or /traces list). */
function traceRowCount(page: Page): Promise<number> {
  return page.locator('[data-testid^="trace-row-"]').count();
}

test.describe('Trace sessions', () => {
  let api: ProxytraceApiClient;
  let projectId: string;
  let endpointId: string;
  let sessionKey: string;
  let sessionId: string;
  let sessionCallIds: string[];
  let looseCallId: string;

  test.beforeEach(async ({ request }) => {
    api = await makeClient(request);
    projectId = await api.firstProjectId();
    endpointId = await api.firstEndpointId();

    // Two agents proves the session groups traces across agents, not just one agent's calls.
    const agentA = await api.createAgent({ name: unique('Session Agent A'), endpointId });
    const agentB = await api.createAgent({ name: unique('Session Agent B'), endpointId });

    sessionKey = unique('e2e-session');
    const c1 = await api.seedAgentCall({ agentId: agentA.id, userContent: 'hi 1', assistantContent: 'r1', sessionKey });
    const c2 = await api.seedAgentCall({ agentId: agentA.id, userContent: 'hi 2', assistantContent: 'r2', sessionKey });
    const c3 = await api.seedAgentCall({ agentId: agentB.id, userContent: 'hi 3', assistantContent: 'r3', sessionKey });
    sessionCallIds = [c1.id, c2.id, c3.id];

    // A control trace with no session key — it must never appear in the session view or filter.
    const loose = await api.seedAgentCall({ agentId: agentA.id, userContent: 'no session', assistantContent: 'r' });
    looseCallId = loose.id;

    // Resolve the derived session id from the list (the seed doesn't return it). The external key is
    // the (untruncated here) session key we sent.
    const { items } = await api.listSessions(projectId);
    const session = items.find(s => s.externalKey === sessionKey);
    if (!session) throw new Error(`seeded session ${sessionKey} not found in ${JSON.stringify(items)}`);
    sessionId = session.id;
  });

  test('GET /api/sessions lists the session with its seeded trace count', async () => {
    const { items } = await api.listSessions(projectId);
    const session = items.find(s => s.id === sessionId);
    expect(session).toBeTruthy();
    expect(session?.traceCount).toBe(3);
  });

  test('trace detail links to the session page, which shows the header and its traces', async ({ page }) => {
    await page.goto('/traces', { waitUntil: 'load' });
    await expect(page.getByTestId('trace-table')).toBeVisible();

    // Open a sessioned trace and follow its "Session" link into the session detail page.
    await page.getByTestId(`trace-row-${sessionCallIds[0]}`).click();
    await expect(page.getByTestId('trace-detail')).toBeVisible();
    await page.getByTestId('trace-session-link').click();

    await expect(page).toHaveURL(new RegExp(`/sessions/${sessionId}$`));
    await expect(page.getByTestId('session-view')).toBeVisible();
    await expect(page.getByTestId('session-header')).toBeVisible();
    await expect(page.getByTestId('session-external-key')).toHaveText(sessionKey);
    await expect(page.getByTestId('session-trace-count')).toHaveText('3');

    // Exactly the three sessioned traces render — the loose control trace is excluded.
    await expect(page.locator('[data-testid^="trace-row-"]')).toHaveCount(3);
    await expect(page.getByTestId(`trace-row-${looseCallId}`)).toHaveCount(0);
  });

  test('a trace seeded into the open session appears live without reload', async ({ page, request }) => {
    await page.goto(`/sessions/${sessionId}`, { waitUntil: 'load' });
    await expect(page.getByTestId('session-view')).toBeVisible();
    await expect(page.locator('[data-testid^="trace-row-"]')).toHaveCount(3);

    // Seed a fourth trace into the same session while the page is open; the SSE stream must add its
    // row without a reload. Use a fresh client so we don't disturb the page's session.
    const live = await makeClient(request);
    await live.seedAgentCall({
      agentId: (await api.createAgent({ name: unique('Session Agent Live'), endpointId })).id,
      userContent: 'live arrival',
      assistantContent: 'r',
      sessionKey,
    });

    await expect
      .poll(() => traceRowCount(page), {
        timeout: 30_000,
        intervals: [1_000],
        message: 'the live-seeded trace did not appear on the open session page',
      })
      .toBe(4);
  });

  test('the traces page session filter narrows the list to the session', async ({ page }) => {
    await page.goto('/traces', { waitUntil: 'load' });
    await expect(page.getByTestId('trace-table')).toBeVisible();
    // The loose trace is present before filtering.
    await expect(page.getByTestId(`trace-row-${looseCallId}`)).toBeVisible();

    // The session picker lists the session by its external key; selecting it filters the table.
    await addTraceFilter(page, 'session', sessionId);
    await expect(page.getByTestId('traces-filter-chip-session')).toContainText(sessionKey);

    await expect(page.locator('[data-testid^="trace-row-"]')).toHaveCount(3);
    await expect(page.getByTestId(`trace-row-${looseCallId}`)).toHaveCount(0);
    for (const id of sessionCallIds) {
      await expect(page.getByTestId(`trace-row-${id}`)).toBeVisible();
    }
  });
});
