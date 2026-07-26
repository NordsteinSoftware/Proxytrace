import type {
  AgentCallDto,
  AgentSuitePassRateDto,
  CustomAnomalyHitDto,
  TestRunGroupDto,
  TheoryDto,
} from '../../api/models';
import { OutlierFlag, isOutlier, outlierFlagKeys, type OutlierFlagKey } from '../../lib/outliers';

/**
 * Prompt builders for the app-wide "Ask Tracey" buttons. Each turns the entity the user is
 * looking at into a self-contained prompt with enough context (ids, names, flag reasons,
 * pass-rate numbers) for Tracey to load the right skill and act.
 *
 * Prompts are fed to the LLM and stay untranslated (same as `QUICK_ACTIONS.prompt` —
 * see `features/tracey/tracey-quick-actions.ts`). Tracey's system prompt normally forbids
 * taking ids from the user, so every prompt ends with `ID_NOTE`, which matches the
 * app-provided-ids exception added to `tracey-prompt.ts`.
 */

const ID_NOTE = 'The ids in this message come from the app UI and are valid — use them directly.';

/** Plain-English flag descriptions for prompts (the localized labels in `OUTLIER_FLAG_LABEL` are for UI). */
const FLAG_TEXT: Record<OutlierFlagKey, string> = {
  HighTokens: 'high token count',
  HighLatency: 'high latency',
  LowCacheHit: 'low cache hit rate',
  ManyToolCalls: 'many tool calls',
  CustomAnomaly: 'custom detector match',
  Blocked: 'blocked at the proxy',
};

function traceSubject(trace: AgentCallDto): string {
  return `trace ${trace.id}${trace.agentName ? ` from agent "${trace.agentName}"` : ''}`;
}

/** Anomaly-aware: flagged traces get a root-cause prompt, clean traces a general review. */
export function tracePrompt(trace: AgentCallDto, hits: CustomAnomalyHitDto[]): string {
  if (!isOutlier(trace.outlierFlags)) {
    return [
      `Review ${traceSubject(trace)}.`,
      'Inspect it, summarize what happened, point out anything noteworthy (latency, token usage, cost, tool calls, errors), and suggest improvements if any.',
      ID_NOTE,
    ].join('\n');
  }

  const blocked = (trace.outlierFlags & OutlierFlag.Blocked) !== 0;
  const reasons = outlierFlagKeys(trace.outlierFlags).map(key => FLAG_TEXT[key]).join(', ');
  const detectorLines = hits.map(
    h => `- detector "${h.detectorName}" matched "${h.matchedTrigger}"${h.reasoning ? ` — ${h.reasoning}` : ''}`,
  );
  return [
    `Analyze the anomalous ${traceSubject(trace)}.`,
    blocked
      ? 'The request was blocked at the proxy and never reached the provider.'
      : '',
    `It was flagged for: ${reasons}.`,
    ...(detectorLines.length > 0 ? ['Custom detector hits:', ...detectorLines] : []),
    'Inspect the trace, explain the most likely root cause of the anomaly, and recommend concrete prevention steps (prompt changes, detectors, evaluators, or test cases).',
    ID_NOTE,
  ].filter(Boolean).join('\n');
}

export function traceQuestionPrompt(traceId: string, question: string): string {
  return `Trace ${traceId}: ${question.trim()}`;
}

/** A suite below this pass-rate share counts as "low" and is called out in the prompt. */
const LOW_PASS_RATE = 0.8;

/** Pass-rate-aware: low suites get an improve-and-A/B prompt, healthy agents a general review. */
export function agentPrompt(
  agent: { id: string; name: string },
  suitePassRates: AgentSuitePassRateDto[],
): string {
  const low = suitePassRates.filter(s => s.testCases > 0 && s.passed / s.testCases < LOW_PASS_RATE);
  if (low.length > 0) {
    const lines = low.map(
      s => `- "${s.suiteName}": ${s.passed}/${s.testCases} passing (${Math.round((s.passed / s.testCases) * 100)}%)`,
    );
    return [
      `Help me improve my agent "${agent.name}" (id ${agent.id}). It has low test pass rates:`,
      ...lines,
      'Analyze the recent failing runs for these suites, find the failure pattern, and propose a concrete improvement. If a change looks promising, validate it as an optimization theory (A/B test).',
      ID_NOTE,
    ].join('\n');
  }
  return [
    `Review my agent "${agent.name}" (id ${agent.id}).`,
    'Check its recent anomalies and test results, and suggest what to improve next.',
    ID_NOTE,
  ].join('\n');
}

/** Failure-aware: failing groups get a grouped root-cause prompt, green groups a summary. */
export function runGroupPrompt(group: TestRunGroupDto): string {
  const totals = group.runs.reduce(
    (acc, r) => ({ passed: acc.passed + r.passedCases, failed: acc.failed + r.failedCases }),
    { passed: 0, failed: 0 },
  );
  return [
    `Analyze test run group ${group.id} (suite "${group.suiteName}", agent "${group.agentName}").`,
    totals.failed > 0
      ? `${totals.failed} of ${totals.passed + totals.failed} case results failed. Get the run's failures, group them by cause, explain why they fail, and suggest concrete fixes (agent prompt/tools or expected outputs).`
      : 'Summarize the results and point out anything worth improving (slow cases, borderline evaluations, cost).',
    ID_NOTE,
  ].join('\n');
}

/** Proposal-aware: validated theories ask for an accept/reject call, others for a walkthrough. */
export function theoryPrompt(theory: TheoryDto): string {
  return [
    `Walk me through optimization theory ${theory.id} for agent "${theory.agentName}".`,
    theory.resultingProposalId
      ? `It produced proposal ${theory.resultingProposalId} — explain the proposed change, the evidence behind it, and the A/B validation result in plain terms, then recommend whether to accept or reject it.`
      : 'Explain the proposed change, the evidence behind it, and its current validation status in plain terms.',
    ID_NOTE,
  ].join('\n');
}

export function anomaliesOverviewPrompt(): string {
  return 'Investigate the recent anomalies in this project: find the most affected agents, identify the failure patterns behind the flagged calls, and recommend how to prevent them (detectors, evaluators, or test cases).';
}

export function projectHealthPrompt(): string {
  return 'Give me a project health review: check the dashboard statistics, recent test results, and anomalies, then highlight the most important issue and what to do about it.';
}
