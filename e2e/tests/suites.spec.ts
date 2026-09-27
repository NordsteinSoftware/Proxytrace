import { test, expect } from '../helpers/fixtures';
import type { APIRequestContext } from '@playwright/test';
import { ProxytraceApiClient } from '../helpers/api-client';

// Test Suites / Test Cases coverage for the /suites page.
//
// Notes:
//  - The /suites page is a master–detail view: a suite list on the left, a detail panel on the
//    right (selected suite persisted in ?id=). Editing happens in the detail panel, not a modal.
//  - There is NO suite-rename endpoint: PUT /api/test-suites/{id} ignores `name`. So we never
//    assert a renamed suite name persists. Evaluator editing is folded into the attach/detach
//    tests below (they drive the detail panel's Evaluators tab and verify the change persists).
//  - The CreateSuiteWizard's Traces step needs agent calls to exist, so the wizard-driving test
//    seeds an agent + a couple of agent calls before opening the wizard.

function uniqueName(prefix: string): string {
  return `${prefix} ${Date.now()}-${Math.floor(Math.random() * 100000)}`;
}

/** Fresh, authenticated client for a test's own `request` fixture. */
async function makeClient(request: APIRequestContext): Promise<ProxytraceApiClient> {
  const client = new ProxytraceApiClient(request);
  const { token } = await client.login('admin@e2e.test', 'E2ePassword1!');
  client.setToken(token);
  return client;
}

/** List suites for a project via the raw endpoint (no list method on the api-client). */
async function listSuites(
  request: APIRequestContext,
  token: string,
  projectId: string,
): Promise<Array<{ id: string; name: string }>> {
  const res = await request.get(`/api/test-suites?projectId=${projectId}&pageSize=200`, {
    headers: { Authorization: `Bearer ${token}` },
  });
  if (!res.ok()) throw new Error(`list suites failed: ${res.status()} ${await res.text()}`);
  const body = (await res.json()) as { items: Array<{ id: string; name: string }> };
  return body.items;
}

