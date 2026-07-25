import { useState } from 'react';
import { Plural, useLingui } from '@lingui/react/macro';
import { useSearchParams } from 'react-router';
import { SearchIcon } from '../../components/icons';
import { Input } from '../../components/ui/Input';
import { SegmentedControl, type Segment } from '../../components/ui/SegmentedControl';
import { FilterDropdown } from '../../components/ui/FilterDropdown';
import { Pagination } from '../../components/ui/Pagination';
import { useDebounce } from '../../hooks/useDebounce';
import { useLocalStorageState } from '../../hooks/useLocalStorageState';
import { ApplicationErrorLevel, type ApplicationErrorDto } from '../../api/models';
import { useErrorLogQuery, useErrorLogEntry, PAGE_SIZE, PAGE_SIZE_OPTIONS } from './hooks/useErrorLogQueries';
import { ErrorLogTable } from './components/ErrorLogTable';
import { ErrorLogDetail } from './components/ErrorLogDetail';
import { TimeRangePicker } from '../../components/ui/TimeRangePicker';
import { ALL_TIME, resolveRange, type TimeRange } from '../../lib/timeRange';

type LevelFilter = 'all' | ApplicationErrorLevel;

export default function ErrorLog() {
  const { t } = useLingui();
  const LEVEL_SEGMENTS: Segment<LevelFilter>[] = [
    { value: 'all', label: t`All` },
    { value: ApplicationErrorLevel.Error, label: t`Error` },
    { value: ApplicationErrorLevel.Critical, label: t`Critical` },
  ];
  const [page, setPage] = useState(1);
  const [storedPageSize, setStoredPageSize] = useLocalStorageState<number>('errorLog.pageSize', PAGE_SIZE);
  // Guard against a stale/garbage stored value — only accept a known option.
  const pageSize = (PAGE_SIZE_OPTIONS as readonly number[]).includes(storedPageSize) ? storedPageSize : PAGE_SIZE;
  // eslint-disable-next-line lingui/no-unlocalized-strings -- filter state token, not UI copy
  const [levelFilter, setLevelFilter] = useState<LevelFilter>('all');
  const [search, setSearch] = useState('');
  const [timeRange, setTimeRange] = useState<TimeRange>(ALL_TIME);
  const [selected, setSelected] = useState<ApplicationErrorDto | null>(null);

  // Deep link from an error toast: `?error=<id>` fetches that captured error and pre-selects it.
  // A manual row click (`selected`) always wins; closing the detail clears both.
  const [searchParams, setSearchParams] = useSearchParams();
  // eslint-disable-next-line lingui/no-unlocalized-strings -- URL query-param key
  const deepLinkId = searchParams.get('error');
  const { data: deepLinkedError } = useErrorLogEntry(selected ? null : deepLinkId);
  const activeError = selected ?? deepLinkedError ?? null;

  function closeDetail() {
    setSelected(null);
    if (deepLinkId) {
      const next = new URLSearchParams(searchParams);
      // eslint-disable-next-line lingui/no-unlocalized-strings -- URL query-param key
      next.delete('error');
      setSearchParams(next, { replace: true });
    }
  }

  const debouncedSearch = useDebounce(search, 200);
  const trimmedSearch = debouncedSearch.trim();

  const { errors, total, isFetching } = useErrorLogQuery({
    page,
    pageSize,
    ...(levelFilter !== 'all' ? { level: levelFilter } : {}),
    ...(trimmedSearch.length >= 2 ? { search: trimmedSearch } : {}),
    ...resolveRange(timeRange),
  });

  function handleLevelChange(value: LevelFilter) {
    setLevelFilter(value);
    setPage(1);
  }

  function handleSearchChange(value: string) {
    setSearch(value);
    setPage(1);
  }

  function handleTimeRangeChange(range: TimeRange) {
    setTimeRange(range);
    setPage(1);
  }

  function handlePageSizeChange(value: number) {
    setStoredPageSize(value);
    setPage(1);
  }

  return (
    <div className="w-full min-w-0 h-full min-h-0 flex flex-col gap-3.5">
      <div className="shrink-0 flex items-center gap-3 flex-wrap">
        <div className="flex-1 min-w-[220px] max-w-[460px]">
          <Input
            leftAddon={<SearchIcon size={13} />}
            value={search}
            onChange={e => handleSearchChange(e.target.value)}
            placeholder={t`Search message or stacktrace…`}
            aria-label={t`Search errors by message or stacktrace`}
            data-testid="error-log-search"
          />
        </div>
        <TimeRangePicker value={timeRange} onChange={handleTimeRangeChange} testId="error-log-time" />
        <div className="ml-auto">
          <SegmentedControl value={levelFilter} onChange={handleLevelChange} segments={LEVEL_SEGMENTS} />
        </div>
      </div>

      <div className="bg-card border border-border rounded-xl overflow-hidden">
        <ErrorLogTable
          errors={errors}
          selectedId={activeError?.id ?? null}
          onSelect={setSelected}
          isFetching={isFetching}
        />
      </div>

      <footer data-testid="error-log-pagination" className="flex items-center justify-between gap-3 shrink-0">
        <FilterDropdown
          label={t`Per page:`}
          value={String(pageSize)}
          active
          direction="up"
          options={PAGE_SIZE_OPTIONS.map(n => ({ key: String(n), label: String(n) }))}
          onChange={key => handlePageSizeChange(Number(key))}
          width={110}
        />
        <Pagination page={page} total={total} pageSize={pageSize} onChange={setPage} />
        <span className="text-body-sm text-muted whitespace-nowrap">{total.toLocaleString()} <Plural value={total} one="error" other="errors" /></span>
      </footer>

      {activeError && <ErrorLogDetail error={activeError} onClose={closeDetail} />}
    </div>
  );
}
