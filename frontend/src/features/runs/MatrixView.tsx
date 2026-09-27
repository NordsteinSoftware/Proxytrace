import { useState, useMemo } from 'react';
import { useSearchParams } from 'react-router';
import { Trans, useLingui } from '@lingui/react/macro';
import type { TestRunGroupDto } from '../../api/models';
import { Card } from '../../components/ui/Card';
import { isActive, runsComplete } from './results';
import { buildLeaderboard } from './comparison';
import { useCurrentEndpointId } from './hooks/useAgentEndpoint';
import type { LiveProgress } from './live';
import {
  buildCohorts,
  buildCohortRows,
  matrixCounts,
  filterSortMatrixRows,
  type MatrixFilter,
  type MatrixSort,
} from './cohorts';
import { SegmentedControl } from '../../components/ui/SegmentedControl';
import { MatrixGrid, type MatrixSelection } from './components/MatrixGrid';
import { ComparisonDrawer } from './drawers';

/** Cases × endpoints grid, divergence-first. Each column averages an endpoint's N samples; live
 * progress overlays in-flight cases. Single-sample endpoints render exactly as a one-run column.
 * Container: owns filter/sort/selection state + URL deep-link; {@link MatrixGrid} renders the grid. */
export function MatrixView({ group, live }: {
  group: TestRunGroupDto;
  live?: LiveProgress;
}) {
  const { t } = useLingui();
  const cohorts = useMemo(() => buildCohorts(group.runs), [group.runs]);
  const currentEndpointId = useCurrentEndpointId(group.agentId);
  const complete = runsComplete(group.runs);
  const baseline = buildLeaderboard(cohorts, complete, currentEndpointId).find(e => e.isBaseline);
  const baselineEndpointId = complete ? baseline?.run.endpointId ?? null : null;
  const [searchParams, setSearchParams] = useSearchParams();
  // eslint-disable-next-line lingui/no-unlocalized-strings -- URL query-param key
  const caseParam = searchParams.get('case');
  // eslint-disable-next-line lingui/no-unlocalized-strings -- filter state token, not UI copy
  const [filter, setFilter] = useState<MatrixFilter>('all');
  // eslint-disable-next-line lingui/no-unlocalized-strings -- sort state token, not UI copy
  const [sort, setSort] = useState<MatrixSort>('order');
  const [selectedCase, setSelectedCase] = useState<MatrixSelection | null>(null);

  // Freeze row order while any run is in flight so rows don't reshuffle on every partial verdict.
  const active = group.runs.some(r => isActive(r.status));
  const allRows = useMemo(() => buildCohortRows(cohorts, live), [cohorts, live]);

  // Deep link (?case=) opens the drawer for one case. Derived from the URL (no effect) so it
  // appears as soon as the row loads; local selection takes precedence once the user clicks.
  const urlCase = useMemo<MatrixSelection | null>(() => {
    if (!caseParam) return null;
    const row = allRows.find(r => r.caseId === caseParam);
    return row ? { caseId: row.caseId, summary: row.summary } : null;
  }, [caseParam, allRows]);
  const openCase = selectedCase ?? urlCase;

  const clearCaseParam = () => setSearchParams(prev => {
    const next = new URLSearchParams(prev);
    // eslint-disable-next-line lingui/no-unlocalized-strings -- URL query-param key
    next.delete('case');
    return next;
  }, { replace: true });
  const closeDrawer = () => { setSelectedCase(null); if (caseParam) clearCaseParam(); };
  const counts = matrixCounts(allRows, baselineEndpointId);
  const rows = useMemo(() => filterSortMatrixRows(allRows, filter, sort, active, baselineEndpointId), [allRows, filter, sort, active, baselineEndpointId]);

  const multi = cohorts.length > 1;
  const sampled = cohorts.some(c => c.sampleCount > 1);
  const selIdx = openCase ? rows.findIndex(r => r.caseId === openCase.caseId) : -1;

  return (
    <Card padding="none" data-testid="matrix-view" className="flex flex-col">
      {/* Toolbar */}
      <div className="flex items-center justify-between gap-3 flex-wrap px-4 py-2.5 border-b border-hairline">
        <div className="flex items-baseline gap-2.5 min-w-0">
          <span className="text-h2 font-semibold"><Trans>Test case matrix</Trans></span>
          <span className="text-body-sm text-muted">
            {multi
              ? <Trans>{counts.all} cases × {cohorts.length} models</Trans>
              : <Trans>{counts.all} cases × 1 model</Trans>}
            {sampled && <> · <Trans>×{group.sampleCount} samples</Trans></>}
          </span>
        </div>
        <div className="flex items-center gap-1.5 flex-wrap">
          <SegmentedControl
            value={filter}
            onChange={setFilter}
            segments={[
              { value: 'all', label: t`All`, count: counts.all },
              ...(multi ? [{ value: 'divergent' as const, label: t`Divergent`, count: counts.divergent }] : []),
              ...(sampled ? [{ value: 'flaky' as const, label: t`Flaky`, count: counts.flaky }] : []),
              { value: 'failing', label: t`Failing`, count: counts.failing },
              { value: 'passing', label: t`Passing`, count: counts.passing },
            ]}
          />
          <SegmentedControl
            value={sort}
            onChange={setSort}
            segments={[{ value: 'order', label: t`Order` }, { value: 'worst', label: t`Worst` }]}
          />
        </div>
      </div>

      {multi && baselineEndpointId && baseline && (
        <div className="flex flex-wrap items-center gap-2 border-b border-hairline px-4 py-2.5" data-testid="matrix-changes">
          <span className="text-body-sm text-secondary"><Trans>Compared with {baseline.run.endpointName}</Trans></span>
          <SegmentedControl value={filter} onChange={setFilter} segments={[
            { value: 'regressions', label: t`Regressions`, count: counts.regressions, testId: 'matrix-regressions' },
            { value: 'improvements', label: t`Improvements`, count: counts.improvements, testId: 'matrix-improvements' },
            { value: 'unchanged', label: t`Unchanged`, count: counts.unchanged, testId: 'matrix-unchanged' },
          ]} />
          <span className="text-body-sm text-muted"><Trans>Only completed, consistently judged cases are compared.</Trans></span>
        </div>
      )}

      {/* Matrix */}
      {rows.length === 0 ? (
        <div className="py-15 text-center text-muted text-body"><Trans>No cases match this filter.</Trans></div>
      ) : (
        <MatrixGrid
          cohorts={cohorts}
          rows={rows}
          allRows={allRows}
          active={active}
          selectedCaseId={openCase?.caseId ?? null}
          onSelectCase={setSelectedCase}
        />
      )}

      {openCase && selIdx >= 0 && (
        <ComparisonDrawer
          runs={group.runs}
          sampleCount={group.sampleCount}
          caseInfo={{ id: openCase.caseId, summary: openCase.summary, idx: selIdx, total: rows.length }}
          focusEndpointId={openCase.focusEndpointId}
          nav={{
            onClose: closeDrawer,
            onPrev: selIdx > 0 ? () => { const p = rows[selIdx - 1]; setSelectedCase({ caseId: p.caseId, summary: p.summary }); } : undefined,
            onNext: selIdx < rows.length - 1 ? () => { const n = rows[selIdx + 1]; setSelectedCase({ caseId: n.caseId, summary: n.summary }); } : undefined,
          }}
        />
      )}
    </Card>
  );
}
