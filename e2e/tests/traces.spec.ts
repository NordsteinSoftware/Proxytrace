import { test, expect } from '../helpers/fixtures';
import type { APIRequestContext } from '@playwright/test';
import { ProxytraceApiClient } from '../helpers/api-client';
import { selectAgentFilter } from '../helpers/traces-ui';

// Traces page (/traces) coverage.
//
// Seeding notes:
//  - api.seedAgentCall builds a captured call directly. The seeded call's model is the agent's
//    endpoint model (the setup default, 'gpt-4o-mini'), NOT a custom DTO string — so we never
//    assert a custom model.
//  - The traces table groups calls that SHARE a conversationId into ConversationGroupRow; the
//    seed endpoint always sets conversationId = null, so every seeded trace renders as a
//    FlatTraceRow. There is no UI "grouping toggle" — grouping is automatic and data-driven.
//    The "conversation grouping toggle" todo item is therefore NOT implementable through the
//    available seed path (see the report). We assert flat rows render instead.

function uniqueName(prefix: string): string {
  return `${prefix} ${Date.now()}-${Math.floor(Math.random() * 100000)}`;
}

async function makeClient(request: APIRequestContext): Promise<ProxytraceApiClient> {
  const client = new ProxytraceApiClient(request);
  const { token } = await client.login('admin@e2e.test', 'E2ePassword1!');
  client.setToken(token);
  return client;
}

