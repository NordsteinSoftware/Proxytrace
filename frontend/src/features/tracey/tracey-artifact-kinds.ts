import type {
  AgentCallDto,
  AgentCallListItemDto,
  AgentDto,
  AgentEntityCountsDto,
  AgentListItemDto,
  AgentTimeSummaryDto,
  DashboardViewDto,
  EvaluatorDetailDto,
  OptimizationProposalDto,
  ProviderDto,
  TestRunDto,
  TestRunGroupDto,
  TestCaseProposalSetDto,
  TestSuiteDto,
  TestSuiteListItemDto,
  TheoryDto,
} from '../../api/models';
import type { TestRunStatus } from '../../api/models';
import type { ChartArtifact, TableArtifact, TextArtifact } from './tracey-artifacts';
import type { RunComparison } from './tools/run-analysis';
import type { CaseResult } from './tools/case-verdict';

/** The payload `get_agent_stats` stores: the 30-day summary plus the agent's entity counts. */
export interface AgentStatsArtifact {
  summary: AgentTimeSummaryDto;
  counts: AgentEntityCountsDto;
}

/**
 * The payload `get_case_results` stores: the run's identity plus a verdict per case. Named for
 * *cases*, not failures — with `caseIds` it reports passing cases too, which is what a green
 * assertion needs and what the old failures-only shape could not express.
 */
export interface CaseResultsArtifact {
  runId: string;
  suiteName: string | null;
  agentName: string;
  runStatus: TestRunStatus;
  passRate: number;
  totalCases: number;
  /** What the caller expected these cases to do — labels the card red/green. Null when unstated. */
  expect: 'pass' | 'fail' | null;
  cases: CaseResult[];
}

/**
 * The single contract between what a tool **stores** and what its card **reads back**: one entry
 * per artifact `kind`, mapping it to the payload type. `StoreFn` (`tools/shared.ts`) only accepts
 * a payload matching its kind, and `useArtifactResult(kind, …)` returns exactly that type (and
 * verifies the kind at runtime) — so a tool and its card can no longer silently disagree about
 * the payload shape (e.g. storing a list-item DTO while the card reads the full DTO).
 */
export interface ArtifactPayloads {
  'agent-list': AgentListItemDto[];
  agent: AgentDto;
  'suite-list': TestSuiteListItemDto[];
  'evaluator-list': EvaluatorDetailDto[];
  suite: TestSuiteDto;
  'test-case-proposals': TestCaseProposalSetDto;
  'run-list': TestRunDto[];
  run: TestRunDto;
  'case-results': CaseResultsArtifact;
  'run-comparison': RunComparison;
  'test-run-group': TestRunGroupDto;
  'trace-list': AgentCallListItemDto[];
  'theory-list': TheoryDto[];
  theory: TheoryDto;
  'proposal-list': OptimizationProposalDto[];
  proposal: OptimizationProposalDto;
  'dashboard-stats': DashboardViewDto;
  'agent-stats': AgentStatsArtifact;
  provider: ProviderDto;
  trace: AgentCallDto;
  chart: ChartArtifact;
  table: TableArtifact;
  text: TextArtifact;
}

export type ArtifactKind = keyof ArtifactPayloads;
