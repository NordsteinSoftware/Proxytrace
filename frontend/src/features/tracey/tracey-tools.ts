import { type TraceyTool, type TraceyToolContext, makeStore } from './tools/shared';
import { createNavigationTools } from './tools/navigation';
import { createAgentTools } from './tools/agents';
import { createSuiteTools } from './tools/suites';
import { createRunTools } from './tools/runs';
import { createProposalTools } from './tools/proposals';
import { createEvaluatorTools } from './tools/evaluators';
import { createStatsTools } from './tools/stats';
import { createProviderTools } from './tools/providers';
import { createTraceTools } from './tools/traces';
import { createDisplayTools } from './tools/display';
import { createAwaitTools } from './tools/await';

export { CANCELLED } from './tools/shared';
export type { TraceyTool, TraceyToolContext } from './tools/shared';

/**
 * Build the full Tracey tool set against a runtime context. The tool definitions are grouped by
 * domain under `./tools/*`; this is the composition root that wires each per-domain factory to a
 * shared artifact store. The backend stores no copy of these tools — it captures the prompt +
 * tools from the wire on Tracey's first call and versions them under her name-attributed agent.
 */
export function createTraceyTools(ctx: TraceyToolContext): Record<string, TraceyTool> {
  const store = makeStore(ctx);
  return {
    ...createNavigationTools(ctx, store),
    ...createAgentTools(ctx, store),
    ...createSuiteTools(ctx, store),
    ...createRunTools(ctx, store),
    ...createProposalTools(ctx, store),
    ...createEvaluatorTools(ctx, store),
    ...createStatsTools(ctx, store),
    ...createProviderTools(ctx, store),
    ...createTraceTools(ctx, store),
    ...createDisplayTools(ctx, store),
    ...createAwaitTools(ctx, store),
  };
}

/**
 * Static name + description for every Tracey tool, for the slash menu / chips. Must stay in sync
 * with {@link createTraceyTools} — every tool defined there has an entry here.
 */
export const TRACEY_TOOLS_META: { name: string; description: string }[] = [
  { name: 'navigate', description: 'Open an in-app page.' },
  { name: 'search_docs', description: 'Search the product manual and cite sources.' },
  { name: 'load_skill', description: 'Load a task playbook (skill) on demand.' },
  { name: 'list_agents', description: 'List the agents in the project.' },
  { name: 'get_agent', description: 'Get one agent by id.' },
  { name: 'list_suites', description: 'List the test suites.' },
  { name: 'get_suite', description: 'Get one test suite by id.' },
  { name: 'create_suite', description: 'Create a suite for an agent from captured traces (confirm).' },
  { name: 'add_to_suite', description: 'Add captured traces to a suite as test cases (confirm).' },
  { name: 'remove_test_case', description: 'Remove a test case from a suite (confirm).' },
  { name: 'update_expected_output', description: "Set a test case's expected output (confirm)." },
  { name: 'cancel_test_run', description: 'Cancel an in-progress test run (confirm).' },
  { name: 'list_runs', description: 'List recent test runs.' },
  { name: 'get_run', description: 'Get one test run by id.' },
  { name: 'get_run_failures', description: "Analyze a run's failing cases (evaluator verdicts)." },
  { name: 'compare_runs', description: 'Compare two runs case by case (fixed vs regressed).' },
  { name: 'list_proposals', description: 'List optimization proposals.' },
  { name: 'get_proposal', description: 'Get one proposal by id.' },
  { name: 'list_theories', description: 'List past optimization theories and their A/B outcomes.' },
  { name: 'get_provider', description: 'Get one model provider by id.' },
  { name: 'find_traces', description: 'Search captured traces (agent, text, status).' },
  { name: 'get_trace', description: 'Get one captured trace by id (verbose: the whole conversation).' },
  { name: 'get_agent_anomalies', description: "Recent anomaly-flagged (outlier) calls of an agent." },
  { name: 'list_evaluators', description: "List the project's evaluators." },
  { name: 'create_evaluator', description: 'Create an evaluator to score suites with (confirm).' },
  { name: 'get_dashboard_stats', description: 'Aggregate dashboard statistics.' },
  { name: 'get_agent_stats', description: 'Token usage, cost & latency for an agent.' },
  { name: 'start_test_run', description: 'Run a suite against an agent (confirm).' },
  { name: 'set_proposal_status', description: 'Approve or reject a proposal (confirm).' },
  { name: 'submit_optimization_theory', description: 'Theorize an agent optimization and A/B-test it (confirm).' },
  { name: 'await_actions', description: 'Wait for test runs / theories to finish, then react.' },
  { name: 'show_chart', description: 'Plot data inline in the chat.' },
  { name: 'show_table', description: 'Show a table inline in the chat.' },
  { name: 'show_text', description: 'Show markdown/JSON/code inline in the chat.' },
  { name: 'ask_questions', description: 'Ask the user one or more clarifying questions inline.' },
];