test.describe('Test Suites', () => {
  let endpointId: string;
  let projectId: string;

  test.beforeAll(async ({ request }) => {
    const api = await makeClient(request);
    endpointId = await api.firstEndpointId();
    projectId = await api.firstProjectId();
  });

  test('create a suite end-to-end via the CreateSuiteWizard', async ({ page, request }) => {
    const client = await makeClient(request);
    const { token } = await client.login('admin@e2e.test', 'E2ePassword1!');

    // Seed an agent + a couple of agent calls so the wizard's Traces step has rows to pick.
    const agentName = uniqueName('Wizard Agent');
    const { id: agentId } = await client.createAgent({ name: agentName, endpointId });
    await client.seedAgentCall({ agentId, userContent: 'Wizard trace one', assistantContent: 'ok one' });
    await client.seedAgentCall({ agentId, userContent: 'Wizard trace two', assistantContent: 'ok two' });

    const suiteName = uniqueName('Wizard Suite');

    await page.goto('/suites', { waitUntil: 'load' });
    await expect(page.getByTestId('suite-list')).toBeVisible();

    await page.getByTestId('suite-create-btn').click();

    // Step 1: select agent.
    await expect(page.getByTestId('wizard-step-agent')).toBeVisible();
    await page.getByTestId(`wizard-agent-option-${agentId}`).click();
    await page.getByRole('button', { name: 'Next →' }).click();

    // Step 2: select traces (must pick at least one to advance).
    await expect(page.getByTestId('wizard-step-traces')).toBeVisible();
    const traceOption = page.locator('[data-testid^="wizard-trace-option-"]').first();
    await expect(traceOption).toBeVisible();
    await traceOption.click();
    await page.getByRole('button', { name: 'Next →' }).click();

    // Step 3: name the suite.
    await expect(page.getByTestId('wizard-step-name')).toBeVisible();
    await page.getByTestId('wizard-name-input').fill(suiteName);
    await page.getByRole('button', { name: 'Next →' }).click();

    // Step 4: evaluators (optional) — submit.
    await expect(page.getByTestId('wizard-step-evaluators')).toBeVisible();
    await page.getByRole('button', { name: 'Create suite' }).click();

    // Suite appears as a SuiteCard. Confirm via API read-back, then assert the card.
    await expect.poll(
      async () => (await listSuites(request, token, projectId)).some(s => s.name === suiteName),
      { timeout: 10_000, message: 'created suite did not appear in API' },
    ).toBeTruthy();

    const created = (await listSuites(request, token, projectId)).find(s => s.name === suiteName);
    expect(created, 'created suite present').toBeTruthy();

    await expect(page.getByTestId(`suite-card-${created!.id}`)).toBeVisible();
    await expect(page.getByTestId(`suite-card-${created!.id}`)).toContainText(suiteName);
  });

  test('SuiteCard shows the correct test-case and evaluator counts', async ({ page, request }) => {
    const client = await makeClient(request);

    const agentName = uniqueName('Counts Agent');
    const { id: agentId } = await client.createAgent({ name: agentName, endpointId });
    const call1 = await client.seedAgentCall({ agentId, userContent: 'c1', assistantContent: 'a1' });
    const call2 = await client.seedAgentCall({ agentId, userContent: 'c2', assistantContent: 'a2' });
    const { id: evaluatorId } = await client.createEvaluator(projectId);

    const { id: suiteId } = await client.createSuiteFromTraces(
      uniqueName('Counts Suite'),
      agentId,
      [call1.id, call2.id],
      [evaluatorId],
    );

    // API read-back: 2 cases, 1 evaluator.
    const fromApi = await client.getTestSuite(suiteId);
    expect(fromApi.testCases.length).toBe(2);
    expect(fromApi.evaluators.length).toBe(1);

    await page.goto('/suites', { waitUntil: 'load' });
    await expect(page.getByTestId(`suite-card-${suiteId}`)).toBeVisible();
    await expect(page.getByTestId(`suite-case-count-${suiteId}`)).toHaveText('2');
    await expect(page.getByTestId(`suite-evaluator-count-${suiteId}`)).toHaveText('1');
  });

  test('selecting a suite opens its detail panel with the stats strip and schedules', async ({ page, request }) => {
    const client = await makeClient(request);

    const agentName = uniqueName('Detail Agent');
    const { id: agentId } = await client.createAgent({ name: agentName, endpointId });
    const call1 = await client.seedAgentCall({ agentId, userContent: 'detail', assistantContent: 'ok' });
    const suiteName = uniqueName('Detail Suite');
    const { id: suiteId } = await client.createSuiteFromTraces(suiteName, agentId, [call1.id], []);

    await page.goto('/suites', { waitUntil: 'load' });
    await page.getByTestId(`suite-select-${suiteId}`).click();

    const detail = page.getByTestId('suite-detail');
    await expect(detail).toBeVisible();
    await expect(page.getByTestId('suite-detail-name')).toHaveText(suiteName);
    await expect(page.getByTestId('suite-stats-strip')).toBeVisible();

    // Schedules now live in their own tab inside the workspace card.
    await page.getByTestId('suite-tab-schedules').click();
    await expect(page.getByTestId('suite-schedules-section')).toBeVisible();

    // The bucket selector switches the window without error (stats strip stays present
    // above the tabs, independent of the active tab).
    await detail.getByRole('button', { name: 'Last 7 days' }).click();
    await expect(page.getByTestId('suite-stats-strip')).toBeVisible();
  });

  test('attach an evaluator from the detail panel increments the evaluator count', async ({ page, request }) => {
    const client = await makeClient(request);

    const agentName = uniqueName('Attach Agent');
    const { id: agentId } = await client.createAgent({ name: agentName, endpointId });
    const call1 = await client.seedAgentCall({ agentId, userContent: 'attach', assistantContent: 'ok' });
    // Promoting from traces with no explicit evaluators auto-attaches one default evaluator, so the
    // suite starts with exactly one; attaching another takes it to two.
    const { id: suiteId } = await client.createSuiteFromTraces(uniqueName('Attach Suite'), agentId, [call1.id], []);
    // A standalone evaluator to attach.
    const { id: evaluatorId } = await client.createEvaluator(projectId);

    await page.goto('/suites', { waitUntil: 'load' });
    await expect(page.getByTestId(`suite-evaluator-count-${suiteId}`)).toHaveText('1');

    await page.getByTestId(`suite-select-${suiteId}`).click();
    const detail = page.getByTestId('suite-detail');
    await expect(detail).toBeVisible();

    // Switch to the Evaluators tab, then toggle the evaluator on and save.
    await page.getByTestId('suite-tab-evaluators').click();
    await page.getByTestId(`edit-suite-evaluator-toggle-${evaluatorId}`).click();
    await page.getByTestId('edit-suite-save-btn').click();

    // API read-back confirms the evaluator is attached (default + the newly attached one = 2).
    await expect.poll(
      async () => (await client.getTestSuite(suiteId)).evaluators.length,
      { timeout: 10_000, message: 'evaluator attach did not persist' },
    ).toBe(2);

    await expect(page.getByTestId(`suite-evaluator-count-${suiteId}`)).toHaveText('2');
  });

  test('detach an evaluator from the detail panel decrements the evaluator count', async ({ page, request }) => {
    const client = await makeClient(request);

    const agentName = uniqueName('Detach Agent');
    const { id: agentId } = await client.createAgent({ name: agentName, endpointId });
    const call1 = await client.seedAgentCall({ agentId, userContent: 'detach', assistantContent: 'ok' });
    const { id: evaluatorId } = await client.createEvaluator(projectId);
    // Suite starts with one evaluator attached.
    const { id: suiteId } = await client.createSuiteFromTraces(
      uniqueName('Detach Suite'),
      agentId,
      [call1.id],
      [evaluatorId],
    );

    await page.goto('/suites', { waitUntil: 'load' });
    await expect(page.getByTestId(`suite-evaluator-count-${suiteId}`)).toHaveText('1');

    await page.getByTestId(`suite-select-${suiteId}`).click();
    const detail = page.getByTestId('suite-detail');
    await expect(detail).toBeVisible();

    await page.getByTestId('suite-tab-evaluators').click();
    // Toggling an attached evaluator detaches it.
    await page.getByTestId(`edit-suite-evaluator-toggle-${evaluatorId}`).click();
    await page.getByTestId('edit-suite-save-btn').click();

    await expect.poll(
      async () => (await client.getTestSuite(suiteId)).evaluators.length,
      { timeout: 10_000, message: 'evaluator detach did not persist' },
    ).toBe(0);

    await expect(page.getByTestId(`suite-evaluator-count-${suiteId}`)).toHaveText('0');
  });

  test('add a test case to a suite increments the case count in the UI', async ({ page, request }) => {
    const client = await makeClient(request);

    const agentName = uniqueName('Case Agent');
    const { id: agentId } = await client.createAgent({ name: agentName, endpointId });
    const call1 = await client.seedAgentCall({ agentId, userContent: 'first', assistantContent: 'r1' });
    const { id: suiteId } = await client.createSuiteFromTraces(uniqueName('Case Suite'), agentId, [call1.id], []);

    await page.goto('/suites', { waitUntil: 'load' });
    await expect(page.getByTestId(`suite-case-count-${suiteId}`)).toHaveText('1');

    // Promote a second agent call into the suite.
    const call2 = await client.seedAgentCall({ agentId, userContent: 'second', assistantContent: 'r2' });
    await client.createTestCase(suiteId, { fromAgentCallId: call2.id });

    // API read-back confirms 2 cases.
    expect((await client.getTestSuite(suiteId)).testCases.length).toBe(2);

    // The list refreshes on reload.
    await page.reload({ waitUntil: 'load' });
    await expect(page.getByTestId(`suite-case-count-${suiteId}`)).toHaveText('2');
  });

  // The trace drawer's Add to test suite is the in-context way to curate a case by hand (the
  // manual route beside Generate tests). It writes through the same suite endpoints the suite page
  // uses, so these two tests pin the UI wiring: append to a picked suite, and create-and-add when
  // the agent has no suite yet.
  test('add a trace to an existing suite from the trace drawer', async ({ page, request }) => {
    const client = await makeClient(request);

    const agentName = uniqueName('Drawer Agent');
    const { id: agentId } = await client.createAgent({ name: agentName, endpointId });
    const suiteSeed = await client.seedAgentCall({ agentId, userContent: 'suite seed', assistantContent: 'seed reply' });
    const call = await client.seedAgentCall({ agentId, userContent: 'drawer input', assistantContent: 'drawer reply' });
    const { id: suiteId } = await client.createSuiteFromTraces(uniqueName('Drawer Suite'), agentId, [suiteSeed.id], []);

    await page.goto(`/traces?trace=${call.id}`, { waitUntil: 'load' });
    await expect(page.getByTestId('trace-detail')).toBeVisible();
    await page.getByTestId('trace-add-test-btn').click();
    const modal = page.getByTestId('add-trace-test-modal');
    await expect(modal).toBeVisible();

    // Pick the destination explicitly; the picker lists this agent's suites.
    await page.getByTestId(`suite-option-${suiteId}`).click();
    await page.getByTestId('modal-panel').getByTestId('modal-submit').click();

    await expect(modal).toBeHidden();
    await expect.poll(
      async () => (await client.getTestSuite(suiteId)).testCases.length,
      { timeout: 10_000, message: 'the trace did not land in the suite' },
    ).toBe(2);

    // The snackbar is the hand-off: it reports the destination and takes the user there.
    const snackbarAction = page.getByTestId('toast-action-btn');
    await expect(snackbarAction).toBeVisible();
    await snackbarAction.click();
    await expect(page).toHaveURL(new RegExp(`/suites\\?id=${suiteId}`));
  });

  test('create a suite from a trace when the agent has none', async ({ page, request }) => {
    const client = await makeClient(request);
    const { token } = await client.login('admin@e2e.test', 'E2ePassword1!');

    const agentName = uniqueName('Drawer New Agent');
    const { id: agentId } = await client.createAgent({ name: agentName, endpointId });
    const call = await client.seedAgentCall({ agentId, userContent: 'create-suite input', assistantContent: 'create-suite reply' });
    const suiteName = uniqueName('Drawer New Suite');

    await page.goto(`/traces?trace=${call.id}`, { waitUntil: 'load' });
    await expect(page.getByTestId('trace-detail')).toBeVisible();
    await page.getByTestId('trace-add-test-btn').click();
    await expect(page.getByTestId('add-trace-test-modal')).toBeVisible();

    // No suites for this agent → the modal starts in create mode and its action waits for a name.
    const submit = page.getByTestId('modal-panel').getByTestId('modal-submit');
    await expect(submit).toBeDisabled();
    await page.getByTestId('modal-panel').getByTestId('trace-new-suite-name').fill(suiteName);
    await expect(submit).toBeEnabled();
    await submit.click();

    await expect(page.getByTestId('add-trace-test-modal')).toBeHidden();
    await expect.poll(
      async () => (await listSuites(request, token, projectId)).find(s => s.name === suiteName)?.id,
      { timeout: 10_000, message: 'the new suite did not appear' },
    ).toBeTruthy();
  });

  test('delete a suite removes its card', async ({ page, request }) => {
    const client = await makeClient(request);
    const { token } = await client.login('admin@e2e.test', 'E2ePassword1!');

    const agentName = uniqueName('Delete Agent');
    const { id: agentId } = await client.createAgent({ name: agentName, endpointId });
    const call1 = await client.seedAgentCall({ agentId, userContent: 'del', assistantContent: 'ok' });
    const suiteName = uniqueName('Delete Suite');
    const { id: suiteId } = await client.createSuiteFromTraces(suiteName, agentId, [call1.id], []);

    await page.goto('/suites', { waitUntil: 'load' });
    await expect(page.getByTestId(`suite-card-${suiteId}`)).toBeVisible();

    await page.getByTestId(`suite-delete-btn-${suiteId}`).click();
    // ConfirmDialog requires typing the suite name to enable the Delete button.
    await page.getByPlaceholder(suiteName).fill(suiteName);
    // Scope to the dialog — the auto-selected suite's detail header also has a "Delete" button.
    await page.getByTestId('modal-panel').getByRole('button', { name: 'Delete', exact: true }).click();

    await expect(page.getByTestId(`suite-card-${suiteId}`)).toBeHidden();

    // API read-back: suite gone.
    await expect.poll(
      async () => (await listSuites(request, token, projectId)).some(s => s.id === suiteId),
      { timeout: 10_000, message: 'deleted suite still present in API' },
    ).toBeFalsy();
  });

  test('agent filter lists only agents that own a suite', async ({ page, request }) => {
    const client = await makeClient(request);

    // One agent with a suite, one agent with none.
    const suiteAgentName = uniqueName('Suite Agent');
    const { id: suiteAgentId } = await client.createAgent({
      name: suiteAgentName,
      endpointId,
      systemMessage: uniqueName('suite-agent-prompt'),
    });
    const evaluator = await client.createEvaluator(projectId);
    await client.createTestSuite(uniqueName('Filterable Suite'), suiteAgentId, [evaluator.id], [
      { userContent: 'hi', expectedContent: 'hello' },
    ]);

    const emptyAgentName = uniqueName('Empty Agent');
    await client.createAgent({
      name: emptyAgentName,
      endpointId,
      systemMessage: uniqueName('empty-agent-prompt'),
    });

    await page.goto('/suites', { waitUntil: 'load' });
    await expect(page.getByTestId('suite-list')).toBeVisible();

    // Open the agent filter dropdown.
    await page.getByRole('button', { name: /All agents/ }).click();

    // The suite-owning agent is offered; the suite-less agent is not.
    await expect(page.getByRole('menuitem', { name: new RegExp(suiteAgentName) })).toBeVisible();
    await expect(page.getByRole('menuitem', { name: new RegExp(emptyAgentName) })).toHaveCount(0);

    // Selecting the suite-owning agent narrows the list to its suite.
    await page.getByRole('menuitem', { name: new RegExp(suiteAgentName) }).click();
    await expect(page.getByTestId('suite-list')).toContainText('Filterable Suite');
  });
});
