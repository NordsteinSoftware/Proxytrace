import { describe, it, expect, vi, beforeEach } from 'vitest';
import { TestRunStatus } from '../../../api/models';

const { agentsApi, testSuitesApi, testCasesApi, testRunGroupsApi } = vi.hoisted(() => ({
  agentsApi: { get: vi.fn() },
  testSuitesApi: {
    get: vi.fn(), create: vi.fn(), createWithCases: vi.fn(),
    addTestCase: vi.fn(), removeTestCase: vi.fn(), updateEvaluators: vi.fn(),
  },
  testCasesApi: { update: vi.fn() },
  testRunGroupsApi: { get: vi.fn(), cancel: vi.fn() },
}));
vi.mock('../../../api/agents', () => ({ agentsApi }));
vi.mock('../../../api/test-suites', () => ({ testSuitesApi }));
vi.mock('../../../api/test-cases', () => ({ testCasesApi }));
vi.mock('../../../api/test-run-groups', () => ({ testRunGroupsApi }));

import { createSuiteTools } from './suites';
import { createRunTools } from './runs';
import { CANCELLED } from './shared';
import type { TraceyTool, TraceyToolContext } from './shared';

// store echoes the digest back so the returned result is assertable. Async to satisfy StoreFn.
const store = vi.fn(async (_kind: string, _full: unknown, summary: unknown) => summary);

// `execute` is optional on the tool type, but every tool under test defines it.
function run(t: TraceyTool, args: Record<string, unknown>, ctx: TraceyToolContext) {
  if (!t.execute) throw new Error('tool has no execute');
  return t.execute(args, ctx);
}

function makeCtx(confirmValue = true): TraceyToolContext {
  return {
    projectId: 'p1',
    artifactScope: 'u:p',
    navigate: vi.fn(),
    confirm: vi.fn().mockResolvedValue(confirmValue),
    loadedSkillIds: new Set<string>(),
  };
}

const suite = (over: Record<string, unknown> = {}) => ({
  id: 's1', name: 'My suite', agentName: 'A', testCases: [{ id: 'c1' }, { id: 'c2' }], passRate: 50, ...over,
});

beforeEach(() => vi.clearAllMocks());

describe('create_suite', () => {
  it('confirms, creates from traces, and returns a compact digest', async () => {
    const ctx = makeCtx();
    agentsApi.get.mockResolvedValue({ id: 'a1', name: 'A' });
    testSuitesApi.createWithCases.mockResolvedValue(suite({ testCases: [{ id: 'c1', sourceAgentCallId: 'call1' }] }));

    const tool = createSuiteTools(ctx, store).create_suite;
    const result = await run(
      tool, { name: 'My suite', agentId: 'a1', cases: [{ agentCallId: 'call1' }] }, ctx,
    );

    expect(ctx.confirm).toHaveBeenCalledOnce();
    expect(testSuitesApi.createWithCases).toHaveBeenCalledWith({
      name: 'My suite', agentId: 'a1', testCases: [{ fromAgentCallId: 'call1' }],
    });
    expect(result).toMatchObject({ id: 's1', name: 'My suite', caseCount: 1 });
  });

  it('passes explicit evaluator ids through to the API', async () => {
    const ctx = makeCtx();
    agentsApi.get.mockResolvedValue({ id: 'a1', name: 'A' });
    testSuitesApi.createWithCases.mockResolvedValue(suite());

    const tool = createSuiteTools(ctx, store).create_suite;
    await run(tool, { name: 'My suite', agentId: 'a1', cases: [{ agentCallId: 'call1' }], evaluatorIds: ['e1', 'e2'] }, ctx);

    expect(testSuitesApi.createWithCases).toHaveBeenCalledWith({
      name: 'My suite', agentId: 'a1', testCases: [{ fromAgentCallId: 'call1' }], evaluatorIds: ['e1', 'e2'],
    });
  });

  it('returns notFound for a missing agent and never creates', async () => {
    const ctx = makeCtx();
    agentsApi.get.mockResolvedValue(null);
    const tool = createSuiteTools(ctx, store).create_suite;
    const result = await run(tool, { name: 'x', agentId: 'bad', cases: [{ agentCallId: 'c' }] }, ctx);
    expect(result).toEqual({ notFound: 'bad' });
    expect(testSuitesApi.createWithCases).not.toHaveBeenCalled();
  });

  it('returns CANCELLED on decline and never creates', async () => {
    const ctx = makeCtx(false);
    agentsApi.get.mockResolvedValue({ id: 'a1', name: 'A' });
    const tool = createSuiteTools(ctx, store).create_suite;
    const result = await run(tool, { name: 'x', agentId: 'a1', cases: [{ agentCallId: 'c' }] }, ctx);
    expect(result).toBe(CANCELLED);
    expect(testSuitesApi.createWithCases).not.toHaveBeenCalled();
  });
});

