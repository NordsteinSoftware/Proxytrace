import { Plural } from '@lingui/react/macro';
import type { TestSuiteListItemDto } from '../../api/models';
import { cn } from '../../lib/cn';
import { CheckIcon } from '../icons';
import { RowButton } from '../ui/RowButton';

/**
 * Default `data-testid` prefix — the id the promote flow's e2e selectors already use, so
 * extracting this out of PromoteModal changed no test.
 */
// eslint-disable-next-line lingui/no-unlocalized-strings -- test id token, not UI copy
const DEFAULT_TEST_ID_PREFIX = 'promote-suite-option';

interface Props {
  suites: TestSuiteListItemDto[];
  value: string;
  onChange: (suiteId: string) => void;
  /** Prefix for each row's `data-testid`. */
  testIdPrefix?: string;
}

/**
 * Destination-suite picker: one selectable row per suite of the trace's agent. Purely
 * presentational — the caller owns which suite is selected and what happens next.
 */
export function SuitePicker({ suites, value, onChange, testIdPrefix = DEFAULT_TEST_ID_PREFIX }: Props) {
  return (
    <div className="flex-1 min-h-0 overflow-y-auto flex flex-col gap-1.5" data-testid="suite-picker">
      {suites.map(suite => {
        const isSelected = suite.id === value;
        return (
          <RowButton
            key={suite.id}
            data-testid={`${testIdPrefix}-${suite.id}`}
            onClick={() => onChange(suite.id)}
            className={cn(
              'px-3 py-2.5 transition-all duration-150 flex items-start gap-2',
              isSelected
                ? 'bg-accent-subtle shadow-[inset_0_0_0_1.5px_color-mix(in_srgb,var(--accent-primary)_67%,transparent)]'
                : 'bg-card-2 shadow-[inset_0_0_0_1px_var(--border-color)]',
            )}
          >
            <span
              className={cn(
                'w-[14px] h-[14px] mt-0.5 shrink-0 flex items-center justify-center transition-all duration-150',
                isSelected
                  ? 'bg-accent border border-accent'
                  : 'bg-transparent border border-border shadow-none',
              )}
            >
              {isSelected && (
                <span className="text-accent-ink inline-flex"><CheckIcon size={9} strokeWidth={3} /></span>
              )}
            </span>
            <span className="flex-1 min-w-0">
              <span className={cn('block text-body font-semibold truncate', isSelected ? 'text-primary' : 'text-secondary')}>
                {suite.name}
              </span>
              <span className="block text-caption text-muted mt-0.5">
                <Plural value={suite.testCaseCount} one="# case" other="# cases" />
                {' · '}
                <Plural value={suite.evaluators.length} one="# evaluator" other="# evaluators" />
              </span>
            </span>
          </RowButton>
        );
      })}
    </div>
  );
}
