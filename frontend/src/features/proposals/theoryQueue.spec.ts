import { describe, expect, it } from 'vitest';
import type { OptimizationProposalDto, TheoryDto } from '../../api/models';
import { Priority, ProposalKind, ProposalStatus, TheorySource, TheoryStatus } from '../../api/models';
import {
  groupIntoQueue,
  indexProposals,
  isInsideNoise,
  significanceLabel,
  loopStats,
  passRateDeltaPt,
  passRateTransition,
  queueGroupOf,
  theoryShortId,
} from './theoryQueue';

function makeTheory(overrides: Partial<TheoryDto> = {}): TheoryDto {
  return {
    id: '6c47abcd-0000-0000-0000-000000000000',
    kind: ProposalKind.SystemPrompt,
    status: TheoryStatus.Proposed,
    source: TheorySource.Optimizer,
    agentId: 'agent-1',
    agentName: 'Code Helper',
    suiteId: 'suite-1',
    priority: Priority.Medium,
    rationale: 'A hypothesis.',
    details: { kind: 'SystemPrompt', currentSystemMessage: 'a', proposedSystemMessage: 'b' },
    evidenceTestRunIds: [],
    resultingProposalId: null,
    baselinePassRate: null,
    projectedPassRate: null,
    pValue: null,
    abTestRunId: null,
    createdAt: '2026-06-01T00:00:00Z',
    updatedAt: '2026-06-01T00:00:00Z',
    ...overrides,
  };
}

function makeProposal(overrides: Partial<OptimizationProposalDto> = {}): OptimizationProposalDto {
  return {
    id: 'prop-1',
    kind: ProposalKind.SystemPrompt,
    status: ProposalStatus.Draft,
    agentId: 'agent-1',
    agentName: 'Code Helper',
    priority: Priority.Medium,
    rationale: 'A proposal.',
    details: { kind: 'SystemPrompt', currentSystemMessage: 'a', proposedSystemMessage: 'b' },
    evidenceTestRunIds: [],
    abTestRun: null,
    currentPassRate: null,
    proposedPassRate: null,
    expectedPassRateDelta: null,
    adoptedAt: null,
    adoptedAgentVersionId: null,
    adoptedAgentVersionNumber: null,
    adoptedManually: null,
    createdAt: '2026-06-01T00:00:00Z',
    updatedAt: '2026-06-01T00:00:00Z',
    ...overrides,
  };
}

describe('queueGroupOf', () => {
  it('puts proposed and validating theories in flight', () => {
    expect(queueGroupOf(makeTheory({ status: TheoryStatus.Proposed }), null)).toBe('inflight');
    expect(queueGroupOf(makeTheory({ status: TheoryStatus.Validating }), null)).toBe('inflight');
  });

  it('puts invalidated theories in history', () => {
    expect(queueGroupOf(makeTheory({ status: TheoryStatus.Invalidated }), null)).toBe('history');
  });

  it('puts failed theories in the needs-attention group, not history', () => {
    expect(queueGroupOf(makeTheory({ status: TheoryStatus.Failed }), null)).toBe('attention');
  });

  it('routes a validated theory by its proposal review state', () => {
    const validated = makeTheory({ status: TheoryStatus.Validated, resultingProposalId: 'prop-1' });
    expect(queueGroupOf(validated, makeProposal({ status: ProposalStatus.Draft }))).toBe('decision');
    expect(queueGroupOf(validated, makeProposal({ status: ProposalStatus.Accepted }))).toBe('adoption');
    expect(queueGroupOf(validated, makeProposal({ status: ProposalStatus.Adopted }))).toBe('history');
    expect(queueGroupOf(validated, makeProposal({ status: ProposalStatus.Rejected }))).toBe('history');
  });

  it('treats a validated theory with an unloaded proposal as needing a decision', () => {
    expect(queueGroupOf(makeTheory({ status: TheoryStatus.Validated }), null)).toBe('decision');
  });
});

describe('groupIntoQueue', () => {
  it('groups by queue key and sorts newest first within a group', () => {
    const older = makeTheory({ id: 'aaa1', status: TheoryStatus.Proposed, createdAt: '2026-06-01T00:00:00Z' });
    const newer = makeTheory({ id: 'bbb2', status: TheoryStatus.Proposed, createdAt: '2026-06-02T00:00:00Z' });
    const done = makeTheory({ id: 'ccc3', status: TheoryStatus.Invalidated });

    const groups = groupIntoQueue([older, done, newer], indexProposals([]));

    expect(groups.inflight.map(t => t.id)).toEqual(['bbb2', 'aaa1']);
    expect(groups.history.map(t => t.id)).toEqual(['ccc3']);
    expect(groups.decision).toHaveLength(0);
    expect(groups.adoption).toHaveLength(0);
  });

  it('resolves each theory’s proposal through the index', () => {
    const accepted = makeProposal({ id: 'prop-a', status: ProposalStatus.Accepted });
    const validated = makeTheory({ status: TheoryStatus.Validated, resultingProposalId: 'prop-a' });

    const groups = groupIntoQueue([validated], indexProposals([accepted]));

    expect(groups.adoption).toHaveLength(1);
    expect(groups.decision).toHaveLength(0);
  });
});