describe('correction cases', () => {
  it('create_suite posts the corrected expected output and reports the new case id', async () => {
    const ctx = makeCtx();
    agentsApi.get.mockResolvedValue({ id: 'a1', name: 'A' });
    testSuitesApi.createWithCases.mockResolvedValue(suite({
      testCases: [{ id: 'c9', sourceAgentCallId: 'call1' }],
    }));

    const tool = createSuiteTools(ctx, store).create_suite;
    const result = await run(tool, {
      name: 'Refund policy', agentId: 'a1',
      cases: [{ agentCallId: 'call1', expectedOutput: 'Refund refused.' }],
      evaluatorIds: ['e1'],
    }, ctx);

    expect(testSuitesApi.createWithCases).toHaveBeenCalledWith({
      name: 'Refund policy', agentId: 'a1', evaluatorIds: ['e1'],
      testCases: [{ fromAgentCallId: 'call1', expectedOutput: { role: 'assistant', content: 'Refund refused.' } }],
    });
    expect(result).toMatchObject({
      addedCases: [{ caseId: 'c9', agentCallId: 'call1', isCorrection: true }],
    });
  });

  it('add_to_suite passes the correction through and diffs out the new case id', async () => {
    const ctx = makeCtx();
    testSuitesApi.get.mockResolvedValue(suite({ testCases: [{ id: 'c1' }] }));
    testSuitesApi.addTestCase.mockResolvedValue(suite({
      testCases: [{ id: 'c1' }, { id: 'c2', sourceAgentCallId: 'call1' }],
    }));

    const tool = createSuiteTools(ctx, store).add_to_suite;
    const result = await run(tool, {
      suiteId: 's1', cases: [{ agentCallId: 'call1', expectedOutput: 'Refund refused.' }],
    }, ctx);

    expect(testSuitesApi.addTestCase).toHaveBeenCalledWith('s1', 'call1', { role: 'assistant', content: 'Refund refused.' });
    expect(result).toMatchObject({ addedCases: [{ caseId: 'c2', agentCallId: 'call1', isCorrection: true }] });
  });

  it('maps each created case by provenance, not by array position', async () => {
    const ctx = makeCtx();
    agentsApi.get.mockResolvedValue({ id: 'a1', name: 'A' });
    // The API is under no obligation to return the cases in request order.
    testSuitesApi.createWithCases.mockResolvedValue(suite({
      testCases: [{ id: 'cB', sourceAgentCallId: 'callB' }, { id: 'cA', sourceAgentCallId: 'callA' }],
    }));

    const tool = createSuiteTools(ctx, store).create_suite;
    const result = await run(tool, {
      name: 'S', agentId: 'a1',
      cases: [{ agentCallId: 'callA', expectedOutput: 'right' }, { agentCallId: 'callB' }],
    }, ctx) as { addedCases: { caseId: string; agentCallId: string; isCorrection: boolean }[] };

    expect(result.addedCases).toEqual([
      { caseId: 'cA', agentCallId: 'callA', isCorrection: true },
      { caseId: 'cB', agentCallId: 'callB', isCorrection: false },
    ]);
  });
});

