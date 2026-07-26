import { describe, expect, it } from 'vitest';
import type { AgentCallDto, CustomAnomalyHitDto, TestRunGroupDto, TheoryDto } from '../../api/models';
import { OutlierFlag } from '../../lib/outliers';
import {
  agentPrompt,
  anomaliesOverviewPrompt,
  projectHealthPrompt,
  runGroupPrompt,
  theoryPrompt,
  tracePrompt,
  traceQuestionPrompt,
} from './askTraceyPrompts';

// Minimal DTO stubs — only the fields the builders read. Widened via a partial cast
// helper so the specs don't have to fabricate full DTOs.
function trace(over: Partial<AgentCallDto>): AgentCallDto {
  return { id: 'trace-1', agentId: 'agent-1', agentName: 'Support Bot', outlierFlags: 0, ...over } as AgentCallDto;
}

const hit: CustomAnomalyHitDto = {
  detectorId: 'det-1',
  detectorName: 'Password leak',
  matchedTrigger: 'hunter2',
  reasoning: 'Request contained a credential-like string.',
} as CustomAnomalyHitDto;

describe('tracePrompt', () => {
  it('describes an anomalous trace with flag reasons and detector hits', () => {
    const p = tracePrompt(trace({ outlierFlags: OutlierFlag.HighLatency | OutlierFlag.CustomAnomaly }), [hit]);
    expect(p).toContain('trace-1');
    expect(p).toContain('Support Bot');
    expect(p).toContain('high latency');
    expect(p).toContain('Password leak');
    expect(p).toContain('hunter2');
    expect(p).toContain('root cause');
    expect(p).toContain('come from the app UI');
  });

  it('states that a blocked trace never reached the provider', () => {
    const p = tracePrompt(trace({ outlierFlags: OutlierFlag.Blocked }), []);
    expect(p).toContain('blocked at the proxy');
    expect(p).toContain('never reached the provider');
  });

  it('falls back to a general review prompt for a non-anomalous trace', () => {
    const p = tracePrompt(trace({ outlierFlags: 0 }), []);
    expect(p).toContain('Review trace trace-1');
    expect(p).not.toContain('anomalous');
  });
});

describe('traceQuestionPrompt', () => {
  it('pairs the trace id with the trimmed custom question', () => {
    expect(traceQuestionPrompt('ddee0986-af5e-48c3-8e5d-d846f3c5350a', '  why was the refund approved?\n'))
      .toBe('Trace ddee0986-af5e-48c3-8e5d-d846f3c5350a: why was the refund approved?');
  });

  it('preserves line breaks inside a multiline question', () => {
    expect(traceQuestionPrompt('trace-1', 'Why was it approved?\nWhich policy applied?'))
      .toBe('Trace trace-1: Why was it approved?\nWhich policy applied?');
  });
});

describe('agentPrompt', () => {
  const agent = { id: 'agent-1', name: 'Support Bot' };

  it('lists low pass-rate suites and asks for an improvement + A/B validation', () => {
    const p = agentPrompt(agent, [
      { suiteId: 's1', suiteName: 'Checkout', latestRunAt: '2026-07-01', passed: 3, testCases: 10 },
      { suiteId: 's2', suiteName: 'Refunds', latestRunAt: '2026-07-01', passed: 9, testCases: 10 },
    ]);
    expect(p).toContain('low test pass rates');
    expect(p).toContain('"Checkout": 3/10 passing (30%)');
    expect(p).not.toContain('Refunds'); // 90% is not low
    expect(p).toContain('optimization theory');
    expect(p).toContain('agent-1');
  });

  it('falls back to a general review when no suite is below the threshold', () => {
    const p = agentPrompt(agent, [
      { suiteId: 's2', suiteName: 'Refunds', latestRunAt: '2026-07-01', passed: 9, testCases: 10 },
    ]);
    expect(p).toContain('Review my agent "Support Bot"');
    expect(p).toContain('anomalies');
  });
});

describe('runGroupPrompt', () => {
  function group(runs: Array<{ passedCases: number; failedCases: number }>): TestRunGroupDto {
    return {
      id: 'group-1', suiteName: 'Checkout', agentName: 'Support Bot',
      runs: runs.map(r => ({ ...r })),
    } as TestRunGroupDto;
  }

  it('summarizes failure counts and asks for grouped causes + fixes', () => {
    const p = runGroupPrompt(group([{ passedCases: 6, failedCases: 4 }]));
    expect(p).toContain('group-1');
    expect(p).toContain('"Checkout"');
    expect(p).toContain('4 of 10');
    expect(p).toContain('group them by cause');
  });

  it('asks for a results summary when everything passed', () => {
    const p = runGroupPrompt(group([{ passedCases: 10, failedCases: 0 }]));
    expect(p).toContain('Summarize the results');
    expect(p).not.toContain('group them by cause');
  });
});

describe('theoryPrompt', () => {
  it('asks for an accept/reject recommendation when a proposal exists', () => {
    const p = theoryPrompt({ id: 'th-1', agentName: 'Support Bot', resultingProposalId: 'prop-1' } as TheoryDto);
    expect(p).toContain('th-1');
    expect(p).toContain('accept or reject');
  });

  it('asks for a plain-terms walkthrough when no proposal exists yet', () => {
    const p = theoryPrompt({ id: 'th-1', agentName: 'Support Bot', resultingProposalId: null } as TheoryDto);
    expect(p).toContain('validation status');
    expect(p).not.toContain('accept or reject');
  });
});

describe('page-level prompts', () => {
  it('anomaliesOverviewPrompt asks for patterns and prevention', () => {
    const p = anomaliesOverviewPrompt();
    expect(p).toContain('anomalies');
    expect(p).toContain('prevent');
  });

  it('projectHealthPrompt asks for a health review', () => {
    expect(projectHealthPrompt()).toContain('health');
  });
});
