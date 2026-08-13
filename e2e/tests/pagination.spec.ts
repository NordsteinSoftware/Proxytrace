import { test, expect } from '../helpers/fixtures';
import { ProxytraceApiClient } from '../helpers/api-client';

// Pagination + filtering, asserted at the API level (robust, no fragile UI paging). Each paged
// list endpoint returns the standard PagedResult envelope `{ items, total, page, pageSize }`
// (Nordstein.Core.Domain.Paging.PagedResult, serialized camelCase). Paging.Clamp enforces page >= 1
// and 1 <= pageSize <= 100, so a pageSize of 5 is honoured verbatim.
//
//   • /api/agents          — AgentsController.GetAll (page/pageSize, optional projectId filter)
//   • /api/test-suites     — TestSuitesController.GetAll (page/pageSize, optional agentId filter)
//   • /api/test-run-groups — TestRunGroupsController.GetAll (page/pageSize)
//
// We seed a SMALL number of rows and use pageSize:5 rather than seeding past the default page size
// of 50 — far faster while still exercising the page-window + total accounting.

const ADMIN_EMAIL = 'admin@e2e.test';
const ADMIN_PASSWORD = 'E2ePassword1!';

test.describe('Pagination & filtering', () => {
  let api: ProxytraceApiClient;
  let projectId: string;
  let endpointId: string;

  test.beforeAll(async ({ request }) => {
    api = new ProxytraceApiClient(request);
    const { token } = await api.login(ADMIN_EMAIL, ADMIN_PASSWORD);
    api.setToken(token);
    projectId = await api.firstProjectId();
    endpointId = await api.firstEndpointId();
  });

  test('agents list pages with a small pageSize', async () => {
    // Seed more agents than the page window so a second page is guaranteed to exist.
    const stamp = Date.now();
    for (let i = 0; i < 6; i++) {
      await api.createAgent({ name: `E2E Page Agent ${stamp}-${i}`, endpointId, projectId });
    }

    const page1 = await api.listAgentsPaged({ page: 1, pageSize: 5 });
    expect(page1.items.length).toBeLessThanOrEqual(5);
    // total reflects the real (unpaged) count, which must exceed one page after seeding 6.
    expect(page1.total).toBeGreaterThan(5);

    const page2 = await api.listAgentsPaged({ page: 2, pageSize: 5 });
    expect(page2.items.length).toBeGreaterThan(0);
    expect(page2.total).toBe(page1.total);

    // Page 2 is a genuinely different window — no id overlaps page 1.
    const idsPage1 = new Set(page1.items.map((a) => a.id));
    expect(page2.items.every((a) => !idsPage1.has(a.id))).toBeTruthy();
  });

  test('suites list pages with a small pageSize', async () => {
    const stamp = Date.now();
    // Suites need an agent; one agent can own several suites.
    const { id: agentId } = await api.createAgent({ name: `E2E Page Suite Agent ${stamp}`, endpointId, projectId });
    for (let i = 0; i < 6; i++) {
      await api.createTestSuite(`E2E Page Suite ${stamp}-${i}`, agentId, [], [
        { userContent: 'q', expectedContent: 'a' },
      ]);
    }

    const page1 = await api.listSuites({ page: 1, pageSize: 5 });
    expect(page1.items.length).toBeLessThanOrEqual(5);
    expect(page1.total).toBeGreaterThan(5);

    const page2 = await api.listSuites({ page: 2, pageSize: 5 });
    expect(page2.total).toBe(page1.total);
    const idsPage1 = new Set(page1.items.map((s) => s.id));
    expect(page2.items.every((s) => !idsPage1.has(s.id))).toBeTruthy();
  });

  test('run groups list pages with a small pageSize', async () => {
    const stamp = Date.now();
    const { id: agentId } = await api.createAgent({ name: `E2E Page Run Agent ${stamp}`, endpointId, projectId });

    // A run group needs a suite; the background run targets a (fake) endpoint and may fail, but the
    // group row is persisted immediately on create, which is all pagination needs.
    for (let i = 0; i < 6; i++) {
      const { id: suiteId } = await api.createTestSuite(`E2E Run Suite ${stamp}-${i}`, agentId, [], [
        { userContent: 'q', expectedContent: 'a' },
      ]);
      await api.createTestRunGroup(suiteId, [endpointId]);
    }

    const page1 = await api.listTestRunGroups({ page: 1, pageSize: 5 });
    expect(page1.items.length).toBeLessThanOrEqual(5);
    expect(page1.total).toBeGreaterThan(5);

    const page2 = await api.listTestRunGroups({ page: 2, pageSize: 5 });
    expect(page2.total).toBe(page1.total);
    const idsPage1 = new Set(page1.items.map((g) => g.id));
    expect(page2.items.every((g) => !idsPage1.has(g.id))).toBeTruthy();
  });

  test('agents filter by projectId returns only that project\'s agents', async () => {
    const stamp = Date.now();
    // A fresh isolated project so its agent set is deterministic.
    const { id: isolatedProjectId } = await api.createProject(`E2E Filter Project ${stamp}`, endpointId);
    const { id: agentId } = await api.createAgent({
      name: `E2E Filter Agent ${stamp}`,
      endpointId,
      projectId: isolatedProjectId,
    });

    const filtered = await api.listAgentsPaged({ projectId: isolatedProjectId, pageSize: 100 });
    // Every project carries a built-in Tracey system agent (auto-provisioned at create), so the
    // list isn't empty — compare against the user agents only. The default project's agents must
    // NOT leak in: the sole non-system agent here is exactly the one we seeded.
    const userAgents = filtered.items.filter((a) => !a.isSystemAgent);
    expect(userAgents.map((a) => a.id)).toEqual([agentId]);
  });

  test('suites filter by agentId returns only that agent\'s suites', async () => {
    const stamp = Date.now();
    const { id: agentId } = await api.createAgent({ name: `E2E Suite Filter Agent ${stamp}`, endpointId, projectId });
    const { id: otherAgentId } = await api.createAgent({ name: `E2E Other Agent ${stamp}`, endpointId, projectId });

    const { id: mineId } = await api.createTestSuite(`E2E Mine Suite ${stamp}`, agentId, [], [
      { userContent: 'q', expectedContent: 'a' },
    ]);
    await api.createTestSuite(`E2E Theirs Suite ${stamp}`, otherAgentId, [], [
      { userContent: 'q', expectedContent: 'a' },
    ]);

    const filtered = await api.listSuites({ agentId, pageSize: 100 });
    expect(filtered.items.some((s) => s.id === mineId)).toBeTruthy();
    // Only this agent's suite — nothing from the other agent.
    expect(filtered.total).toBe(1);
    expect(filtered.items).toHaveLength(1);
    expect(filtered.items[0].id).toBe(mineId);
  });

  test('the /agents page renders its list (UI smoke for pagination)', async ({ page }) => {
    // One lightweight UI check: the agents list mounts. Seed one agent so the list (not the empty
    // state) renders — the DB is reset to the setup baseline before each test.
    await api.createAgent({ name: `E2E Page UI Agent ${Date.now()}`, endpointId, projectId });

    await page.goto('/agents', { waitUntil: 'load' });
    await expect(page.getByTestId('agent-list')).toBeVisible();
  });
});
