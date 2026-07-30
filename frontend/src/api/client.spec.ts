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

/** An error Response stand-in. `text` is the only body reader the client uses (see below). */
const errorRes = (status: number, body = '{}') =>
  ({ ok: false, status, statusText: 'err', text: async () => body }) as unknown as Response;

describe('api request — silentStatuses', () => {
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
 * The body of a failed response is read ONCE, as text, and parsed afterwards. Reading it as JSON
 * first and falling back to text cannot work — `json()` consumes the stream even when it throws, so
 * the fallback rejects with "body already read". Every non-JSON error message was lost that way,
 * and an explanatory 409 (MVC writes `Conflict("…")` as bare text/plain) reached the user as an
 * unactionable "409 err".
 */
describe('api request — error messages', () => {
  it('uses the message from our own JSON error envelope', async () => {
    fetchMock.mockResolvedValue(errorRes(400, JSON.stringify({ error: { message: 'Bad thresholds.' } })));
    await expect(api.post('/x', {})).rejects.toThrow('Bad thresholds.');
  });

  it('keeps a plain-text explanation instead of dropping it', async () => {
    fetchMock.mockResolvedValue(errorRes(409, 'This project already has a budget.'));
    await expect(api.post('/x', {})).rejects.toThrow('This project already has a budget.');
  });

  it('keeps a bare JSON string explanation', async () => {
    fetchMock.mockResolvedValue(errorRes(409, JSON.stringify('This agent already has a budget.')));
    await expect(api.post('/x', {})).rejects.toThrow('This agent already has a budget.');
  });

  it('falls back to the status when the body is empty', async () => {
    fetchMock.mockResolvedValue(errorRes(500, ''));
    await expect(api.get('/x')).rejects.toThrow('500 err');
  });

  it('keeps a string error field, not the JSON wrapper around it', async () => {
    fetchMock.mockResolvedValue(errorRes(400, JSON.stringify({ error: 'Unsupported provider.' })));
    await expect(api.post('/x', {})).rejects.toThrow('Unsupported provider.');
  });

  it('falls back to the status when the body is only whitespace', async () => {
    fetchMock.mockResolvedValue(errorRes(500, '\n  \n'));
    await expect(api.get('/x')).rejects.toThrow('500 err');
    const [message] = showToast.mock.calls[0] as [string];
    expect(message).toBe('500 err');
  });

  it('keeps the status for an HTML body and does not paste a whole error page into the toast', async () => {
    fetchMock.mockResolvedValue(errorRes(502, `<html><body>${'x'.repeat(500)}</body></html>`));
    await expect(api.get('/x')).rejects.toThrow(/^502 err: <html>/);
    const [message] = showToast.mock.calls[0] as [string];
    expect(message.length).toBeLessThan(260);
  });

  it('caps a plain-text body too, not only markup', async () => {
    fetchMock.mockResolvedValue(errorRes(500, 'x'.repeat(5000)));
    await expect(api.get('/x')).rejects.toBeInstanceOf(Error);
    const [message] = showToast.mock.calls[0] as [string];
    expect(message.length).toBeLessThan(260);
  });

  /**
   * ASP.NET answers every bodiless `NotFound()`/`Conflict()` with a ProblemDetails document. Dumping
   * that JSON into a red toast that never auto-dismisses is strictly worse than the status line, so
   * an unrecognized JSON shape keeps the status — and a recognized one is reduced to its sentence.
   */
  it('never renders raw ProblemDetails JSON', async () => {
    const problem = { type: 'https://tools.ietf.org/html/rfc9110#section-15.5.5', title: 'Not Found', status: 404 };
    fetchMock.mockResolvedValue(errorRes(404, JSON.stringify(problem)));
    await expect(api.get('/x')).rejects.toThrow('Not Found');
    const [message] = showToast.mock.calls[0] as [string];
    expect(message).not.toContain('{');
  });

  it('prefers a ProblemDetails detail over its generic title', async () => {
    fetchMock.mockResolvedValue(
      errorRes(409, JSON.stringify({ title: 'Conflict', detail: 'The budget is already gone.' })),
    );
    await expect(api.get('/x')).rejects.toThrow('The budget is already gone.');
  });

  it('surfaces the field messages of a validation problem, not "One or more validation errors occurred."', async () => {
    const problem = {
      title: 'One or more validation errors occurred.',
      status: 400,
      errors: { softLimitEur: ['The field softLimitEur must be between 0 and 1000000.'] },
    };
    fetchMock.mockResolvedValue(errorRes(400, JSON.stringify(problem)));
    await expect(api.post('/x', {})).rejects.toThrow(/must be between 0 and 1000000/);
  });

  it('keeps the status for JSON in a shape it does not recognize', async () => {
    fetchMock.mockResolvedValue(errorRes(500, JSON.stringify({ foo: 1, bar: [2, 3] })));
    await expect(api.get('/x')).rejects.toThrow('500 err');
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
