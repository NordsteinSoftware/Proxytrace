import { randomUUID } from 'node:crypto';
import { test, expect } from '../helpers/fixtures';
import type { APIRequestContext } from '@playwright/test';
import { ProxytraceApiClient } from '../helpers/api-client';
import { addTraceFilter } from '../helpers/traces-ui';

// End-to-end coverage for scopes (use-case groups of agents): a scope key stamped on seeded traces
// auto-creates the scope, which surfaces on the Scopes page (list + detail + edit), as a tag and
// detail link on the traces it groups, and as the traces-page scope filter. Seed-based — no LLM
// round-trip — so it runs in the `core` project. The seed admits scopes through the same
// normalisation the proxy uses, so "Support Agents" lands as `support-agents`.

function unique(prefix: string): string {
  return `${prefix}-${Date.now()}-${randomUUID().slice(0, 8)}`;
}

async function makeClient(request: APIRequestContext): Promise<ProxytraceApiClient> {
  const client = new ProxytraceApiClient(request);
  const { token } = await client.login('admin@e2e.test', 'E2ePassword1!');
  client.setToken(token);
  return client;
}

test.describe('Scopes', () => {
  let api: ProxytraceApiClient;
  let projectId: string;
  let scopeId: string;
  let scopedCallIds: string[];
  let looseCallId: string;
  let sharedAgentId: string;

  test.beforeEach(async ({ request }) => {
    api = await makeClient(request);
    projectId = await api.firstProjectId();
    const endpointId = await api.firstEndpointId();

    const support = await api.createAgent({ name: unique('Scope Support Agent'), endpointId });
    const shared = await api.createAgent({ name: unique('Scope Shared Agent'), endpointId });
    sharedAgentId = shared.id;

    const c1 = await api.seedAgentCall({ agentId: support.id, userContent: 'refund?', assistantContent: 'r1', scopeKey: 'Support Agents' });
    const c2 = await api.seedAgentCall({ agentId: shared.id, userContent: 'translate', assistantContent: 'r2', scopeKey: 'support-agents' });
    // The shared agent also serves a second scope — membership is per scope, not exclusive.
    await api.seedAgentCall({ agentId: shared.id, userContent: 'invoice', assistantContent: 'r3', scopeKey: 'billing' });
    scopedCallIds = [c1.id, c2.id];
    const loose = await api.seedAgentCall({ agentId: support.id, userContent: 'no scope', assistantContent: 'r' });
    looseCallId = loose.id;

    const scope = (await api.listScopes(projectId)).find(s => s.key === 'support-agents');
    if (!scope) throw new Error('seeded scope support-agents not found');
    scopeId = scope.id;
  });

  test('GET /api/scopes lists normalised scopes with their member agents', async () => {
    const scopes = await api.listScopes(projectId);
    expect(scopes.map(s => s.key).sort()).toEqual(['billing', 'support-agents']);
    const support = scopes.find(s => s.id === scopeId);
    expect(support?.traceCount).toBe(2);
    expect(support?.agentIds).toContain(sharedAgentId);
    expect(scopes.find(s => s.key === 'billing')?.agentIds).toEqual([sharedAgentId]);
  });

  test('the Scopes page shows the scope and its display name can be edited', async ({ page }) => {
    await page.goto(`/scopes?id=${scopeId}`, { waitUntil: 'load' });
    await expect(page.getByTestId(`scope-row-${scopeId}`)).toBeVisible();
    await expect(page.getByTestId('scope-detail-key')).toHaveText('support-agents');

    await page.getByTestId('scope-edit-btn').click();
    await page.getByTestId('scope-display-name-input').fill('Support crew');
    await page.getByTestId('modal-submit').click();

    await expect(page.getByTestId('scope-detail-name')).toHaveText('Support crew');
    await expect.poll(async () => (await api.listScopes(projectId)).find(s => s.id === scopeId)?.displayName)
      .toBe('Support crew');
  });

  test('"View traces" opens the traces list filtered to the scope', async ({ page }) => {
    await page.goto(`/scopes?id=${scopeId}`, { waitUntil: 'load' });
    await page.getByTestId('scope-view-traces-btn').click();

    await expect(page).toHaveURL(/\/traces/);
    await expect(page.getByTestId('traces-filter-chip-scope')).toContainText('support-agents');
    await expect(page.locator('[data-testid^="trace-row-"]')).toHaveCount(2);
    await expect(page.getByTestId(`trace-row-${looseCallId}`)).toHaveCount(0);
  });

  test('the traces scope filter narrows the list and a scoped trace links to its scope', async ({ page }) => {
    await page.goto('/traces', { waitUntil: 'load' });
    await expect(page.getByTestId(`trace-row-${looseCallId}`)).toBeVisible();

    await addTraceFilter(page, 'scope', scopeId);
    await expect(page.locator('[data-testid^="trace-row-"]')).toHaveCount(2);
    for (const id of scopedCallIds) {
      await expect(page.getByTestId(`trace-row-${id}`)).toBeVisible();
    }
    await expect(page.getByTestId(`trace-scope-tag-${scopeId}`).first()).toBeVisible();

    await page.getByTestId(`trace-row-${scopedCallIds[0]}`).click();
    await page.getByTestId('trace-scope-link').click();
    await expect(page).toHaveURL(new RegExp(`/scopes\\?id=${scopeId}`));
    await expect(page.getByTestId('scope-detail')).toBeVisible();
  });
});
