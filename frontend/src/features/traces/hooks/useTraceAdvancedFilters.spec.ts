import { describe, it, expect } from 'vitest';
import { advancedFiltersKey, parseStoredAdvancedFilters } from './useTraceAdvancedFilters';
import { EMPTY_ADVANCED_FILTERS } from '../tracesMeta';

describe('advancedFiltersKey', () => {
  it('namespaces the storage key per project', () => {
    expect(advancedFiltersKey('proj-1')).toBe('traces.filters.proj-1');
    expect(advancedFiltersKey('proj-2')).not.toBe(advancedFiltersKey('proj-1'));
  });
});

describe('parseStoredAdvancedFilters', () => {
  it('defaults to all-empty with nothing stored', () => {
    expect(parseStoredAdvancedFilters(null, null, null)).toEqual(EMPTY_ADVANCED_FILTERS);
  });

  it('round-trips a valid stored value', () => {
    const stored = { ...EMPTY_ADVANCED_FILTERS, tool: 'web_search', statusClass: '5' as const };
    expect(parseStoredAdvancedFilters(JSON.stringify(stored), null, null)).toEqual(stored);
  });

  it('falls back to defaults for corrupt or invalid JSON (user-editable storage)', () => {
    expect(parseStoredAdvancedFilters('{not json', null, null)).toEqual(EMPTY_ADVANCED_FILTERS);
    expect(parseStoredAdvancedFilters(JSON.stringify({ anomaly: 'bogus' }), null, null)).toEqual(EMPTY_ADVANCED_FILTERS);
  });

  it('keeps a value stored before a slot existed, filling the new slot with its default', () => {
    // A filter bar saved before scopes existed has no `scope` key; it must survive, not reset.
    const preScope: Partial<typeof EMPTY_ADVANCED_FILTERS> = { ...EMPTY_ADVANCED_FILTERS, agent: 'agent-1', tool: 'web_search' };
    delete preScope.scope;
    expect(parseStoredAdvancedFilters(JSON.stringify(preScope), null, null)).toEqual({
      ...EMPTY_ADVANCED_FILTERS,
      agent: 'agent-1',
      tool: 'web_search',
    });
  });

  it('seeds from the legacy agent-filter and outliers-only keys when no new value exists', () => {
    expect(parseStoredAdvancedFilters(null, 'agent-1', 'true')).toEqual({
      ...EMPTY_ADVANCED_FILTERS,
      agent: 'agent-1',
      anomaly: 'any',
    });
    expect(parseStoredAdvancedFilters(null, null, 'false')).toEqual(EMPTY_ADVANCED_FILTERS);
  });

  it('ignores the legacy keys once a new-format value exists', () => {
    const stored = { ...EMPTY_ADVANCED_FILTERS, agent: 'agent-2' };
    expect(parseStoredAdvancedFilters(JSON.stringify(stored), 'agent-1', 'true')).toEqual(stored);
  });
});
