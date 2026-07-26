import { Trans, useLingui } from '@lingui/react/macro';
import { cn } from '../../../lib/cn';
import { Tooltip } from '../../../components/ui/Tooltip';
import { AlertTriangleIcon, ArrowDownIcon, ArrowUpIcon } from '../../../components/icons';
import {
  COL_HEADERS,
  COL_HEADER_LABELS,
  COL_VIS_CLS,
  SORT_FIELD_BY_COL,
  TRACE_GRID_CLS,
} from '../tracesMeta';
import type { TraceSort, TraceSortField } from '../tracesMeta';

// eslint-disable-next-line lingui/no-unlocalized-strings -- CSS utility classes, not UI copy
const HEADER_TEXT_CLS = 'text-body-sm font-semibold text-secondary uppercase tracking-[0.06em]';

function SortableHeader({ label, field, sort, onSortChange, alignRight }: {
  label: string;
  field: TraceSortField;
  sort: TraceSort;
  onSortChange: (field: TraceSortField) => void;
  alignRight: boolean;
}) {
  const active = sort.field === field;
  return (
    // eslint-disable-next-line no-restricted-syntax -- bespoke sortable column header; Button's ghost padding/height doesn't fit the dense sticky header row
    <button
      type="button"
      data-testid={`traces-sort-${field}`}
      onClick={() => onSortChange(field)}
      className={cn(
        HEADER_TEXT_CLS,
        'inline-flex items-center gap-1 cursor-pointer bg-transparent p-0 border-0',
        'transition-colors duration-[var(--motion-fast)] hover:text-secondary',
        'focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[color-mix(in_srgb,var(--accent-primary)_60%,transparent)] rounded-sm',
        alignRight && 'justify-end',
        active && 'text-accent-text',
      )}
    >
      {label}
      {active && (sort.desc ? <ArrowDownIcon size={10} /> : <ArrowUpIcon size={10} />)}
    </button>
  );
}

interface PositionProps {
  /** 1-based index of the first visible trace row, or 0 when the list is empty. */
  first: number;
  /** 1-based index of the last visible trace row. */
  last: number;
  /** Total traces matching the current filters. */
  total: number;
  /** Live traces arrived while the reader was scrolled; the list refreshes on return to the top. */
  pendingRefresh: boolean;
}

/**
 * Where the reader is in the set. This is what replaced the paging stepper: the same orientation the
 * page number gave, but as a readout rather than a control, since scrolling is now how you move.
 */
function PositionReadout({ first, last, total, pendingRefresh }: PositionProps) {
  return (
    <span
      data-testid="trace-position-readout"
      className="ml-auto flex items-center gap-1.5 font-mono text-caption text-muted tabular-nums whitespace-nowrap"
    >
      {pendingRefresh && (
        <>
          {/* Colour and motion alone can never carry meaning (DESIGN.md §7), so the dot is decorative
              and the live region below states it in words. */}
          <span aria-hidden className="pulse-dot inline-block w-1.5 h-1.5 rounded-full bg-accent" />
          <span className="sr-only" aria-live="polite">
            <Trans>New traces available. Scroll to the top to refresh.</Trans>
          </span>
        </>
      )}
      {total > 0 && <Trans>{first.toLocaleString()}–{last.toLocaleString()} of {total.toLocaleString()}</Trans>}
    </span>
  );
}

interface Props {
  sort: TraceSort;
  onSortChange: (field: TraceSortField) => void;
  position: PositionProps;
}

/** Sticky column header for the trace list. Sits outside the virtualized area so it never scrolls. */
export function TraceTableHeader({ sort, onSortChange, position }: Props) {
  const { i18n } = useLingui();

  return (
    <div className="sticky top-0 z-10 bg-card border-b border-hairline">
      <div className={cn('grid items-center px-4 py-2', TRACE_GRID_CLS)}>
        {COL_HEADERS.map((header, i) => {
          const headerLabel = i18n._(COL_HEADER_LABELS[i]);
          const isAnomaly = header === '';
          const sortField = SORT_FIELD_BY_COL[i];
          const alignRight = i === COL_HEADERS.length - 1;

          if (sortField) {
            return (
              <span key={i} className={cn(alignRight && 'text-right', COL_VIS_CLS[i])}>
                <SortableHeader
                  label={headerLabel}
                  field={sortField}
                  sort={sort}
                  onSortChange={onSortChange}
                  alignRight={alignRight}
                />
              </span>
            );
          }

          return (
            <span
              key={i}
              className={cn(
                HEADER_TEXT_CLS,
                isAnomaly && 'flex items-center justify-center',
                alignRight && 'text-right',
                COL_VIS_CLS[i],
              )}
            >
              {isAnomaly ? (
                <Tooltip content={headerLabel}>
                  <span aria-label={headerLabel} className="inline-flex text-muted">
                    <AlertTriangleIcon size={13} />
                  </span>
                </Tooltip>
              ) : (
                headerLabel
              )}
            </span>
          );
        })}
      </div>

      <div className="flex px-4 pb-1.5">
        <PositionReadout {...position} />
      </div>
    </div>
  );
}
