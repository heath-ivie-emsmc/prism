/** Query key: internal SPA path to restore after login. */
export const LOGIN_QUERY_RETURN_URL = 'returnUrl';

/** Query key: why the user reached `/login`. */
export const LOGIN_QUERY_REASON = 'reason';

/** Value for {@link LOGIN_QUERY_REASON} when the route guard or a 401 sent the user to sign in. */
export const LOGIN_REASON_AUTH = 'auth';

/** Query key: set by the API when the SSO callback failed. The value is a short code, never shown raw. */
export const LOGIN_QUERY_ERROR = 'error';

const LOGIN_PATH = '/login';

function hasSchemePrefix(value: string): boolean {
  const colon = value.indexOf(':');
  if (colon <= 0) return false;
  for (let i = 0; i < colon; i++) {
    if (!/[a-zA-Z0-9+\-.]/.test(value[i])) return false;
  }
  return true;
}

/**
 * ASCII controls (tab, CR, LF, below 0x20, DEL), C1 controls, any whitespace and Unicode format characters
 * (zero-width, bidi marks). Browsers strip tab, CR and LF from URLs, so `/<TAB>/evil.com` would otherwise read as
 * `//evil.com`. Keep in step with the post-login redirect validator on the server.
 */
// eslint-disable-next-line no-control-regex
const BROWSER_NORMALIZED_CHARACTER = /[\u0000-\u001f\u007f-\u009f\s\u00ad\u200b-\u200f\u2028-\u202f\u2060-\u206f\ufeff]/u;

const PROBE_ORIGIN = 'https://prism-origin.invalid';

function ownOrigin(): string {
  const origin = typeof window !== 'undefined' ? window.location?.origin : undefined;
  return origin && origin !== 'null' ? origin : PROBE_ORIGIN;
}

/** True when `candidate` is a safe root-relative SPA path (query and fragment allowed). */
export function isSafeInternalReturnUrl(candidate: string): boolean {
  if (candidate?.[0] !== '/') return false;
  if (BROWSER_NORMALIZED_CHARACTER.test(candidate)) return false;
  if (candidate.startsWith('//')) return false;
  if (candidate.includes('\\')) return false;
  if (candidate.includes('@')) return false;
  if (hasSchemePrefix(candidate)) return false;

  // Resolve the way a browser would against this page's own origin; anything that lands elsewhere is refused.
  const base = ownOrigin();
  let resolved: URL;
  try {
    resolved = new URL(candidate, base);
  } catch {
    return false;
  }
  if (resolved.origin !== new URL(base).origin) return false;

  const pathOnly = candidate.split(/[?#]/)[0];
  return pathOnly !== LOGIN_PATH && !pathOnly.startsWith(`${LOGIN_PATH}/`);
}

/** Returns a validated internal return path, or null when it is empty or unsafe. */
export function normalizeReturnUrl(candidate: string | null | undefined): string | null {
  if (candidate == null) return null;
  // Only plain spaces are trimmed; any control character, even at the ends, rejects the value.
  const trimmed = candidate.replace(/^ +| +$/g, '');
  if (!trimmed || !isSafeInternalReturnUrl(trimmed)) return null;
  return trimmed;
}

/** Login query params when redirecting from a protected route; null when there is no safe target. */
export function buildLoginQueryParams(
  attemptedUrl: string,
): { returnUrl: string; reason: typeof LOGIN_REASON_AUTH } | null {
  const safe = normalizeReturnUrl(attemptedUrl);
  return safe ? { returnUrl: safe, reason: LOGIN_REASON_AUTH } : null;
}
