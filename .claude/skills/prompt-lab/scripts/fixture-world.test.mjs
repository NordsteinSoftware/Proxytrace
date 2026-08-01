// node --test .claude/skills/prompt-lab/scripts/fixture-world.test.mjs
//
// The fixture DSL is the one piece of the lab with logic of its own, and a bug in it is expensive:
// it does not fail loudly, it makes a correct model decision look like a prompt regression a few
// steps later. These cases pin the shapes the real fixture world relies on.
import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import assert from 'node:assert/strict';
import test from 'node:test';

import { fixtureResult } from './fixture-world.mjs';

const HERE = dirname(fileURLToPath(import.meta.url));
const WORLD = JSON.parse(readFileSync(join(HERE, '..', 'fixtures', 'tracey.json'), 'utf8'));

const SUITE_ID = '5e6f7a8b-9c0d-4e1f-a2b3-c4d5e6f7a8b9';
const OTHER_SUITE_ID = '6f7a8b9c-0d1e-4f20-b3c4-d5e6f7a8b9c0';
const DECIDING_CALL = 'b0c1d2e3-f4a5-4677-8899-0a1b2c3d4e5f';
const SUMMARY_CALL = 'c1d2e3f4-a5b6-4788-9a0b-1c2d3e4f5061';

test('a tool the world has no entry for is a miss, not an empty answer', () => {
  assert.equal(fixtureResult(WORLD, 'no_such_tool', {}), undefined);
});

test('_byArg answers about the entity that was asked for', () => {
  assert.equal(fixtureResult(WORLD, 'get_suite', { suiteId: SUITE_ID }).name, 'Refund policy regressions');
  assert.equal(fixtureResult(WORLD, 'get_suite', { suiteId: OTHER_SUITE_ID }).name, 'Tone & escalation');
});

test('_byArg with no branch and no default returns notFound, like the real by-id tools', () => {
  assert.deepEqual(fixtureResult(WORLD, 'get_suite', { suiteId: 'nope' }), { notFound: 'nope' });
});

test('get_trace returns the requested trace, and verbose widens it', () => {
  const summary = fixtureResult(WORLD, 'get_trace', { traceId: DECIDING_CALL });
  assert.equal(summary.id, DECIDING_CALL);
  assert.equal(summary.messages, undefined);

  const verbose = fixtureResult(WORLD, 'get_trace', { traceId: DECIDING_CALL, verbose: true });
  assert.equal(verbose.id, DECIDING_CALL);
  assert.equal(verbose.messageCount, 4);
  assert.equal(verbose.response.toolRequests[0].name, 'issue_refund');
});

test('add_to_suite reports the correction it was actually sent', () => {
  const result = fixtureResult(WORLD, 'add_to_suite', {
    suiteId: SUITE_ID,
    cases: [
      { agentCallId: DECIDING_CALL, expectedOutput: 'I cannot refund outside the window.' },
      { agentCallId: SUMMARY_CALL },
    ],
  });
  assert.equal(result.id, SUITE_ID);
  assert.equal(result.addedCases.length, 2);
  assert.equal(result.addedCases[0].agentCallId, DECIDING_CALL);
  assert.equal(result.addedCases[0].isCorrection, true);
  assert.equal(result.addedCases[1].isCorrection, false);
  assert.notEqual(result.addedCases[0].caseId, result.addedCases[1].caseId);
});

test('minted ids are stable across calls, so an A/B diff is never id noise', () => {
  const args = { suiteId: SUITE_ID, cases: [{ agentCallId: DECIDING_CALL, expectedOutput: 'x' }] };
  const first = fixtureResult(WORLD, 'add_to_suite', args);
  const second = fixtureResult(WORLD, 'add_to_suite', structuredClone(args));
  assert.equal(first.addedCases[0].caseId, second.addedCases[0].caseId);
});

test('create_suite mints one id per case and uses it in both cases and addedCases', () => {
  const result = fixtureResult(WORLD, 'create_suite', {
    name: 'Return window',
    agentId: '4b1c2a7e-9f30-4d18-8c55-1a2b3c4d5e6f',
    cases: [{ agentCallId: DECIDING_CALL, expectedOutput: 'Refused.' }],
  });
  assert.equal(result.name, 'Return window');
  assert.equal(result.agentName, 'Customer Support Agent');
  assert.equal(result.caseCount, 1);
  assert.equal(result.cases[0].id, result.addedCases[0].caseId);
  assert.equal(result.cases[0].sourceAgentCallId, DECIDING_CALL);
});

test('a write against an entity the world does not have stays a miss', () => {
  assert.deepEqual(
    fixtureResult(WORLD, 'add_to_suite', { suiteId: 'ghost', cases: [{ agentCallId: DECIDING_CALL }] }),
    { notFound: 'ghost' },
  );
});

test('set_suite_evaluators reflects the complete set that was sent', () => {
  const ids = ['f6a7b8c9-d0e1-4f23-a4b5-c6d7e8f9a0b1', 'a7b8c9d0-e1f2-4a34-b5c6-d7e8f9a0b1c2', 'e1f2a3b4-c5d6-4e78-a9b0-c1d2e3f4a5b6'];
  const result = fixtureResult(WORLD, 'set_suite_evaluators', { suiteId: SUITE_ID, evaluatorIds: ids });
  assert.deepEqual(result.evaluators.map((e) => e.id), ids);
});

