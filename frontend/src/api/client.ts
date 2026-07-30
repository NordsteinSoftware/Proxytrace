import { msg } from '@lingui/core/macro';
import { getAccessToken, notifyUnauthorized } from '../auth/token';
import { showToast } from '../components/ui/Toast';
import { i18n } from '../i18n';

type ErrorMeta = { status: number; stacktrace?: string; type?: string };

/** The envelope the API wraps handled failures in. Every field is optional — a proxy or a raw MVC
 *  result may answer with something else entirely, which is what {@link parseErrorBody} models. */
interface ApiErrorEnvelope {
  error?: { message?: string; stacktrace?: string; type?: string; errorId?: string };
}

/**
 * What an error body turned out to be.
 *
 * The `json` / `text` split is load-bearing: only a body that is *not* JSON is prose the server
 * wrote for a human. Collapsing both into "unrecognized" would dump the raw JSON of every
 * `ProblemDetails` — which ASP.NET emits for every bodiless `NotFound()`/`Conflict()` and every
 * model-binding 400 — into a red toast.
 */
type ParsedError =
  /** Our own `{ error: { … } }` envelope. */
  | { kind: 'envelope'; error: ApiErrorEnvelope['error'] }
  /** A human-readable sentence extracted from a recognized JSON shape. */
  | { kind: 'message'; message: string }
  /** Valid JSON in a shape we don't know — never worth rendering verbatim. */
  | { kind: 'json' }
  /** Not JSON at all: the body is whatever the server (or a proxy) wrote as prose or markup. */
  | { kind: 'text' };

/** The longest raw body we'll paste into a toast. Error toasts never auto-dismiss, so an
 *  unbounded message can push its own close button off-screen. */
const MAX_RAW_MESSAGE_LEN = 200;

/** Caps a server-supplied message at a length a toast can actually show. */
function clip(text: string): string {
  return text.length <= MAX_RAW_MESSAGE_LEN ? text : `${text.slice(0, MAX_RAW_MESSAGE_LEN)}…`;
}

/** Reads our envelope's fields off an arbitrary parsed object, keeping only the strings. */
function pickEnvelope(error: object): NonNullable<ApiErrorEnvelope['error']> {
  const { message, stacktrace, type, errorId } = error as Record<string, unknown>;
  const str = (v: unknown) => (typeof v === 'string' ? v : undefined);
  return { message: str(message), stacktrace: str(stacktrace), type: str(type), errorId: str(errorId) };
}

/** RFC 7807 `ProblemDetails` — what ASP.NET answers with when a controller returns a bodiless
 *  status result, and (with `errors`) for model-binding failures. */
interface ProblemDetails {
  title?: unknown;
  detail?: unknown;
  errors?: unknown;
}

/** The best sentence a `ProblemDetails` has to offer, or null if it is not one. */
function problemDetailsMessage(parsed: object): string | null {
  const { title, detail, errors } = parsed as ProblemDetails;
  // ValidationProblemDetails: the per-field messages say far more than "One or more validation
  // errors occurred." ever will.
  if (typeof errors === 'object' && errors !== null) {
    const messages = Object.values(errors as Record<string, unknown>)
      .flatMap(v => (Array.isArray(v) ? v : [v]))
      .filter((v): v is string => typeof v === 'string' && v.trim() !== '');
    if (messages.length > 0) return messages.join(' ');
  }
  if (typeof detail === 'string' && detail.trim() !== '') return detail;
  if (typeof title === 'string' && title.trim() !== '') return title;
  return null;
}

/**
 * Classifies an error response body that has already been read as text.
 *
 * Recognizes our own envelope, a bare JSON string (`BadRequest("…")` under a JSON formatter), a
 * string `error` field, and `ProblemDetails`. Anything else stays `json` (keep the status line) or
 * `text` (the body is the server's own words).
 */
