/**
 * Helpers for the OpenAI-compatible ingestion proxy URL shown in the UI.
 *
 * The proxy base URL carries the project as its first path segment, and optionally a scope (a
 * use-case group of agents) as the second:
 *   {base}/{project-slug}/openai/v1
 *   {base}/{project-slug}/{scope-key}/openai/v1
 * The slug is derived from the project name and must match the backend's `ToSlug`
 * (Proxytrace.Common.Text.SlugExtensions): lower-cased, non-alphanumeric characters dropped,
 * and runs of whitespace / `-` / `_` collapsed into single hyphens.
 */

/** Derives the URL slug for a project name. Mirrors the backend `ToSlug`. */
export function projectSlug(name: string): string {
  const out: string[] = [];
  let pendingHyphen = false;

  for (const ch of name) {
    if (/\p{L}|\p{N}/u.test(ch)) {
      if (pendingHyphen && out.length > 0) out.push('-');
      pendingHyphen = false;
      out.push(ch.toLowerCase());
    } else if (/\s/.test(ch) || ch === '-' || ch === '_') {
      pendingHyphen = true;
    }
  }

  return out.join('');
}

/** Longest canonical scope key — mirrors the backend `ScopeKey.MaxLength`. */
export const SCOPE_KEY_MAX_LENGTH = 64;

// Path segments of the proxy's own route surface; mirrors the backend `ScopeKey` reserved set.
const RESERVED_SCOPE_KEYS = new Set(['openai', 'v1']);

/**
 * Canonical scope key for a raw scope name, or `''` when nothing usable is left (empty,
 * punctuation-only, or reserved). Mirrors the backend `ScopeKey.Normalize`: the project-slug
 * rules, capped at {@link SCOPE_KEY_MAX_LENGTH} without a trailing hyphen.
 */
export function scopeKey(name: string): string {
  let key = projectSlug(name);
  if (key.length > SCOPE_KEY_MAX_LENGTH) key = key.slice(0, SCOPE_KEY_MAX_LENGTH).replace(/-+$/, '');
  return RESERVED_SCOPE_KEYS.has(key) ? '' : key;
}

/** Request header that names a call's scope per request (overrides the URL segment). */
export const SCOPE_HEADER = 'x-proxytrace-scope';

/** The header line a client sends to put one request into the scope `key`. */
export function scopeHeaderLine(key: string): string {
  return `${SCOPE_HEADER}: ${key}`;
}

/**
 * The proxy host clients point at. The ingestion proxy is a separate service with its own
 * port/hostname, so the page origin can never be assumed correct — precedence is the
 * backend-advertised URL (`/api/config` → `Proxy:PublicBaseUrl`), then the build-time
 * override, then the page origin as a last resort.
 */
export function resolveProxyBase(advertised?: string | null): string {
  const fromConfig = advertised?.trim();
  if (fromConfig) return fromConfig.replace(/\/+$/, '');
  const fromEnv = (import.meta.env.VITE_PROXY_BASE_URL as string | undefined)?.trim();
  if (fromEnv) return fromEnv.replace(/\/+$/, '');
  return window.location.origin;
}

/**
 * Full OpenAI base_url a client should target for the given project — and, when a usable scope
 * name is given, for that scope inside it.
 */
export function ingestionUrl(projectName: string, base: string, scope?: string): string {
  const key = scope ? scopeKey(scope) : '';
  const scopeSegment = key ? `/${encodeURIComponent(key)}` : '';
  return `${base.replace(/\/+$/, '')}/${projectSlug(projectName)}${scopeSegment}/openai/v1`;
}
