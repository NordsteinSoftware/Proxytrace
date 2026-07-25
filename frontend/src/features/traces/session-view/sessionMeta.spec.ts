import { describe, it, expect } from 'vitest';
import { isSessionLive, LIVE_WINDOW_MS } from './sessionMeta';

describe('isSessionLive', () => {
  const now = Date.parse('2026-07-17T12:00:00.000Z');

  it('is live when the last activity is inside the window', () => {
    const recent = new Date(now - (LIVE_WINDOW_MS - 1_000)).toISOString();
    expect(isSessionLive(recent, now)).toBe(true);
  });

  it('is live right now (zero elapsed)', () => {
    expect(isSessionLive(new Date(now).toISOString(), now)).toBe(true);
  });

  it('is not live exactly at the window boundary', () => {
    const boundary = new Date(now - LIVE_WINDOW_MS).toISOString();
    expect(isSessionLive(boundary, now)).toBe(false);
  });

  it('is not live when the last activity is older than the window', () => {
    const old = new Date(now - (LIVE_WINDOW_MS + 1_000)).toISOString();
    expect(isSessionLive(old, now)).toBe(false);
  });

  it('is not live for a missing or unparseable timestamp', () => {
    expect(isSessionLive(null, now)).toBe(false);
    expect(isSessionLive(undefined, now)).toBe(false);
    expect(isSessionLive('not-a-date', now)).toBe(false);
  });
});