describe('loopStats', () => {
  it('counts each stage and computes win rate and proven gain', () => {
    const proposals = indexProposals([
      makeProposal({ id: 'prop-draft', status: ProposalStatus.Draft }),
      makeProposal({ id: 'prop-accepted', status: ProposalStatus.Accepted }),
    ]);
    const stats = loopStats(
      [
        makeTheory({ status: TheoryStatus.Proposed }),
        makeTheory({ status: TheoryStatus.Validating }),
        makeTheory({
          status: TheoryStatus.Validated,
          resultingProposalId: 'prop-draft',
          baselinePassRate: 0.78,
          projectedPassRate: 0.9,
        }),
        makeTheory({
          status: TheoryStatus.Validated,
          resultingProposalId: 'prop-accepted',
          baselinePassRate: 0.71,
          projectedPassRate: 0.78,
        }),
        makeTheory({ status: TheoryStatus.Invalidated, baselinePassRate: 0.89, projectedPassRate: 0.9 }),
      ],
      proposals,
    );

    expect(stats.testing).toBe(2);
    expect(stats.decision).toBe(1);
    expect(stats.adoption).toBe(1);
    expect(stats.decided).toBe(1);
    expect(stats.winRate).toBe(67); // 2 of 3 tested validated
    expect(stats.provenGainPt).toBe(19); // 12 + 7
  });

  it('returns a null win rate when nothing has been tested', () => {
    const stats = loopStats([makeTheory({ status: TheoryStatus.Proposed })], indexProposals([]));
    expect(stats.winRate).toBeNull();
    expect(stats.provenGainPt).toBe(0);
  });

  it('excludes failed theories from the win rate and the decided count', () => {
    const stats = loopStats(
      [
        makeTheory({ status: TheoryStatus.Validated, resultingProposalId: 'missing', baselinePassRate: 0.7, projectedPassRate: 0.8 }),
        makeTheory({ status: TheoryStatus.Invalidated, baselinePassRate: 0.9, projectedPassRate: 0.9 }),
        makeTheory({ status: TheoryStatus.Failed }),
        makeTheory({ status: TheoryStatus.Failed }),
      ],
      indexProposals([]),
    );

    expect(stats.failed).toBe(2);
    expect(stats.decided).toBe(1);
    expect(stats.winRate).toBe(50); // 1 of 2 actually tested — failures don't count as losses
  });

  it('reports a null win rate when only failed theories exist', () => {
    const stats = loopStats([makeTheory({ status: TheoryStatus.Failed })], indexProposals([]));
    expect(stats.winRate).toBeNull();
    expect(stats.failed).toBe(1);
  });

  it('ignores non-positive deltas in proven gain', () => {
    const stats = loopStats(
      [makeTheory({ status: TheoryStatus.Validated, baselinePassRate: 0.9, projectedPassRate: 0.9 })],
      indexProposals([]),
    );
    expect(stats.provenGainPt).toBe(0);
  });
});

describe('theoryShortId', () => {
  it('builds a thy_ handle from the first hex characters', () => {
    expect(theoryShortId('6c47abcd-0000-0000-0000-000000000000')).toBe('thy_6c47');
  });

  it('falls back to zeros when the id has no hex', () => {
    expect(theoryShortId('zzzz')).toBe('thy_0000');
  });
});

describe('passRateTransition', () => {
  it('returns rounded from/to/delta when both rates are present', () => {
    const t = passRateTransition(makeTheory({ baselinePassRate: 0.781, projectedPassRate: 0.904 }));
    expect(t).toEqual({ fromPct: 78, toPct: 90, deltaPt: 12 });
  });

  it('returns null when a rate is missing', () => {
    expect(passRateTransition(makeTheory({ baselinePassRate: 0.5, projectedPassRate: null }))).toBeNull();
    expect(passRateDeltaPt(makeTheory())).toBeNull();
  });
});

describe('isInsideNoise', () => {
  it('treats p ≥ 0.05 as noise and lower as significant', () => {
    expect(isInsideNoise(0.41)).toBe(true);
    expect(isInsideNoise(0.05)).toBe(true);
    expect(isInsideNoise(0.008)).toBe(false);
    expect(isInsideNoise(null)).toBe(false);
  });
});

describe('significanceLabel', () => {
  const id = (pValue: number, validated: boolean) => significanceLabel(pValue, validated).id;

  it('calls a low p-value significant either way', () => {
    expect(id(0.008, true)).toBe(id(0.008, false));
  });

  it('never labels a noisy p-value significant', () => {
    expect(id(0.19, true)).not.toBe(id(0.008, true));
    expect(id(0.19, false)).not.toBe(id(0.008, false));
  });

  // A validated theory above the threshold means the significance gate was relaxed: the row shows
  // a win, so "inside noise" would contradict it — it must read as an improvement instead.
  it('distinguishes a noisy win from a noisy rejection', () => {
    expect(id(0.19, true)).not.toBe(id(0.19, false));
  });
});
