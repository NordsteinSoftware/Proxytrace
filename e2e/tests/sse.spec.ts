import { test, expect } from '../helpers/fixtures';
import { ProxytraceApiClient } from '../helpers/api-client';

// The agent-call seed endpoint publishes TraceCreatedEvent to ITraceBroadcaster (the same path
// real ingestion uses), so the dashboard's LiveTraceStream — wired to the SSE trace stream —
// appends a row live, without a page reload. This exercises the SSE broadcaster end to end.
test.describe('SSE real-time trace stream', () => {
  let agentId: string;

  test.beforeEach(async ({ request }) => {
    const api = new ProxytraceApiClient(request);
    const { token } = await api.login('admin@e2e.test', 'E2ePassword1!');
    api.setToken(token);
    const endpointId = await api.firstEndpointId();
    agentId = (await api.createAgent({ name: `SSE Agent ${Date.now()}`, endpointId })).id;
  });

  test('a newly ingested trace streams into LiveTraceStream without a reload', async ({ page, request }) => {
    const client = new ProxytraceApiClient(request);
    const { token } = await client.login('admin@e2e.test', 'E2ePassword1!');
    client.setToken(token);

    await page.goto('/dashboard', { waitUntil: 'load' });
    await expect(page.getByTestId('live-trace-stream')).toBeVisible();

    // Seed a trace AFTER the SSE subscription is live; the row must arrive via the push, so we
    // never navigate or reload between the seed and the assertion.
    const seeded = await client.seedAgentCall({
      agentId,
      userContent: 'sse ping',
      assistantContent: 'sse pong',
    });

    await expect
      .poll(async () => page.getByTestId(`live-trace-row-${seeded.id}`).count(), {
        timeout: 20_000,
        intervals: [1_000],
        message: 'seeded trace did not stream into LiveTraceStream via SSE',
      })
      .toBeGreaterThan(0);
  });

  test('a live trace inserts into the traces table in place, without redrawing it', async ({ page, request }) => {
    const client = new ProxytraceApiClient(request);
    const { token } = await client.login('admin@e2e.test', 'E2ePassword1!');
    client.setToken(token);

    // A row to watch: the arrival must be grafted onto the rows already rendered, not replace them.
    const existing = await client.seedAgentCall({
      agentId,
      userContent: 'already listed',
      assistantContent: 'ok',
    });

    await page.goto('/traces', { waitUntil: 'load' });
    await expect(page.getByTestId(`trace-row-${existing.id}`)).toBeVisible();

    // Prove the stream is connected before the assertion below depends on it: a warm-up arrival that
    // lands takes the "was the event missed?" ambiguity out of the real check.
    const warmup = await client.seedAgentCall({ agentId, userContent: 'warm up', assistantContent: 'ok' });
    await expect(page.getByTestId(`trace-row-${warmup.id}`)).toBeVisible({ timeout: 20_000 });

    // Record any reappearance of the list's loading skeleton from here on. The bug this guards was
    // exactly that: every arrival reset the infinite query, which left it with no rows to render, so
    // the whole table flashed back to skeletons and redrew.
    await page.evaluate(() => {
      const flags = window as unknown as { __traceSkeletonSeen?: boolean };
      flags.__traceSkeletonSeen = false;
      new MutationObserver(() => {
        if (document.querySelector('[data-testid="trace-list-loading"]')) flags.__traceSkeletonSeen = true;
      }).observe(document.body, { childList: true, subtree: true });
    });
    const watchedRow = await page.getByTestId(`trace-row-${existing.id}`).elementHandle();

    const arrival = await client.seedAgentCall({
      agentId,
      userContent: 'live arrival',
      assistantContent: 'ok',
    });

    await expect(page.getByTestId(`trace-row-${arrival.id}`)).toBeVisible({ timeout: 20_000 });

    // The arrival carries the one-shot cyan wash: the class is on the row and its overlay is mid-decay.
    await expect(page.getByTestId(`trace-row-${arrival.id}`)).toHaveClass(/arrival-flash/);

    // Same DOM node as before the arrival: an in-place insert keeps it mounted, a reload replaces it.
    expect(await watchedRow?.evaluate(el => el.isConnected)).toBe(true);
    expect(await page.evaluate(() => (window as unknown as { __traceSkeletonSeen?: boolean }).__traceSkeletonSeen))
      .toBe(false);
  });
});
