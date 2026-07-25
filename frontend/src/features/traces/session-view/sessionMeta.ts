// Pure derive helpers for the session detail view. No JSX, no I/O — unit-tested in
// sessionMeta.spec.ts.

/** A session is "live" when its most recent trace arrived within this window. */
export const LIVE_WINDOW_MS = 5 * 60_000;

/** Session-timeline page size — sessions are bounded, so a generous single page is fine. */
export const SESSION_TRACES_PAGE_SIZE = 100;

/**
 * Whether the session is currently live: its last activity falls within {@link LIVE_WINDOW_MS} of
 * `nowMs`. A missing/unparseable timestamp reads as not-live rather than throwing.
 */
export function isSessionLive(lastActivityAtIso: string | null | undefined, nowMs: number): boolean {
  if (!lastActivityAtIso) return false;
  const last = Date.parse(lastActivityAtIso);
  if (Number.isNaN(last)) return false;
  return nowMs - last < LIVE_WINDOW_MS;
}
