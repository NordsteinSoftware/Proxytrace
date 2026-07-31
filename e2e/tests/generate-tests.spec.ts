import { randomUUID } from 'crypto';
import { test, expect } from '../helpers/fixtures';
import { ProxytraceApiClient } from '../helpers/api-client';

const ADMIN_EMAIL = 'admin@e2e.test';
const ADMIN_PASSWORD = 'E2ePassword1!';

// The Generate-tests panel hands a captured conversation to the project's system endpoint and asks
// it which turns are worth testing, so this is a real LLM round-trip: @llm-gated, generously timed,
// and asserted on the parts the model does not get to choose. WHICH turns it proposes, and how
// many, is its call; that it proposes something, that the panel approves exactly what was ticked,
// and that exactly those cases land in the destination suite is not.
test.describe('@llm Generate test cases from a trace', () => {
  test.skip(!process.env.OPENAI_API_KEY, 'requires OPENAI_API_KEY env var');

  let api: ProxytraceApiClient;
  let suiteId: string;
  let decidingCallId: string;

  test.beforeEach(async ({ request }) => {
    api = new ProxytraceApiClient(request);
    const { token } = await api.login(ADMIN_EMAIL, ADMIN_PASSWORD);
    api.setToken(token);

    const endpointId = await api.firstEndpointId();
    const projectId = await api.firstProjectId();

    // Self-seed everything: the DB reset does not run for @llm projects, so the spec must depend on
    // no other spec's data (and unique names keep the suite picker unambiguous).
    const stamp = Date.now();
    const agentId = (await api.createAgent({
      name: `E2E Synthesis Agent ${stamp}`,
      endpointId,
      projectId,
      systemMessage:
        'You are a customer-support agent for an online shop. Always look up the order with '
        + `lookup_order before issuing a refund with issue_refund. [${stamp}]`,
    })).id;

    // A three-turn refund conversation. The first two turns are decisions — the agent chose a tool
    // and its arguments; the third is only a summary of what already happened. A good proposal set
    // targets the decisions.
    const conversationId = randomUUID();
    const turns = [
      {
        userContent: 'I want a refund for order 91 — the jacket arrived torn.',
        assistantContent: 'Let me pull up order 91.',
        toolNames: ['lookup_order'],
      },
      {
        userContent: 'The order shows the torn jacket, £40. Refund it.',
        assistantContent: 'Issuing the refund for £40 now.',
        toolNames: ['issue_refund'],
      },
      {
        userContent: 'The refund succeeded.',
        assistantContent: 'Your £40 refund for order 91 is on its way. Anything else I can help with?',
      },
    ];

    const seededIds: string[] = [];
    for (const turn of turns) {
      const call = await api.seedAgentCall({
        agentId,
        conversationId,
        systemContent: 'You are a customer-support agent. Look up the order before issuing a refund.',
        ...turn,
      });
      seededIds.push(call.id);
    }
    decidingCallId = seededIds[0];

    // The destination suite, seeded from a trace OUTSIDE the conversation so the baseline case
    // count is independent of what the agent proposes. from-traces with no evaluators auto-attaches
    // one ExactMatch, which is what a suite of promoted traces normally looks like.
    const seedCall = await api.seedAgentCall({
      agentId,
      userContent: 'Do you sell jackets?',
      assistantContent: 'Yes, we do.',
    });
    suiteId = (await api.createSuiteFromTraces(
      `E2E Synthesis Suite ${stamp}`,
      agentId,
      [seedCall.id],
      [],
    )).id;
    expect((await api.getTestSuite(suiteId)).testCases.length).toBe(1);
  });

  test('proposes cases from the conversation and adds the approved ones to a suite', async ({ page }) => {
    // One synthesis round is a full model round-trip over the transcript; allow well beyond it.
    test.setTimeout(240_000);

    // ?trace= opens the detail drawer by id — no scrolling to find the row, and no dependence on
    // the list's time range or on expanding the conversation group.
    await page.goto(`/traces?trace=${decidingCallId}`, { waitUntil: 'load' });
    await expect(page.getByTestId('trace-detail')).toBeVisible();

    await page.getByTestId('generate-tests-btn').click();
    const modal = page.getByTestId('synthesize-tests-modal');
    await expect(modal).toBeVisible();

    // Pick the destination first: the request carries the suite so the agent can see how the cases
    // would be scored, and it decides its judge suggestion from that.
    await page.getByTestId(`promote-suite-option-${suiteId}`).click();

    // Nothing is generated until asked — that is the point of the explicit button.
    await expect(page.getByTestId('synthesis-proposal-list')).toHaveCount(0);
    await page.getByTestId('synthesize-generate-btn').click();

    await expect(page.getByTestId('synthesis-proposal-list')).toBeVisible({ timeout: 180_000 });

    const toggles = page.locator('[data-testid^="synthesis-proposal-toggle-"]');
    const proposalCount = await toggles.count();
    expect(
      proposalCount,
      'a conversation with two tool decisions should yield at least one candidate',
    ).toBeGreaterThan(0);

    // HOW MANY candidates the model returns is its judgement and varies run to run (a three-turn
    // refund conversation has come back with one and with two), so approve up to two rather than
    // asserting a count the model never promised. What is not its call: the approved ones, and only
    // those, must land in the suite.
    const approved = Math.min(2, proposalCount);
    // Some candidates arrive pre-checked (high-relevance promotions), so normalise rather than
    // assume — check the ones we want, clear the rest.
    for (let i = 0; i < proposalCount; i++) {
      if (i < approved) await toggles.nth(i).check();
      else await toggles.nth(i).uncheck();
    }

    // The agent may also suggest an agentic judge, and the panel defaults to its recommendation —
    // which can be a NEW suite. Decline it so the cases go to the suite we picked. The button only
    // renders while a judge is chosen, so its absence means there was nothing to decline.
    const declineJudge = page.getByTestId('synthesis-judge-decline-btn');
    if (await declineJudge.count() > 0) await declineJudge.click();

    const submit = page.getByTestId('synthesize-submit-btn');
    await expect(submit).toContainText(String(approved));
    await submit.click();

    await expect(modal).toBeHidden();
    await expect.poll(
      async () => (await api.getTestSuite(suiteId)).testCases.length,
      { timeout: 30_000, intervals: [1_000], message: 'the approved proposals did not land in the suite' },
    ).toBe(1 + approved);
  });
});