function parseErrorBody(raw: string): ParsedError {
  try {
    const parsed: unknown = JSON.parse(raw);
    if (typeof parsed === 'string') {
      return parsed.trim() === '' ? { kind: 'json' } : { kind: 'message', message: parsed };
    }
    if (typeof parsed !== 'object' || parsed === null) return { kind: 'json' };
    if ('error' in parsed) {
      const { error } = parsed as { error: unknown };
      if (typeof error === 'object' && error !== null) {
        return { kind: 'envelope', error: pickEnvelope(error) };
      }
      if (typeof error === 'string' && error.trim() !== '') return { kind: 'message', message: error };
    }
    const problem = problemDetailsMessage(parsed);
    return problem === null ? { kind: 'json' } : { kind: 'message', message: problem };
  } catch {
    return { kind: 'text' };
  }
}

/* ── Read-only mode (the kiosk demo) ─────────────────────────────────────────────────────────── */

/** HTTP methods that never mutate server state — mirrors `KioskReadOnlyMiddleware`. */
const READ_METHODS = new Set(['GET', 'HEAD', 'OPTIONS']);

const READ_ONLY_MESSAGE = msg`This is a read-only demo — changes aren't saved.`;

let readOnly = false;

/**
 * Blocks every mutating request at the transport.
 *
 * The kiosk's read-only mode used to be gated only by CSS (`body.kiosk [data-write]` in
 * `index.css`), and `pointer-events: none` suppresses *pointer* hit-testing alone: tagged controls
 * stayed in the tab order and `Enter`/`Space` still dispatched their `onClick`. Anything that
 * wasn't a click — a keyboard activation, an effect, a timer, a retry — walked straight past it,
 * reached the API, and came back 403 as a red error toast on a surface whose whole job is to look
 * polished. Enforcing here covers every path uniformly, so no call site has to remember.
 *
 * The backend (`KioskReadOnlyMiddleware`) remains the actual authority; this only keeps the demo
 * from asking it questions it will refuse.
 */
export function setApiReadOnly(value: boolean) {
  readOnly = value;
}

/** True when `method` would mutate and the client is in read-only mode. */
export function isWriteBlocked(method: string | undefined): boolean {
  return readOnly && !READ_METHODS.has((method ?? 'GET').toUpperCase());
}

/** The localized "read-only demo" copy, for callers that surface it themselves. */
export function readOnlyMessage(): string {
  return i18n._(READ_ONLY_MESSAGE);
}

/**
 * Thrown instead of issuing a mutating request in read-only mode. Distinct type so callers (and
 * `app/queryClient.ts`) can tell "the demo declined this" from a genuine API failure.
 */
export class ReadOnlyModeError extends Error {
  readonly status = 403;

  constructor(message: string) {
    super(message);
    this.name = 'ReadOnlyModeError';
  }
}

/** Error types the backend tags on a 402 Payment Required response. */
export type UpgradeErrorType = 'FeatureNotLicensed' | 'LicenseLimitExceeded';

/**
 * Thrown when the API rejects a request because the current license tier does
 * not permit the feature or has exceeded a usage limit (HTTP 402). Callers can
 * branch on this (e.g. show an upgrade placeholder) instead of the generic
 * error toast, which is intentionally suppressed for these responses.
 */
export class UpgradeRequiredError extends Error {
  readonly status = 402;
  readonly errorType: UpgradeErrorType;

  constructor(message: string, errorType: UpgradeErrorType) {
    super(message);
    this.name = 'UpgradeRequiredError';
    this.errorType = errorType;
  }
}

function isUpgradeErrorType(type: string | undefined): type is UpgradeErrorType {
  return type === 'FeatureNotLicensed' || type === 'LicenseLimitExceeded';
}

/** Per-request behaviour overrides. */
export interface RequestOptions {
  /**
   * HTTP error statuses the caller treats as an expected outcome rather than a failure.
   * The request still rejects (so callers/queries see the error), but no red error toast
   * fires — used e.g. for a 404 when a run has no result for a given test case.
   */
  silentStatuses?: number[];
  /**
   * Aborts the request. Forwarded to `fetch`, so a cancelled caller (e.g. Tracey's `await_actions`
   * poll when the user hits Stop) tears down the in-flight HTTP request instead of letting it run
   * to completion. An aborted fetch rejects with a DOMException whose `name` is `'AbortError'`.
   */
  signal?: AbortSignal;
}