test('get_case_results answers about the run and the cases it was asked about', () => {
  const known = fixtureResult(WORLD, 'get_case_results', {
    runId: '2c3d4e5f-6a7b-4c8d-9e0f-1a2b3c4d5e6f',
    caseIds: ['aa11bb22-cc33-4d44-9e55-f66a77b88c99', 'ffffffff-ffff-4fff-8fff-ffffffffffff'],
  });
  assert.equal(known.runId, '2c3d4e5f-6a7b-4c8d-9e0f-1a2b3c4d5e6f');
  assert.deepEqual(known.cases.map((c) => c.verdict), ['fail', 'not-in-run']);

  // Called bare it reports the run's non-passing cases — pass is never in the list.
  const bare = fixtureResult(WORLD, 'get_case_results', { runId: '2c3d4e5f-6a7b-4c8d-9e0f-1a2b3c4d5e6f' });
  assert.equal(bare.cases.some((c) => c.verdict === 'pass'), false);

  assert.deepEqual(fixtureResult(WORLD, 'get_case_results', { runId: 'ghost' }), { notFound: 'ghost' });
});

test('a case created in a scenario reaches a red verdict, then a green one', () => {
  const caseId = fixtureResult(WORLD, 'add_to_suite', {
    suiteId: SUITE_ID,
    cases: [{ agentCallId: DECIDING_CALL, expectedOutput: 'Refused.' }],
  }).addedCases[0].caseId;

  const started = fixtureResult(WORLD, 'start_test_run', { suiteId: SUITE_ID, agentId: '4b1c2a7e-9f30-4d18-8c55-1a2b3c4d5e6f' });
  const waited = fixtureResult(WORLD, 'await_actions', { handles: [started.awaitable] });
  const runId = waited.results[0].runs[0].runId;

  const red = fixtureResult(WORLD, 'get_case_results', { runId, caseIds: [caseId] });
  assert.deepEqual(red.cases, [red.cases[0]]);
  assert.equal(red.cases[0].testCaseId, caseId);
  assert.equal(red.cases[0].verdict, 'fail');

  const theory = fixtureResult(WORLD, 'submit_optimization_theory', {
    agentId: '4b1c2a7e-9f30-4d18-8c55-1a2b3c4d5e6f',
    suiteId: SUITE_ID,
    priority: 'High',
    rationale: 'Verify the delivery date before refunding.',
    details: { kind: 'SystemPrompt', currentSystemMessage: 'a', proposedSystemMessage: 'b' },
  });
  const validated = fixtureResult(WORLD, 'await_actions', { handles: [theory.awaitable] });
  const green = fixtureResult(WORLD, 'get_case_results', {
    runId: validated.results[0].abTestRunId,
    caseIds: [caseId],
    expect: 'pass',
  });
  assert.equal(green.cases[0].testCaseId, caseId);
  assert.equal(green.cases[0].verdict, 'pass');
});

test('await_actions answers per handle, dispatched on each handle kind', () => {
  const result = fixtureResult(WORLD, 'await_actions', {
    handles: [
      { kind: 'test-run', id: '3d4e5f6a-7b8c-4d9e-af01-2b3c4d5e6f70' },
      { kind: 'theory', id: '9e0f1a2b-3c4d-4e5f-a607-1b2c3d4e5f60' },
    ],
  });
  assert.deepEqual(result.results.map((r) => r.kind), ['test-run', 'theory']);
  assert.equal(result.results[0].id, '3d4e5f6a-7b8c-4d9e-af01-2b3c4d5e6f70');
  assert.equal(result.results[1].id, '9e0f1a2b-3c4d-4e5f-a607-1b2c3d4e5f60');
  assert.equal(result.anyTimedOut, false);
});

test('tokens: literals, escapes, presence and counts', () => {
  const world = {
    probe: {
      literal: 'plain',
      escaped: '$$args.x',
      missing: '$args.nope',
      count: '$count.args.list',
      sent: '$has.args.list',
      rows: { _forEach: 'list', _item: { at: '$index', value: '$item' }, _empty: [{ at: -1 }] },
    },
  };
  assert.deepEqual(fixtureResult(world, 'probe', { list: ['a', 'b'] }), {
    literal: 'plain',
    escaped: '$args.x',
    missing: null,
    count: 2,
    sent: true,
    rows: [{ at: 0, value: 'a' }, { at: 1, value: 'b' }],
  });
  const empty = fixtureResult(world, 'probe', {});
  assert.equal(empty.count, 0);
  assert.equal(empty.sent, false);
  assert.deepEqual(empty.rows, [{ at: -1 }]);
});

test('a malformed _like is reported rather than silently answered', () => {
  assert.throws(() => fixtureResult({ a: { _like: 'ghost' } }, 'a', {}), /unknown entry "ghost"/);
  assert.throws(() => fixtureResult({ a: { _like: 'b' }, b: { _like: 'a' } }, 'a', {}), /circular/);
});
