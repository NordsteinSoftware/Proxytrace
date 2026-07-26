// Pure logic for the proposals review desk: queue grouping, loop-strip stats, and the
// pass-rate/p-value helpers shared across the feature. No JSX, no I/O — unit-tested in
// theoryQueue.spec.ts.

import { msg } from '@lingui/core/macro';
import type { MessageDescriptor } from '@lingui/core';
import type { OptimizationProposalDto, TheoryDto } from '../../api/models';
import { ProposalStatus, TheoryStatus } from '../../api/models';
import type { DisplayTone } from './shared';

/** Urgency-ordered groups of the queue rail. */
export type QueueGroupKey = 'decision' | 'attention' | 'adoption' | 'inflight' | 'history';

export interface QueueGroupMeta {
  key: QueueGroupKey;
  label: MessageDescriptor;
  tone: DisplayTone;
}

/** Rail order: what needs the user first, history last. */
export const QUEUE_GROUPS: readonly QueueGroupMeta[] = [
  { key: 'decision', label: msg`Needs decision`, tone: 'accent' },
  { key: 'attention', label: msg`Needs attention`, tone: 'danger' },
  { key: 'adoption', label: msg`Awaiting adoption`, tone: 'success' },
  { key: 'inflight', label: msg`In flight`, tone: 'teal' },
  { key: 'history', label: msg`History`, tone: 'muted' },
] as const;

export type ProposalById = ReadonlyMap<string, OptimizationProposalDto>;

/** Index proposals by id for O(1) theory → proposal resolution. */
export function indexProposals(proposals: readonly OptimizationProposalDto[]): ProposalById {
  return new Map(proposals.map(p => [p.id, p]));
}

/** The proposal a theory spawned, when loaded. */
export function proposalFor(theory: TheoryDto, proposals: ProposalById): OptimizationProposalDto | null {
  return theory.resultingProposalId ? proposals.get(theory.resultingProposalId) ?? null : null;
}

/**
 * Where a theory sits in the queue. A validated theory's group follows its proposal's review
 * state; a proposal that hasn't loaded yet reads as Draft (same convention as REVIEW_META).
 * A Failed theory (its A/B validation errored out) needs the user's attention — retry or
 * dismiss — so it must not disappear into the collapsed History group.
 */
export function queueGroupOf(theory: TheoryDto, proposal: OptimizationProposalDto | null): QueueGroupKey {
  if (theory.status === TheoryStatus.Proposed || theory.status === TheoryStatus.Validating) return 'inflight';
  if (theory.status === TheoryStatus.Failed) return 'attention';
  if (theory.status === TheoryStatus.Invalidated) return 'history';
  switch (proposal?.status) {
    case ProposalStatus.Accepted: return 'adoption';
    case ProposalStatus.Adopted:
    case ProposalStatus.Rejected: return 'history';
    default: return 'decision';
  }
}

/** Groups theories into the queue, newest first within each group. */
export function groupIntoQueue(
  theories: readonly TheoryDto[],
  proposals: ProposalById,
): Record<QueueGroupKey, TheoryDto[]> {
  const groups: Record<QueueGroupKey, TheoryDto[]> = { decision: [], attention: [], adoption: [], inflight: [], history: [] };
  for (const theory of theories) {
    groups[queueGroupOf(theory, proposalFor(theory, proposals))].push(theory);
  }
  for (const key of Object.keys(groups) as QueueGroupKey[]) {
    groups[key].sort((a, b) => b.createdAt.localeCompare(a.createdAt));
  }
  return groups;
}

/** Counts and outcomes surfaced by the loop strip. */
export interface LoopStats {
  /** Theories currently proposed or mid-A/B. */
  testing: number;
  /** Validated proposals awaiting a promote/dismiss decision. */
  decision: number;
  /** Theories whose A/B validation errored out — retry or dismiss. */
  failed: number;
  /** Promoted proposals waiting to be detected in live traffic. */
  adoption: number;
  /** Terminal outcomes (adopted, dismissed, disproven). */
  decided: number;
  /** Share of A/B-tested theories that validated, 0–100, or null when none tested. */
  winRate: number | null;
  /** Sum of proven pass-rate gains across validated theories, in percentage points. */
  provenGainPt: number;
}

export function loopStats(theories: readonly TheoryDto[], proposals: ProposalById): LoopStats {
  const groups = groupIntoQueue(theories, proposals);
  const validated = theories.filter(t => t.status === TheoryStatus.Validated);
  const invalidated = theories.filter(t => t.status === TheoryStatus.Invalidated);
  // Failed theories were never A/B-tested, so they stay out of the win-rate denominator —
  // an infrastructure outage must not read as a lost experiment.
  const tested = validated.length + invalidated.length;

  const provenGainPt = validated.reduce((sum, t) => {
    const delta = passRateDeltaPt(t);
    return sum + (delta != null && delta > 0 ? delta : 0);
  }, 0);

  return {
    testing: groups.inflight.length,
    decision: groups.decision.length,
    failed: groups.attention.length,
    adoption: groups.adoption.length,
    decided: groups.history.length,
    winRate: tested > 0 ? Math.round((validated.length / tested) * 100) : null,
    provenGainPt,
  };
}

/** Short, human-friendly theory handle, e.g. `thy_6c47`. */
export function theoryShortId(id: string): string {
  const hex = id.replace(/[^0-9a-f]/gi, '').slice(0, 4).toLowerCase();
  return `thy_${hex || '0000'}`;
}

export interface PassRateTransition {
  fromPct: number;
  toPct: number;
  deltaPt: number;
}

/** Baseline → projected pass-rate transition, or null when metrics are absent. */
export function passRateTransition(theory: TheoryDto): PassRateTransition | null {
  if (theory.baselinePassRate == null || theory.projectedPassRate == null) return null;
  const fromPct = Math.round(theory.baselinePassRate * 100);
  const toPct = Math.round(theory.projectedPassRate * 100);
  return { fromPct, toPct, deltaPt: toPct - fromPct };
}

/** Proven gain in percentage points for a validated theory, or null when unavailable. */
export function passRateDeltaPt(theory: TheoryDto): number | null {
  return passRateTransition(theory)?.deltaPt ?? null;
}

/** Significance level above which a difference is treated as sampling noise. */
export const NOISE_THRESHOLD = 0.05;

export function isInsideNoise(pValue: number | null): boolean {
  return pValue != null && pValue >= NOISE_THRESHOLD;
}

/**
 * Caption for a theory's p-value. A *validated* theory whose p-value sits above the threshold only
 * occurs where the significance gate is relaxed (see OptimizationOptions.RequireStatisticalSignificance,
 * used by the kiosk showcase): the candidate did beat the baseline, but the evidence does not rule
 * out luck. Such a row must not claim "significant", and "inside noise" would contradict the win it
 * is displayed next to — so it says plainly which of the two the decision rests on.
 */
export function significanceLabel(pValue: number, validated: boolean): MessageDescriptor {
  if (!isInsideNoise(pValue)) return msg`significant`;
  return validated ? msg`improvement only` : msg`inside noise`;
}

export function formatPValue(pValue: number): string {
  return `p=${pValue.toFixed(2)}`;
}