async function request<T>(url: string, init?: RequestInit, opts?: RequestOptions): Promise<T> {
  // Refuse before the request leaves the browser, so a read-only demo never earns a 403.
  if (isWriteBlocked(init?.method)) throw new ReadOnlyModeError(readOnlyMessage());

  const token = getAccessToken();
  const headers: Record<string, string> = {
    'Content-Type': 'application/json',
    ...(init?.headers as Record<string, string> | undefined),
  };
  if (token) headers['Authorization'] = `Bearer ${token}`;

  const res = await fetch(url, { ...init, headers, signal: opts?.signal ?? init?.signal });
  if (res.status === 401) {
    if (token) notifyUnauthorized();
    throw new Error('401 Unauthorized');
  }
  if (!res.ok) {
    let message = `${res.status} ${res.statusText}`;
    let stacktrace: string | undefined;
    let type: string | undefined;
    let errorId: string | undefined;

    // Read the body ONCE as text, then try to parse it. Calling `res.json()` first and falling
    // back to `res.text()` cannot work: `json()` consumes the stream even when it throws, so the
    // fallback rejects with "body already read" and the message is lost. That silently reduced
    // every non-JSON error — an MVC `Conflict("This project already has a budget.")` is written as
    // bare text/plain by StringOutputFormatter — to an unhelpful "409 Conflict".
    const raw = (await res.text().catch(() => '')).trim();
    const body = raw === '' ? ({ kind: 'json' } as const) : parseErrorBody(raw);
    if (body.kind === 'envelope') {
      if (body.error?.message) message = body.error.message;
      stacktrace = body.error?.stacktrace;
      type = body.error?.type;
      // Present when the backend captured this error to the Error Log; lets an admin deep-link to it.
      errorId = body.error?.errorId;
    } else if (body.kind === 'message') {
      message = clip(body.message);
    } else if (body.kind === 'text') {
      // Not JSON. A plain sentence is the server's own explanation and reads better alone; markup
      // (an HTML error page from a reverse proxy) is noise, so keep the status in front of it.
      message = raw.startsWith('<') ? `${message}: ${clip(raw)}` : clip(raw);
    }
    // `json` keeps the status line: a body we parsed but did not recognize (a bare `ProblemDetails`
    // with nothing but a `status`, some third party's shape) is machine noise, not an explanation.

    // License gating: a 402 tagged with a licensing error type is not a generic
    // failure — surface it as an UpgradeRequiredError so the UI can route to the
    // upgrade placeholder. Do NOT fire the red error toast for these.
    if (res.status === 402 && isUpgradeErrorType(type)) {
      throw new UpgradeRequiredError(message, type);
    }

    const error = new Error(message) as Error & ErrorMeta;
    error.status = res.status;
    error.stacktrace = stacktrace;
    error.type = type;

    // Expected statuses (e.g. a 404 for a missing fixture) still reject so callers can
    // branch, but must not raise the red error toast.
    if (opts?.silentStatuses?.includes(res.status)) {
      throw error;
    }

    showToast(message, 'error', {
      // Don't surface server stacktraces to users in production builds.
      stacktrace: import.meta.env.DEV ? stacktrace : undefined,
      errorId,
    });

    throw error;
  }
  if (res.status === 204) return undefined as T;
  return res.json();
}

export const api = {
  get: <T>(url: string, opts?: RequestOptions) => request<T>(url, undefined, opts),
  post: <T>(url: string, body?: unknown, opts?: RequestOptions) =>
    request<T>(url, { method: 'POST', body: body != null ? JSON.stringify(body) : undefined }, opts),
  put: <T>(url: string, body?: unknown, opts?: RequestOptions) =>
    request<T>(url, { method: 'PUT', body: body != null ? JSON.stringify(body) : undefined }, opts),
  patch: <T>(url: string, body?: unknown, opts?: RequestOptions) =>
    request<T>(url, { method: 'PATCH', body: body != null ? JSON.stringify(body) : undefined }, opts),
  del: <T = void>(url: string, opts?: RequestOptions) => request<T>(url, { method: 'DELETE' }, opts),
};

export function qs(params: Record<string, unknown>): string {
  const p = new URLSearchParams();
  for (const [k, v] of Object.entries(params)) {
    if (v != null && v !== '') p.set(k, String(v));
  }
  const s = p.toString();
  return s ? '?' + s : '';
}
