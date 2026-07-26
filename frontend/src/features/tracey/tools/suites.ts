import { z } from 'zod';
import { agentsApi } from '../../../api/agents';
import { testSuitesApi } from '../../../api/test-suites';
import { testCasesApi } from '../../../api/test-cases';
import type { TestSuiteDto } from '../../../api/models';
import { type ToolFactory, tool, CANCELLED, ignore404, isEntityId, listDigest, presentArg } from './shared';
import { clip } from './run-analysis';

/**
 * The compact suite digest returned by the read + curation tools (the card shows everything).
 *
 * It carries the evaluator set and the cases on purpose. A case passes only when EVERY attached
 * evaluator passes, so the whole set has to be visible before anyone changes it; and the case ids
 * are what `remove_test_case`, `update_expected_output` and `get_case_results` all take.
 */
const suiteDigest = (suite: TestSuiteDto) => ({
  id: suite.id,
  name: suite.name,
  agentId: suite.agentId,
  agentName: suite.agentName,
  caseCount: suite.testCases.length,
  passRate: suite.passRate,
  evaluators: suite.evaluators.map((evaluator) => ({ id: evaluator.id, kind: evaluator.kind })),
  cases: listDigest(suite.testCases, 25, (testCase) => ({
    id: testCase.id,
    sourceAgentCallId: testCase.sourceAgentCallId ?? null,
    expected: clip(testCase.expectedOutput.content, 120),
    resolvedToolCalls: testCase.resolvedToolCallCount ?? 0,
  })),
});

/**
 * Cases whose input already contains completed tool calls, paired with the expected output they were
 * corrected to.
 *
 * A run scores exactly ONE model call, so such a case grades the message written after those calls
 * came back — never the choice to make them. Correcting one is the trap this reports: the last call
 * of a tool loop contains the harmful call AND its success, so an expected output that contradicts
 * that result cannot be produced by any prompt. The case fails forever and reads as "the fix did not
 * work" when nothing was ever wrong with the fix.
 */
function summaryOnlyCorrections(
  added: { caseId: string; agentCallId: string; isCorrection: boolean }[],
  cases: TestSuiteDto['testCases'],
) {
  const resolved = new Map(cases.map((testCase) => [testCase.id, testCase.resolvedToolCallCount ?? 0]));
  return added.flatMap((entry) => {
    const count = resolved.get(entry.caseId) ?? 0;
    if (!entry.isCorrection || count === 0) return [];
    return [{
      caseId: entry.caseId,
      agentCallId: entry.agentCallId,
      resolvedToolCalls: count,
      problem:
        `This case's input already contains ${count} completed tool call${count === 1 ? '' : 's'}, so a ` +
        'run only scores the message the agent writes AFTER them — the decision was made in an ' +
        'earlier call and cannot change here. If any of those results contradicts the expected ' +
        'output you set, the case can never pass.',
      fix:
        'Find the earlier trace of the same conversationId whose own response CONTAINS the wrong ' +
        'tool call, and correct that one instead. Then remove this case.',
    }];
  });
}

/** One trace to turn into a test case, optionally with the answer the agent SHOULD have given. */
const caseSpecSchema = z.object({
  agentCallId: z.string().describe('Captured trace (agent-call) id — from find_traces / get_trace.'),
  expectedOutput: z.string().min(1).optional().describe(
    'What the agent SHOULD have answered. Omit to seed the case with the response it actually gave ' +
    '(a plain promotion, which passes immediately). Set it to author a CORRECTION — a case that ' +
    'FAILS until the agent is fixed, which is how a reported defect becomes a regression test.',
  ),
});

type CaseSpec = z.infer<typeof caseSpecSchema>;

/** Wraps the model's plain string as the assistant message the API scores the case against. */
const expectedMessage = (content: string) => ({ role: 'assistant', content });

/**
 * Reports which case each spec produced, mapped by provenance (`sourceAgentCallId`) rather than by
 * array position — the API is under no obligation to return the cases in request order, and a
 * mis-mapped id would silently point the whole red/green loop at the wrong case.
 */
function addedCases(specs: CaseSpec[], cases: { id: string; sourceAgentCallId?: string | null }[]) {
  const pool = new Map<string, string[]>();
  for (const testCase of cases) {
    if (!testCase.sourceAgentCallId) continue;
    const ids = pool.get(testCase.sourceAgentCallId) ?? [];
    ids.push(testCase.id);
    pool.set(testCase.sourceAgentCallId, ids);
  }
  return specs.flatMap((spec) => {
    const caseId = pool.get(spec.agentCallId)?.shift();
    return caseId
      ? [{ caseId, agentCallId: spec.agentCallId, isCorrection: spec.expectedOutput !== undefined }]
      : [];
  });
}

