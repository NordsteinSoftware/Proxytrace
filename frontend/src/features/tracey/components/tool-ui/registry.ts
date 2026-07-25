import type { ToolCallMessagePartComponent } from '@assistant-ui/react';
import { ChartToolUI } from './ChartToolUI';
import { TableToolUI } from './TableToolUI';
import { TextToolUI } from './TextToolUI';
import { AgentCardToolUI } from './AgentCardToolUI';
import { SuiteCardToolUI } from './SuiteCardToolUI';
import { RunCardToolUI } from './RunCardToolUI';
import { ProposalCardToolUI } from './ProposalCardToolUI';
import { ProviderCardToolUI } from './ProviderCardToolUI';
import { TraceCardToolUI } from './TraceCardToolUI';
import { AskQuestionsToolUI } from './AskQuestionsToolUI';
import { AgentListToolUI } from './AgentListToolUI';
import { SuiteListToolUI } from './SuiteListToolUI';
import { RunListToolUI } from './RunListToolUI';
import { StartTestRunToolUI } from './StartTestRunToolUI';
import { ProposalListToolUI } from './ProposalListToolUI';
import { DashboardStatsToolUI } from './DashboardStatsToolUI';
import { AgentStatsToolUI } from './AgentStatsToolUI';
import { TheoryToolUI } from './TheoryToolUI';
import { AwaitActionsToolUI } from './AwaitActionsToolUI';
import { CaseResultsToolUI } from './CaseResultsToolUI';
import { RunComparisonToolUI } from './RunComparisonToolUI';
import { TraceListToolUI } from './TraceListToolUI';
import { AnomalyListToolUI } from './AnomalyListToolUI';
import { EvaluatorListToolUI } from './EvaluatorListToolUI';
import { TheoryListToolUI } from './TheoryListToolUI';
import { presentGate } from './present-gate';

/** Renders nothing — for plumbing tools (e.g. `load_skill`) whose calls are noise in the thread. */
const HiddenToolUI: ToolCallMessagePartComponent = () => null;

/**
 * Maps a Tracey tool name to the React component that renders its result inline in the chat
 * thread (assistant-ui `MessagePrimitive.Parts` `tools.by_name`). Tools absent here fall back
 * to the diagnostic {@link ToolCallCard} (e.g. `navigate`).
 *
 * **Read tools are wrapped in {@link presentGate}**: they render their full card only when the
 * model set `present: true` (the card *is* the answer the user asked to see); otherwise the call
 * collapses to the slim `ToolCallCard` trace row, so intermediate reads the model did for its own
 * reasoning don't spam the thread. The genuinely purposeful tools — explicit viz (`show_*`), the
 * live actions, the wait/interactive tools, and the mutations — are NOT gated: they always render
 * because they are never noise. Note `get_suite` is gated while the suite *writes*
 * (`create_suite`/`add_to_suite`/`remove_test_case`, same card) are not — a mutation result is a
 * real event worth showing.
 */
export const TRACEY_TOOL_UI: Record<string, ToolCallMessagePartComponent> = {
  load_skill: HiddenToolUI,
  // Always-on: explicit presentation + live/interactive + mutations.
  show_chart: ChartToolUI,
  show_table: TableToolUI,
  show_text: TextToolUI,
  create_suite: SuiteCardToolUI,
  add_to_suite: SuiteCardToolUI,
  remove_test_case: SuiteCardToolUI,
  set_suite_evaluators: SuiteCardToolUI,
  start_test_run: StartTestRunToolUI,
  submit_optimization_theory: TheoryToolUI,
  await_actions: AwaitActionsToolUI,
  ask_questions: AskQuestionsToolUI,
  // Gated reads: full card only when the model opts in with `present: true`.
  get_agent: presentGate(AgentCardToolUI),
  get_suite: presentGate(SuiteCardToolUI),
  get_run: presentGate(RunCardToolUI),
  get_proposal: presentGate(ProposalCardToolUI),
  get_provider: presentGate(ProviderCardToolUI),
  get_trace: presentGate(TraceCardToolUI),
  list_agents: presentGate(AgentListToolUI),
  list_suites: presentGate(SuiteListToolUI),
  list_runs: presentGate(RunListToolUI),
  list_proposals: presentGate(ProposalListToolUI),
  list_theories: presentGate(TheoryListToolUI),
  get_dashboard_stats: presentGate(DashboardStatsToolUI),
  get_agent_stats: presentGate(AgentStatsToolUI),
  get_case_results: presentGate(CaseResultsToolUI),
  compare_runs: presentGate(RunComparisonToolUI),
  find_traces: presentGate(TraceListToolUI),
  get_agent_anomalies: presentGate(AnomalyListToolUI),
  list_evaluators: presentGate(EvaluatorListToolUI),
};