describe('add_to_suite', () => {
  it('adds each trace as a case and returns the final suite digest', async () => {
    const ctx = makeCtx();
    testSuitesApi.get.mockResolvedValue(suite());
    testSuitesApi.addTestCase
      .mockResolvedValueOnce(suite({ testCases: [{ id: 'c1' }, { id: 'c2' }, { id: 'c3' }] }))
      .mockResolvedValueOnce(suite({ testCases: [{ id: 'c1' }, { id: 'c2' }, { id: 'c3' }, { id: 'c4' }] }));

    const tool = createSuiteTools(ctx, store).add_to_suite;
    const result = await run(tool, { suiteId: 's1', cases: [{ agentCallId: 'call3' }, { agentCallId: 'call4' }] }, ctx);

    expect(testSuitesApi.addTestCase).toHaveBeenCalledTimes(2);
    expect(testSuitesApi.addTestCase).toHaveBeenNthCalledWith(1, 's1', 'call3', undefined);
    expect(testSuitesApi.addTestCase).toHaveBeenNthCalledWith(2, 's1', 'call4', undefined);
    expect(result).toMatchObject({ id: 's1', caseCount: 4 });
  });

  it('returns notFound for a missing suite and never adds', async () => {
    const ctx = makeCtx();
    testSuitesApi.get.mockResolvedValue(null);
    const tool = createSuiteTools(ctx, store).add_to_suite;
    const result = await run(tool, { suiteId: 'bad', cases: [{ agentCallId: 'c' }] }, ctx);
    expect(result).toEqual({ notFound: 'bad' });
    expect(testSuitesApi.addTestCase).not.toHaveBeenCalled();
  });

  it('captures a per-id failure without losing the cases that did add', async () => {
    const ctx = makeCtx();
    testSuitesApi.get.mockResolvedValue(suite());
    testSuitesApi.addTestCase
      .mockResolvedValueOnce(suite({ testCases: [{ id: 'c1' }, { id: 'c2' }, { id: 'c3' }] }))
      .mockRejectedValueOnce(new Error('stale trace'));

    const tool = createSuiteTools(ctx, store).add_to_suite;
    const result = await run(tool, { suiteId: 's1', cases: [{ agentCallId: 'good' }, { agentCallId: 'bad' }] }, ctx) as {
      caseCount: number; failed?: { id: string; error: string }[];
    };

    expect(testSuitesApi.addTestCase).toHaveBeenCalledTimes(2);
    expect(result.caseCount).toBe(3); // the successful add is still reflected
    expect(result.failed).toEqual([{ id: 'bad', error: 'stale trace' }]);
  });
});

describe('remove_test_case', () => {
  it('confirms and removes the case, returning the updated suite digest', async () => {
    const ctx = makeCtx();
    testSuitesApi.get.mockResolvedValue(suite());
    testSuitesApi.removeTestCase.mockResolvedValue(suite({ testCases: [{ id: 'c1' }] }));

    const tool = createSuiteTools(ctx, store).remove_test_case;
    const result = await run(tool, { suiteId: 's1', caseId: 'c2' }, ctx);

    expect(testSuitesApi.removeTestCase).toHaveBeenCalledWith('s1', 'c2');
    expect(result).toMatchObject({ id: 's1', caseCount: 1 });
  });

  it('returns notFound for a missing suite and never removes', async () => {
    const ctx = makeCtx();
    testSuitesApi.get.mockResolvedValue(null);
    const tool = createSuiteTools(ctx, store).remove_test_case;
    const result = await run(tool, { suiteId: 'bad', caseId: 'c1' }, ctx);
    expect(result).toEqual({ notFound: 'bad' });
    expect(testSuitesApi.removeTestCase).not.toHaveBeenCalled();
  });

  it('returns CANCELLED on decline and never removes', async () => {
    const ctx = makeCtx(false);
    testSuitesApi.get.mockResolvedValue(suite());
    const tool = createSuiteTools(ctx, store).remove_test_case;
    const result = await run(tool, { suiteId: 's1', caseId: 'c2' }, ctx);
    expect(result).toBe(CANCELLED);
    expect(testSuitesApi.removeTestCase).not.toHaveBeenCalled();
  });
});

