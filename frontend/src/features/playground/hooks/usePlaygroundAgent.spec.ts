import { describe, it, expect } from 'vitest';
import { resolveStoredAgent } from './usePlaygroundAgent';

const list = (ids: string[], total = ids.length) => ({ ids, total });

describe('resolveStoredAgent', () => {
  it('waits while nothing is selected', () => {
    expect(resolveStoredAgent(null, list(['a']))).toBe('wait');
  });

  it('waits until the agent list has loaded, rather than guessing', () => {
    expect(resolveStoredAgent('a', undefined)).toBe('wait');
  });

  it('fetches an agent the project actually has', () => {
    expect(resolveStoredAgent('a', list(['a', 'b']))).toBe('fetch');
  });

  it('clears a stored id the project no longer has', () => {
    // The kiosk re-seeds into in-memory storage on every restart, so the id persisted by a
    // previous boot is dead. Fetching it would only earn a 404.
    expect(resolveStoredAgent('c5fe0321', list(['a', 'b']))).toBe('clear');
  });

  it('clears when the project has no agents at all', () => {
    expect(resolveStoredAgent('a', list([]))).toBe('clear');
  });

  it('fetches an unlisted id when the list is truncated — absence proves nothing', () => {
    // Only a complete list can prove an id is gone; past the page size, fall back to asking the
    // server (and tolerating its answer) instead of dropping a valid selection.
    expect(resolveStoredAgent('z', list(['a', 'b'], 500))).toBe('fetch');
  });
});