test.describe('Traces', () => {
  let endpointId: string;
  let projectId: string;

  test.beforeAll(async ({ request }) => {
    const api = await makeClient(request);
    endpointId = await api.firstEndpointId();
    projectId = await api.firstProjectId();
  });

  test('TraceTable lists seeded traces', async ({ page, request }) => {
    const client = await makeClient(request);

    const agentName = uniqueName('List Agent');
    const { id: agentId } = await client.createAgent({ name: agentName, endpointId });
    const calls: Array<{ id: string }> = [];
    for (let i = 0; i < 3; i++) {
      calls.push(await client.seedAgentCall({ agentId, userContent: `list trace ${i}`, assistantContent: `resp ${i}` }));
    }

    await page.goto('/traces', { waitUntil: 'load' });
    await expect(page.getByTestId('trace-table')).toBeVisible();

    // Filter to this agent so the assertion is independent of other tests' data.
    await selectAgentFilter(page, agentId);

    for (const c of calls) {
      await expect(page.getByTestId(`trace-row-${c.id}`)).toBeVisible();
    }
  });

  test('clicking a trace row opens the detail drawer with messages and metadata', async ({ page, request }) => {
    const client = await makeClient(request);

    const agentName = uniqueName('Detail Agent');
    const { id: agentId } = await client.createAgent({ name: agentName, endpointId });
    const userText = `detail unique ${Date.now()}`;
    const call = await client.seedAgentCall({ agentId, userContent: userText, assistantContent: 'detail reply here' });

    await page.goto('/traces', { waitUntil: 'load' });
    await selectAgentFilter(page, agentId);

    await page.getByTestId(`trace-row-${call.id}`).click();

    // Drawer opens, defaulting to the Messages tab.
    const drawer = page.getByTestId('trace-detail');
    await expect(drawer).toBeVisible();

    // Messages tab shows the conversation content.
    const messagesTab = page.getByTestId('trace-messages-tab');
    await expect(messagesTab).toBeVisible();
    await expect(messagesTab).toContainText(userText);
    await expect(messagesTab).toContainText('detail reply here');

    // Switch to the Metadata tab.
    await page.getByTestId('trace-tab-metadata').click();
    const metadataTab = page.getByTestId('trace-metadata-tab');
    await expect(metadataTab).toBeVisible();
    await expect(metadataTab).toContainText('model');
    await expect(metadataTab).toContainText('http_status');
  });

  test('agent filter narrows the table to a single agent', async ({ page, request }) => {
    const client = await makeClient(request);

    const agentAName = uniqueName('Filter Agent A');
    const agentBName = uniqueName('Filter Agent B');
    const { id: agentAId } = await client.createAgent({ name: agentAName, endpointId });
    const { id: agentBId } = await client.createAgent({ name: agentBName, endpointId });

    const callA = await client.seedAgentCall({ agentId: agentAId, userContent: 'from A', assistantContent: 'a' });
    const callB = await client.seedAgentCall({ agentId: agentBId, userContent: 'from B', assistantContent: 'b' });

    await page.goto('/traces', { waitUntil: 'load' });
    await expect(page.getByTestId('trace-table')).toBeVisible();

    // Filter to agent A: A's trace is visible, B's is not.
    await selectAgentFilter(page, agentAId);
    await expect(page.getByTestId(`trace-row-${callA.id}`)).toBeVisible();
    await expect(page.getByTestId(`trace-row-${callB.id}`)).toBeHidden();
  });

  test('seeded traces render as flat rows (no automatic conversation grouping)', async ({ page, request }) => {
    // Seeded calls carry conversationId = null, so buildRows() emits FlatTraceRow for each.
    // This is the closest verifiable behaviour to the "grouping toggle" item, which has no UI.
    const client = await makeClient(request);

    const agentName = uniqueName('Flat Agent');
    const { id: agentId } = await client.createAgent({ name: agentName, endpointId });
    const c1 = await client.seedAgentCall({ agentId, userContent: 'flat one', assistantContent: 'r1' });
    const c2 = await client.seedAgentCall({ agentId, userContent: 'flat two', assistantContent: 'r2' });

    await page.goto('/traces', { waitUntil: 'load' });
    await selectAgentFilter(page, agentId);

    // Each seeded call is an individually-clickable flat row (not nested under a conversation).
    await expect(page.getByTestId(`trace-row-${c1.id}`)).toBeVisible();
    await expect(page.getByTestId(`trace-row-${c2.id}`)).toBeVisible();
  });

  test('scrolling the trace list loads more traces', async ({ page, request }) => {
    const client = await makeClient(request);

    // TRACE_CHUNK_SIZE is 50; seed 60 for one agent so a second chunk exists.
    const agentName = uniqueName('Scrolling Agent');
    const { id: agentId } = await client.createAgent({ name: agentName, endpointId });
    for (let i = 0; i < 60; i++) {
      await client.seedAgentCall({ agentId, userContent: `scroll trace ${i}`, assistantContent: `r${i}` });
    }

    await page.goto('/traces', { waitUntil: 'load' });
    // Filter to this agent so exactly 60 traces drive the scrolling.
    await selectAgentFilter(page, agentId);
    await expect(page.getByTestId('trace-table')).toBeVisible();

    // The readout is the reliable witness: the list is virtualized, so the DOM only ever holds the
    // visible window — counting rows would measure the viewport, not what has been loaded.
    const readout = page.getByTestId('trace-position-readout');
    await expect(readout).toContainText('60', { timeout: 10_000 });

    const scroller = page.getByTestId('trace-scroll');

    // Every distinct trace seen across the whole scroll.
    const seen = new Set<string>();
    // Reads the currently-mounted rows and folds them in, asserting that no single snapshot ever
    // holds the same trace twice — a duplicated row inside one window means the chunk boundary
    // drifted under offset paging.
    const collect = async () => {
      const ids = await page.locator('[data-testid^="trace-row-"]').evaluateAll(
        els => els.map(el => el.getAttribute('data-testid') ?? ''),
      );
      expect(new Set(ids).size, 'a trace was rendered twice in one window').toBe(ids.length);
      for (const id of ids) seen.add(id);
    };

    await collect();
    const firstScreenCount = seen.size;
    expect(firstScreenCount).toBeGreaterThan(0);
    // Virtualization is doing its job: the DOM holds a window, not all 60 rows.
    expect(firstScreenCount).toBeLessThan(60);

    // Walk to the bottom a screen at a time, collecting as we go — a single jump to the end would
    // skip the rows that are only ever mounted mid-scroll.
    for (let step = 0; step < 30; step++) {
      await scroller.evaluate(el => { el.scrollTop += el.clientHeight; });
      await page.waitForTimeout(150);
      await collect();
      const atEnd = await scroller.evaluate(el => el.scrollTop + el.clientHeight >= el.scrollHeight - 4);
      if (atEnd) break;
    }

    // The second chunk was fetched and rendered.
    expect(seen.size).toBeGreaterThan(firstScreenCount);
    // Every seeded trace was reachable exactly once: no skips across the chunk boundary, and no
    // trace served by both chunks.
    expect(seen.size, 'every seeded trace should be reachable exactly once').toBe(60);

    // The list ends explicitly rather than simply stopping.
    await expect(page.getByTestId('trace-list-end')).toBeVisible({ timeout: 10_000 });
  });
});