describe('update_expected_output', () => {
  it('updates the case with an assistant message and reports updated', async () => {
    const ctx = makeCtx();
    testCasesApi.update.mockResolvedValue({ id: 'c1' });
    const tool = createSuiteTools(ctx, store).update_expected_output;
    const result = await run(tool, { caseId: 'c1', content: 'the right answer' }, ctx);

    expect(testCasesApi.update).toHaveBeenCalledWith(
      'c1', { role: 'assistant', content: 'the right answer' }, { silentStatuses: [404] },
    );
    expect(result).toEqual({ caseId: 'c1', status: 'updated' });
  });

  it('returns notFound when the case is gone', async () => {
    const ctx = makeCtx();
    const err = Object.assign(new Error('404'), { status: 404 });
    testCasesApi.update.mockRejectedValue(err);
    const tool = createSuiteTools(ctx, store).update_expected_output;
    const result = await run(tool, { caseId: 'gone', content: 'x' }, ctx);
    expect(result).toEqual({ notFound: 'gone' });
  });

  it('returns CANCELLED on decline and never updates', async () => {
    const ctx = makeCtx(false);
    const tool = createSuiteTools(ctx, store).update_expected_output;
    const result = await run(tool, { caseId: 'c1', content: 'x' }, ctx);
    expect(result).toBe(CANCELLED);
    expect(testCasesApi.update).not.toHaveBeenCalled();
  });
});

describe('cancel_test_run', () => {
  it('confirms and cancels the group, returning its status', async () => {
    const ctx = makeCtx();
    testRunGroupsApi.get.mockResolvedValue({ id: 'g1', suiteName: 'S', agentName: 'A', status: TestRunStatus.Running });
    testRunGroupsApi.cancel.mockResolvedValue({ id: 'g1', status: TestRunStatus.Cancelled });

    const tool = createRunTools(ctx, store).cancel_test_run;
    const result = await run(tool, { groupId: 'g1' }, ctx);

    expect(testRunGroupsApi.cancel).toHaveBeenCalledWith('g1');
    expect(result).toEqual({ id: 'g1', status: TestRunStatus.Cancelled });
  });

  it('short-circuits a finished run without calling cancel', async () => {
    const ctx = makeCtx();
    testRunGroupsApi.get.mockResolvedValue({ id: 'g1', suiteName: 'S', agentName: 'A', status: TestRunStatus.Completed });
    const tool = createRunTools(ctx, store).cancel_test_run;
    const result = await run(tool, { groupId: 'g1' }, ctx);
    expect(result).toEqual({ id: 'g1', status: TestRunStatus.Completed, alreadyTerminal: true });
    expect(ctx.confirm).not.toHaveBeenCalled();
    expect(testRunGroupsApi.cancel).not.toHaveBeenCalled();
  });

  it('returns notFound for a missing group and never cancels', async () => {
    const ctx = makeCtx();
    testRunGroupsApi.get.mockResolvedValue(null);
    const tool = createRunTools(ctx, store).cancel_test_run;
    const result = await run(tool, { groupId: 'bad' }, ctx);
    expect(result).toEqual({ notFound: 'bad' });
    expect(testRunGroupsApi.cancel).not.toHaveBeenCalled();
  });

  it('returns CANCELLED on decline and never cancels', async () => {
    const ctx = makeCtx(false);
    testRunGroupsApi.get.mockResolvedValue({ id: 'g1', suiteName: 'S', agentName: 'A', status: TestRunStatus.Running });
    const tool = createRunTools(ctx, store).cancel_test_run;
    const result = await run(tool, { groupId: 'g1' }, ctx);
    expect(result).toBe(CANCELLED);
    expect(testRunGroupsApi.cancel).not.toHaveBeenCalled();
  });
});
