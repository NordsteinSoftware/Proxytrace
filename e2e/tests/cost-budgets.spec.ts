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
// `cost-by-api-key`, `cost-api-key-list`, `cost-api-key-row-<id>`,
// `cost-api-key-row-unattributed`, `cost-dimension-agent`, `cost-dimension-api-key`,
// `budget-section`, `budget-create-btn` (the one and only create action, in the budgets card
// header), `budget-upgrade-btn`, `budget-empty-state`,
// `budget-list`, `budget-row-<id>`, `budget-scope-<id>`, `budget-spend-<id>`,
// `budget-edit-btn-<id>`, `budget-delete-btn-<id>` (row-level; the delete confirmation is the
// shared `.modal-panel` ConfirmDialog), and the editor (`budget-editor`,
// `budget-scope-select` — the scope *kind*, with `budget-scope-select-option-{project,agent,apiKey}`
// derived by `Select` — `budget-scope-element` (+ `budget-scope-element-option-<id>`) for the
// agent/key picker, `budget-scope-empty`, `budget-scope-stale`, `budget-scope-locked-kind`,
// `budget-scope-locked-element`, `budget-scope-help`, `budget-soft-input`, `budget-hard-input`,
// `budget-enabled-switch`, `budget-save-btn`, `budget-cancel-btn`, `budget-editor-error`,
// `budget-save-error`).

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

  test('a key-scoped budget is listed under its key name and marked as a key budget', async ({ page }) => {
    const providerId = await api.firstProviderId();
    const key = await api.createApiKeyForProject(providerId, `Budget Key ${Date.now()}`, projectId);
    const created = await api.createCostLimit({ projectId, apiKeyId: key.id, hardLimitEur: 10 });

    await page.goto('/costs', { waitUntil: 'load' });

    await expect(page.getByTestId(`budget-scope-${created.id}`)).toHaveText(key.name);
    // The list must distinguish key budgets from agent ones: they enforce differently, since a key
    // budget cannot be dodged by omitting the agent header.
    await expect(page.getByTestId(`budget-row-${created.id}`)).toContainText('API Key');
  });

  test('a key budget coexists with the project-wide budget', async ({ page }) => {
    const providerId = await api.firstProviderId();
    const key = await api.createApiKeyForProject(providerId, `Coexist Key ${Date.now()}`, projectId);

    const projectWide = await api.createCostLimit({ projectId, hardLimitEur: 100 });
    const keyScoped = await api.createCostLimit({ projectId, apiKeyId: key.id, hardLimitEur: 10 });

    // Regression guard for the partial unique index: its filter names BOTH scope columns, so a
    // key-scoped row does not collide with the project-wide one. On Postgres only — the in-memory
    // provider used by unit tests ignores index filters entirely.
    await page.goto('/costs', { waitUntil: 'load' });

    await expect(page.getByTestId(`budget-row-${projectWide.id}`)).toBeVisible();
    await expect(page.getByTestId(`budget-row-${keyScoped.id}`)).toBeVisible();
  });

  test('the API rejects a budget scoped to an agent and a key at once', async () => {
    const providerId = await api.firstProviderId();
    const key = await api.createApiKeyForProject(providerId, `Both Key ${Date.now()}`, projectId);
    const endpointId = await api.firstEndpointId();
    const agent = await api.createAgent({ name: `Both Agent ${Date.now()}`, endpointId, projectId });

    const res = await api.tryCreateCostLimit({
      projectId,
      agentId: agent.id,
      apiKeyId: key.id,
      softLimitEur: null,
      hardLimitEur: 10,
      enabled: true,
    });

    // A budget has exactly one scope; the cross-product is not a thing the proxy can enforce.
    expect(res.status()).toBe(400);
  });

  test('the cost overview reports per-key totals alongside per-agent ones', async () => {
    const now = new Date();
    const from = new Date(Date.UTC(now.getUTCFullYear(), now.getUTCMonth(), 1)).toISOString();

    const overview = await api.costOverview({ projectId, from, to: now.toISOString() });

    // Shape assertion only: whether any key has spend depends on what this run proxied, but the
    // arrays must always be present so the page can render its breakdown.
    expect(Array.isArray(overview.apiKeyTotals)).toBe(true);
    expect(Array.isArray(overview.apiKeySeries)).toBe(true);
  });

  test('deleting a budget removes it from the list', async ({ page }) => {
    const created = await api.createCostLimit({ projectId, softLimitEur: 10, hardLimitEur: 20 });

    await page.goto('/costs', { waitUntil: 'load' });
    // Delete lives on the row behind a confirmation, not one gap away from Save in the editor.
    await page.getByTestId(`budget-delete-btn-${created.id}`).click();
    await page.locator('.modal-panel').getByRole('button', { name: 'Delete', exact: true }).click();

    await expect
      .poll(async () => (await api.listCostLimits(projectId)).length, {
        timeout: 15_000,
        message: 'budget was not deleted',
      })
      .toBe(0);

    await expect(page.getByTestId('budget-empty-state')).toBeVisible();
  });

  // ── The three defects this suite exists to keep fixed ────────────────────────────────────────

  test('a second budget can be created, scoped to an agent, once the project one exists', async ({ page }) => {
    // The regression: the editor always opened on the project scope and offered it even when it
    // was taken, so every second budget ended in an opaque 409 with the dialog left open.
    await api.createCostLimit({ projectId, hardLimitEur: 100 });
    const endpointId = await api.firstEndpointId();
    const agent = await api.createAgent({ name: `Second Budget Agent ${Date.now()}`, endpointId, projectId });

    await page.goto('/costs', { waitUntil: 'load' });
    await page.getByTestId('budget-create-btn').click();

    // The dialog opens on a scope that is actually free rather than on the taken project one...
    await expect(page.getByTestId('budget-scope-element')).toBeVisible();
    // ...and picking the taken scope explains itself instead of failing on Save.
    await page.getByTestId('budget-scope-select').click();
    await page.getByTestId('budget-scope-select-option-project').click();
    await expect(page.getByTestId('budget-scope-empty')).toBeVisible();
    await expect(page.getByTestId('budget-save-btn')).toBeDisabled();

    await page.getByTestId('budget-scope-select').click();
    await page.getByTestId('budget-scope-select-option-agent').click();

    await page.getByTestId('budget-scope-element').click();
    await page.getByTestId(`budget-scope-element-option-${agent.id}`).click();
    await page.getByTestId('budget-hard-input').fill('30');
    await page.getByTestId('budget-save-btn').click();

    await expect
      .poll(async () => (await api.listCostLimits(projectId)).filter(l => l.agentId === agent.id).length, {
        timeout: 15_000,
        message: 'the second (agent-scoped) budget was not persisted',
      })
      .toBe(1);

    // Both budgets coexist, and the new row appears without waiting on the heavy overview refetch.
    const [agentLimit] = (await api.listCostLimits(projectId)).filter(l => l.agentId === agent.id);
    await expect(page.getByTestId(`budget-row-${agentLimit.id}`)).toBeVisible();
    await expect(page.getByTestId('budget-list').getByTestId(/^budget-row-/)).toHaveCount(2);
  });

  test('editing an agent budget shows the agent name, never its id', async ({ page }) => {
    // The reported bug: a budgeted agent is filtered out of the pickable roster, so the old single
    // Select could not resolve its own value and printed the raw `agent:<uuid>`.
    const endpointId = await api.firstEndpointId();
    const agent = await api.createAgent({ name: `Named Scope Agent ${Date.now()}`, endpointId, projectId });
    const created = await api.createCostLimit({ projectId, agentId: agent.id, hardLimitEur: 10 });

    await page.goto('/costs', { waitUntil: 'load' });
    await page.getByTestId(`budget-edit-btn-${created.id}`).click();

    await expect(page.getByTestId('budget-scope-locked-kind')).toHaveText('Agent');
    const element = page.getByTestId('budget-scope-locked-element');
    await expect(element).toHaveText(agent.name);
    await expect(element).not.toContainText(agent.id);
  });

});
