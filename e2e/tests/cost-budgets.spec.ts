import { test, expect } from '../helpers/fixtures';
import type { APIRequestContext } from '@playwright/test';
import { ProxytraceApiClient } from '../helpers/api-client';

// Monthly cost budgets on the /costs page.
//
// Configuring a budget is admin-only AND gated behind the CostControls (Enterprise) license
// feature; the default e2e stack (the `core` project, :5101) is Enterprise-licensed, so the
// "New budget" button is live here. Reading the page and the budget list is free on every tier —
// the Free-tier :5103 stack is exercised separately by licensing.spec.ts.
//
// The spec covers the configuration half end-to-end (create through the UI, verify through the
// API, see the consumption meter render). The hard-block 403 round trip is deliberately NOT
// asserted: it needs the periodic guard to fire, which is a 5-minute interval by default, so
// asserting it here would either sleep for minutes or need a test-only guard interval.
//
// Stable data-testids: `costs-page`, `cost-kpis`, `cost-over-time`, `cost-by-agent`,
// `budget-section`, `budget-create-btn`, `budget-upgrade-btn`, `budget-empty-state`,
// `budget-list`, `budget-row-<id>`, `budget-scope-<id>`, `budget-spend-<id>`,
// `budget-edit-btn-<id>`, and the editor (`budget-editor`, `budget-soft-input`,
// `budget-hard-input`, `budget-enabled-switch`, `budget-save-btn`, `budget-delete-btn`,
// `budget-editor-error`).

/** Fresh, authenticated client for a test's own `request` fixture. */
async function makeClient(request: APIRequestContext): Promise<ProxytraceApiClient> {
  const client = new ProxytraceApiClient(request);
  const { token } = await client.login('admin@e2e.test', 'E2ePassword1!');
  client.setToken(token);
  return client;
}

test.describe('Cost budgets', () => {
  let api: ProxytraceApiClient;
  let projectId: string;

  test.beforeEach(async ({ request }) => {
    // The DB is reset to the setup baseline before each core test, so seed per test. The setup
    // project survives the reset; resolve it deterministically.
    api = await makeClient(request);
    projectId = await api.firstProjectId();

    // Budgets are NOT per-run content, so the reset does not remove them — clear any left behind
    // by a previous test in this file so the empty-state assertions below are meaningful.
    for (const limit of await api.listCostLimits(projectId)) {
      await api.deleteCostLimit(limit.id);
    }
  });

  test('page renders its summary sections with no budgets configured', async ({ page }) => {
    await page.goto('/costs', { waitUntil: 'load' });

    await expect(page.getByTestId('costs-page')).toBeVisible();
    await expect(page.getByTestId('cost-kpis')).toBeVisible();
    await expect(page.getByTestId('cost-over-time')).toBeVisible();
    await expect(page.getByTestId('cost-by-agent')).toBeVisible();
    await expect(page.getByTestId('budget-empty-state')).toBeVisible();
  });

  test('an admin creates a project budget and sees its consumption meter', async ({ page }) => {
    await page.goto('/costs', { waitUntil: 'load' });

    await page.getByTestId('budget-create-btn').click();
    await expect(page.getByTestId('budget-editor')).toBeVisible();

    await page.getByTestId('budget-soft-input').fill('25');
    await page.getByTestId('budget-hard-input').fill('50');
    await page.getByTestId('budget-save-btn').click();

    // Verify through the API — that is the durable record; then assert the UI reflects it.
    await expect
      .poll(async () => (await api.listCostLimits(projectId)).length, {
        timeout: 15_000,
        message: 'budget was not persisted',
      })
      .toBe(1);

    const [limit] = await api.listCostLimits(projectId);
    expect(limit.softLimitEur).toBe(25);
    expect(limit.hardLimitEur).toBe(50);
    expect(limit.agentId).toBeNull();

    await expect(page.getByTestId(`budget-row-${limit.id}`)).toBeVisible();
    await expect(page.getByTestId(`budget-spend-${limit.id}`)).toBeVisible();
  });

  test('the editor refuses a soft limit above the hard limit', async ({ page }) => {
    await page.goto('/costs', { waitUntil: 'load' });

    await page.getByTestId('budget-create-btn').click();
    await page.getByTestId('budget-soft-input').fill('200');
    await page.getByTestId('budget-hard-input').fill('100');

    // A soft threshold above the hard one could never fire — the hard limit blocks first.
    await expect(page.getByTestId('budget-editor-error')).toBeVisible();
    await expect(page.getByTestId('budget-save-btn')).toBeDisabled();
  });

  test('editing a budget clears its breach state so a raised limit stops blocking', async ({ page }) => {
    const created = await api.createCostLimit({ projectId, softLimitEur: 1, hardLimitEur: 2 });

    await page.goto('/costs', { waitUntil: 'load' });
    await page.getByTestId(`budget-edit-btn-${created.id}`).click();
    await page.getByTestId('budget-hard-input').fill('500');
    await page.getByTestId('budget-save-btn').click();

    await expect
      .poll(async () => (await api.listCostLimits(projectId))[0]?.hardLimitEur, {
        timeout: 15_000,
        message: 'raised hard limit was not persisted',
      })
      .toBe(500);

    // The overview reports breach state; after an edit the budget must read as un-breached.
    const overview = await api.costOverview({
      projectId,
      from: new Date(Date.now() - 24 * 60 * 60 * 1000).toISOString(),
      to: new Date().toISOString(),
      bucket: 'daily',
    });
    const budget = overview.budgets.find(b => b.costLimitId === created.id);
    expect(budget?.hardBreached).toBe(false);
  });

  test('an agent-scoped budget is listed under its agent name', async ({ page }) => {
    const endpointId = await api.firstEndpointId();
    const agent = await api.createAgent({ name: `Budget Agent ${Date.now()}`, endpointId, projectId });
    const created = await api.createCostLimit({ projectId, agentId: agent.id, hardLimitEur: 10 });

    await page.goto('/costs', { waitUntil: 'load' });

    await expect(page.getByTestId(`budget-scope-${created.id}`)).toHaveText(agent.name);
  });

  test('deleting a budget removes it from the list', async ({ page }) => {
    const created = await api.createCostLimit({ projectId, softLimitEur: 10, hardLimitEur: 20 });

    await page.goto('/costs', { waitUntil: 'load' });
    await page.getByTestId(`budget-edit-btn-${created.id}`).click();
    await page.getByTestId('budget-delete-btn').click();

    await expect
      .poll(async () => (await api.listCostLimits(projectId)).length, {
        timeout: 15_000,
        message: 'budget was not deleted',
      })
      .toBe(0);

    await expect(page.getByTestId('budget-empty-state')).toBeVisible();
  });
});
