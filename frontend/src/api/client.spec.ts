import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';

const { getAccessToken, notifyUnauthorized, showToast } = vi.hoisted(() => ({
  getAccessToken: vi.fn(() => undefined as string | undefined),
  notifyUnauthorized: vi.fn(),
  showToast: vi.fn(),
}));
vi.mock('../auth/token', () => ({ getAccessToken, notifyUnauthorized }));
vi.mock('../components/ui/Toast', () => ({ showToast }));

import { api, isWriteBlocked, readOnlyMessage, ReadOnlyModeError, setApiReadOnly } from './client';
import { i18n } from '../i18n';

/** A minimal ok JSON Response stand-in. */
const okJson = (body: unknown = { ok: true }) =>
  ({ ok: true, status: 200, json: async () => body }) as unknown as Response;

const fetchMock = vi.fn();

beforeEach(() => {
  vi.clearAllMocks();
  fetchMock.mockResolvedValue(okJson());
  vi.stubGlobal('fetch', fetchMock);
  // The error path reads window.location.href; stub it so the spec runs regardless of test env.
  vi.stubGlobal('window', { location: { href: 'http://test/' } });
});

afterEach(() => vi.unstubAllGlobals());

/** The RequestInit fetch was called with on the last invocation. */
const lastInit = (): RequestInit => fetchMock.mock.calls.at(-1)?.[1] as RequestInit;

describe('api request — opts.signal forwarding', () => {
  it('forwards opts.signal to fetch on every verb', async () => {
    const signal = new AbortController().signal;
    await api.get('/x', { signal });
    expect(lastInit().signal).toBe(signal);

    await api.post('/x', { a: 1 }, { signal });
    expect(lastInit().signal).toBe(signal);

    await api.put('/x', { a: 1 }, { signal });
    expect(lastInit().signal).toBe(signal);

    await api.patch('/x', { a: 1 }, { signal });
    expect(lastInit().signal).toBe(signal);

    await api.del('/x', { signal });
    expect(lastInit().signal).toBe(signal);
  });

  it('leaves signal undefined when no opts are passed', async () => {
    await api.get('/x');
    expect(lastInit().signal).toBeUndefined();
  });

  it('serializes the body and sets the method per verb', async () => {
    await api.post('/x', { a: 1 });
    expect(lastInit().method).toBe('POST');
    expect(lastInit().body).toBe(JSON.stringify({ a: 1 }));

    await api.del('/x');
    expect(lastInit().method).toBe('DELETE');
  });
});

describe('api request — silentStatuses', () => {
  const errorRes = (status: number) =>
    ({ ok: false, status, statusText: 'err', json: async () => ({}) }) as unknown as Response;

  it('still rejects on a silenced status but does NOT raise the error toast', async () => {
    fetchMock.mockResolvedValue(errorRes(404));
    await expect(api.get('/missing', { silentStatuses: [404] })).rejects.toMatchObject({ status: 404 });
    expect(showToast).not.toHaveBeenCalled();
  });

  it('raises the error toast for a non-silenced status', async () => {
    fetchMock.mockResolvedValue(errorRes(500));
    await expect(api.get('/boom')).rejects.toBeInstanceOf(Error);
    expect(showToast).toHaveBeenCalledOnce();
  });
});

/**
 * The kiosk demo's read-only guard. It used to be `body.kiosk [data-write]` in `index.css` alone,
 * and `pointer-events: none` suppresses *pointer* hit-testing only — a tagged control stayed in the
 * tab order and `Enter` still dispatched its `onClick`, so the write reached the API and returned
 * 403 as a red toast. Anything not driven by a click (an effect, a timer, a retry) bypassed it
 * outright. These pin that no mutating request now leaves the browser, however it was triggered.
 */
describe('api request — read-only mode', () => {
  beforeEach(() => i18n.loadAndActivate({ locale: 'en', messages: {} }));
  afterEach(() => setApiReadOnly(false));

  it('is off by default, so a normal install is unaffected', async () => {
    await api.post('/api/agents', { name: 'a' });
    expect(fetchMock).toHaveBeenCalledOnce();
  });

  it('refuses every mutating verb without issuing a request', async () => {
    setApiReadOnly(true);

    await expect(api.post('/api/agents', {})).rejects.toBeInstanceOf(ReadOnlyModeError);
    await expect(api.put('/api/agents/1', {})).rejects.toBeInstanceOf(ReadOnlyModeError);
    await expect(api.patch('/api/notifications/1/read')).rejects.toBeInstanceOf(ReadOnlyModeError);
    await expect(api.del('/api/agents/1')).rejects.toBeInstanceOf(ReadOnlyModeError);

    // Nothing reached the network — the 403 round-trip is what produced the red toast.
    expect(fetchMock).not.toHaveBeenCalled();
    expect(showToast).not.toHaveBeenCalled();
  });

  it('still allows reads, so the demo keeps working', async () => {
    setApiReadOnly(true);

    await expect(api.get('/api/agents')).resolves.toEqual({ ok: true });
    expect(fetchMock).toHaveBeenCalledOnce();
  });

  it('rejects with the localized read-only message', async () => {
    setApiReadOnly(true);

    await expect(api.post('/api/agents', {})).rejects.toThrow(readOnlyMessage());
    expect(readOnlyMessage()).toContain('read-only demo');
  });

  it('classifies methods case-insensitively, treating an absent method as GET', () => {
    setApiReadOnly(true);
    expect(isWriteBlocked(undefined)).toBe(false);
    expect(isWriteBlocked('get')).toBe(false);
    expect(isWriteBlocked('HEAD')).toBe(false);
    expect(isWriteBlocked('OPTIONS')).toBe(false);
    expect(isWriteBlocked('post')).toBe(true);
    expect(isWriteBlocked('DELETE')).toBe(true);

    setApiReadOnly(false);
    expect(isWriteBlocked('POST')).toBe(false);
  });
});