export const createSuiteTools: ToolFactory = (ctx, store) => {
  const projectId = ctx.projectId;
  return {
    list_suites: tool({
      description:
        'List test suites. Pass agentId to list only the suites that benchmark that agent (use this when ' +
        'optimizing or curating for a specific agent); omit it for all of the project\'s suites. Returns a ' +
        'compact index — each row carries the suite\'s agent — and the full list renders to the user.',
      parameters: z.object({
        present: presentArg,
        agentId: z.string().optional().describe('Restrict to the suites that benchmark this agent.'),
      }),
      confirm: false,
      execute: async ({ agentId }) => {
        if (agentId !== undefined && !isEntityId(agentId)) return { notFound: agentId };
        const items = (await testSuitesApi.list({ projectId, agentId })).items;
        return store(
          'suite-list',
          items,
          listDigest(items, 25, (s) => ({ id: s.id, name: s.name, agentId: s.agentId, agentName: s.agentName })),
        );
      },
    }),
    get_suite: tool({
      description:
        'Get one test suite by id. Returns a summary (name, case count, pass rate); the full suite ' +
        'renders as a card. Each test case carries its own id — use those with remove_test_case / update_expected_output.',
      parameters: z.object({ present: presentArg, suiteId: z.string().describe('The id of the test suite to fetch.') }),
      confirm: false,
      execute: async ({ suiteId }) => {
        const suite = await ignore404(() => testSuitesApi.get(suiteId, { silentStatuses: [404] }));
        if (!suite) return { notFound: suiteId };
        return store('suite', suite, suiteDigest(suite));
      },
    }),
    create_suite: tool({
      description:
        'Create a benchmark suite for an agent, seeded from captured traces. Requires confirmation. ' +
        'Each case names a trace id from find_traces. Give a case an `expectedOutput` to seed it as ' +
        'a CORRECTION — the trace\'s input with the answer the agent should have given — which is ' +
        'how you write a case that fails until the agent is fixed; omit it to lock in the response ' +
        'the agent actually gave. Pass evaluatorIds to score the suite with specific evaluators ' +
        '(they replace the default); omit it to get a default exact-match evaluator. Returns the ' +
        'new suite as a card, with `addedCases` naming the case id each trace produced. ' +
        'When correcting, pick the trace where the agent MADE the wrong decision — an agent turn ' +
        'that used tools spans several traces, and the last one already contains the harmful tool ' +
        'call and its result, so a correction there can never pass. Any case that lands this way is ' +
        'reported back in `unpassableCases`; act on it rather than re-running the suite.',
      parameters: z.object({
        name: z.string().min(1).describe('A short, descriptive name for the suite.'),
        agentId: z.string().describe('The id of the agent the suite benchmarks.'),
        cases: z.array(caseSpecSchema).min(1).describe('The traces to seed as test cases.'),
        evaluatorIds: z.array(z.string()).optional()
          .describe('Evaluator ids to attach (from list_evaluators / create_evaluator). They replace ' +
            'the default; omit to attach a default exact-match evaluator.'),
      }),
      confirm: true,
      execute: async ({ name, agentId, cases, evaluatorIds }, c) => {
        const agent = await ignore404(() => agentsApi.get(agentId, { silentStatuses: [404] }));
        if (!agent) return { notFound: agentId };
        const n = cases.length;
        const ok = await c.confirm(`Create suite "${name}" for agent "${agent.name}" from ${n} trace${n === 1 ? '' : 's'}?`);
        if (!ok) return CANCELLED;
        const suite = await testSuitesApi.createWithCases({
          name,
          agentId,
          testCases: cases.map((spec) => ({
            fromAgentCallId: spec.agentCallId,
            ...(spec.expectedOutput === undefined ? {} : { expectedOutput: expectedMessage(spec.expectedOutput) }),
          })),
          ...(evaluatorIds ? { evaluatorIds } : {}),
        });
        const added = addedCases(cases, suite.testCases);
        const unpassable = summaryOnlyCorrections(added, suite.testCases);
        return store('suite', suite, {
          ...suiteDigest(suite),
          addedCases: added,
          ...(unpassable.length > 0 ? { unpassableCases: unpassable } : {}),
        });
      },
    }),
    add_to_suite: tool({
      description:
        'Add captured traces to an existing suite as new test cases. Requires confirmation. ' +
        'Each case names a trace id from find_traces. Give a case an `expectedOutput` to add it as ' +
        'a CORRECTION — the trace\'s input with the answer the agent should have given — which ' +
        'fails until the agent is fixed; omit it to lock in the response the agent actually gave. ' +
        'Returns the updated suite as a card, with `addedCases` naming the case id each trace ' +
        'produced (you need those ids to check the case in a run). ' +
        'When correcting, pick the trace where the agent MADE the wrong decision — an agent turn ' +
        'that used tools spans several traces, and the last one already contains the harmful tool ' +
        'call and its result, so a correction there can never pass. Any case that lands this way is ' +
        'reported back in `unpassableCases`; act on it rather than re-running the suite.',
      parameters: z.object({
        suiteId: z.string().describe('The id of the suite to add cases to.'),
        cases: z.array(caseSpecSchema).min(1).describe('The traces to add as test cases.'),
      }),
      confirm: true,
      execute: async ({ suiteId, cases }, c) => {
        const existing = await ignore404(() => testSuitesApi.get(suiteId, { silentStatuses: [404] }));
        if (!existing) return { notFound: suiteId };
        const n = cases.length;
        const ok = await c.confirm(`Add ${n} case${n === 1 ? '' : 's'} to suite "${existing.name}"?`);
        if (!ok) return CANCELLED;
        // Each addTestCase commits server-side and returns the whole updated suite. Apply
        // sequentially; capture per-id failures rather than throwing, so a mid-batch error
        // (e.g. one stale trace id) can't both partially mutate the suite AND lose the report of
        // what was added. Keep the latest successful suite snapshot for the digest/card.
        let suite = existing;
        const failed: { id: string; error: string }[] = [];
        const added: { caseId: string; agentCallId: string; isCorrection: boolean }[] = [];
        for (const spec of cases) {
          // Diff against the snapshot from before THIS add, so the new case id is exact even when
          // the same trace is added twice or the API reorders the collection.
          const before = new Set(suite.testCases.map((testCase) => testCase.id));
          try {
            suite = await testSuitesApi.addTestCase(
              suiteId,
              spec.agentCallId,
              spec.expectedOutput === undefined ? undefined : expectedMessage(spec.expectedOutput),
            );
            const caseId = suite.testCases.find((testCase) => !before.has(testCase.id))?.id;
            if (caseId) {
              added.push({
                caseId,
                agentCallId: spec.agentCallId,
                isCorrection: spec.expectedOutput !== undefined,
              });
            }
          } catch (e) {
            failed.push({ id: spec.agentCallId, error: e instanceof Error ? e.message : String(e) });
          }
        }
        const unpassable = summaryOnlyCorrections(added, suite.testCases);
        return store('suite', suite, {
          ...suiteDigest(suite),
          addedCases: added,
          ...(unpassable.length > 0 ? { unpassableCases: unpassable } : {}),
          ...(failed.length > 0 ? { failed } : {}),
        });
      },
    }),
    remove_test_case: tool({
      description:
        'Remove a test case from a suite. Requires confirmation. Pass the suite id and the case id ' +
        '(from get_suite). Returns the updated suite as a card.',
      parameters: z.object({
        suiteId: z.string().describe('The id of the suite.'),
        caseId: z.string().describe('The id of the test case to remove (from get_suite).'),
      }),
      confirm: true,
      execute: async ({ suiteId, caseId }, c) => {
        const existing = await ignore404(() => testSuitesApi.get(suiteId, { silentStatuses: [404] }));
        if (!existing) return { notFound: suiteId };
        const ok = await c.confirm(`Remove a test case from suite "${existing.name}"?`);
        if (!ok) return CANCELLED;
        const suite = await testSuitesApi.removeTestCase(suiteId, caseId);
        return store('suite', suite, suiteDigest(suite));
      },
    }),
    update_expected_output: tool({
      description:
        "Set a test case's expected output — what it is scored against. Requires confirmation. " +
        'Pass the case id (from get_suite) and the expected assistant text.',
      parameters: z.object({
        caseId: z.string().describe('The id of the test case to update (from get_suite).'),
        content: z.string().min(1).describe('The expected assistant response the case is scored against.'),
      }),
      confirm: true,
      execute: async ({ caseId, content }, c) => {
        const ok = await c.confirm('Update the expected output of this test case?');
        if (!ok) return CANCELLED;
        const updated = await ignore404(() =>
          testCasesApi.update(caseId, { role: 'assistant', content }, { silentStatuses: [404] }),
        );
        if (!updated) return { notFound: caseId };
        return { caseId: updated.id, status: 'updated' };
      },
    }),
    set_suite_evaluators: tool({
      description:
        'Set which evaluators a suite scores its cases with. Requires confirmation. This REPLACES ' +
        'the current set, so pass every evaluator id you want attached — read the existing ones ' +
        'from get_suite first. A case passes only when EVERY attached evaluator passes, so leaving ' +
        'a strict exact-match evaluator next to a behavioral judge makes a prose answer unpassable ' +
        'no matter how correct it is. Returns the updated suite as a card.',
      parameters: z.object({
        suiteId: z.string().describe('The id of the suite to configure.'),
        evaluatorIds: z.array(z.string()).min(1)
          .describe('The COMPLETE set of evaluator ids to attach (from list_evaluators / create_evaluator).'),
      }),
      confirm: true,
      execute: async ({ suiteId, evaluatorIds }, c) => {
        const existing = await ignore404(() => testSuitesApi.get(suiteId, { silentStatuses: [404] }));
        if (!existing) return { notFound: suiteId };
        const n = evaluatorIds.length;
        const ok = await c.confirm(`Set ${n} evaluator${n === 1 ? '' : 's'} on suite "${existing.name}"?`);
        if (!ok) return CANCELLED;
        const suite = await testSuitesApi.updateEvaluators(suiteId, evaluatorIds);
        return store('suite', suite, suiteDigest(suite));
      },
    }),
  };
};
