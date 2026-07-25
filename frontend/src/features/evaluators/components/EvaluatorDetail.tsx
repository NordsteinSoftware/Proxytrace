import { useState } from 'react';
import { useNavigate } from 'react-router';
import { cn } from '../../../lib/cn';
import { type RangeKey } from '../../../lib/time-range';
import { EvaluatorKind, type EvaluationScore, type EvaluatorDetailDto } from '../../../api/models';
import type { RecentEvaluationItemDto } from '../../../api/evaluators';
import { KIND_CATEGORY } from '../evaluatorMeta';
import { useEvaluatorDetail, useEvaluatorRecentByScore } from '../hooks/useEvaluatorQueries';
import { WorkspaceHeader } from './WorkspaceHeader';
import { PerformancePanel } from './PerformancePanel';
import { DefinitionPanel } from './DefinitionPanel';
import { ScoreDistributionPanel } from './ScoreDistributionPanel';
import { CostPanel } from './CostPanel';
import { AttachedPanel, type AttachedSuite, type AttachedAgent } from './AttachedPanel';
import { RecentEvaluationsTable } from './RecentEvaluationsTable';

interface Props {
  evaluator: EvaluatorDetailDto;
  attachedSuites: AttachedSuite[];
  range: RangeKey;
  onRangeChange: (r: RangeKey) => void;
  onEdit: () => void;
  onDelete: () => void;
  onTestBench: (id: string) => void;
}

/** Full detail view for one evaluator — stacks the header and all stat panels. */
export function EvaluatorDetail({ evaluator: e, attachedSuites, range, onRangeChange, onEdit, onDelete, onTestBench }: Props) {
  const navigate = useNavigate();
  const cat = KIND_CATEGORY[e.kind];
  const showCost = e.kind === EvaluatorKind.Agentic;

  // Clicking a score bar filters the recent-evaluations table; clicking it again clears.
  const [scoreFilter, setScoreFilter] = useState<EvaluationScore | null>(null);

  const agents: AttachedAgent[] = Array.from(
    new Map(attachedSuites.map(s => [s.agentId, { id: s.agentId, name: s.agentName }])).values(),
  );

  const { data: detail, isLoading } = useEvaluatorDetail(e.id, range);
  const overview = detail?.overview ?? null;

  // When a score is selected, pull matching evaluations from the server (the last-8 set the detail
  // view returns may not contain that score even though the distribution counts it over the range).
  const scored = useEvaluatorRecentByScore(e.id, scoreFilter);
  const recentRows = scoreFilter ? (scored.data ?? []) : (detail?.recentEvaluations ?? []);
  const recentLoading = scoreFilter ? scored.isLoading : isLoading;

  function openResult(row: RecentEvaluationItemDto) {
    if (!row.runId) return;
    navigate(`/runs?run=${encodeURIComponent(row.runId)}&case=${encodeURIComponent(row.testCaseId)}`);
  }

  return (
    <div data-testid="evaluator-detail" className="fade-up flex flex-col gap-3.5 @container">
      <WorkspaceHeader evaluator={e} onEdit={onEdit} onDelete={onDelete} onTestBench={() => onTestBench(e.id)} />

      <PerformancePanel evaluator={e} overview={overview} range={range} onRangeChange={onRangeChange} />

      <DefinitionPanel evaluator={e} />

      <div className={cn('grid gap-3.5', showCost ? 'grid-cols-1 @3xl:grid-cols-[minmax(0,1.4fr)_minmax(0,1fr)]' : 'grid-cols-1')}>
        <ScoreDistributionPanel
          buckets={overview?.scoreDistribution ?? []}
          category={cat}
          totalRuns={overview?.summary.totalEvaluations ?? 0}
          range={range}
          selectedScore={scoreFilter}
          onSelectScore={s => setScoreFilter(prev => (prev === s ? null : s))}
        />
        {showCost && (
          <CostPanel overview={overview} category={cat} modelName={e.endpointName ?? null} range={range} />
        )}
      </div>

      <AttachedPanel
        suites={attachedSuites}
        agents={agents}
        onOpenSuite={id => navigate(`/suites?id=${encodeURIComponent(id)}`)}
        onOpenAgent={id => navigate(`/agents?id=${encodeURIComponent(id)}`)}
      />

      <RecentEvaluationsTable
        rows={recentRows}
        isLoading={recentLoading}
        scoreFilter={scoreFilter}
        onClearFilter={() => setScoreFilter(null)}
        onOpenResult={openResult}
      />
    </div>
  );
}
