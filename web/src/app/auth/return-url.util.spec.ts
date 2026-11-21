import {
  LOGIN_REASON_AUTH,
  buildLoginQueryParams,
  isSafeInternalReturnUrl,
  normalizeReturnUrl,
} from './return-url.util';

/** Builds a string containing a character that is awkward to type in source, such as a tab or a zero-width space. */
const withChar = (code: number, before = '/', after = '/evil.com'): string => before + String.fromCharCode(code) + after;

describe('return-url.util', () => {
  it('allows safe internal paths with query and fragment', () => {
    expect(isSafeInternalReturnUrl('/dashboard')).toBe(true);
    expect(isSafeInternalReturnUrl('/dashboard?period=2026-10')).toBe(true);
    expect(isSafeInternalReturnUrl('/help#metrics')).toBe(true);
    expect(normalizeReturnUrl('/dashboard')).toBe('/dashboard');
  });

  it('rejects open-redirect style values', () => {
    for (const bad of [
      '',
      'https://evil.com',
      '//evil.com',
      '/\\evil',
      '/\\\\evil',
      'javascript:alert(1)',
      '/login',
      '/login?returnUrl=/dashboard',
      '/login/sso',
      '@evil',
      '/@evil.com',
      'relative/path',
    ]) {
      expect(isSafeInternalReturnUrl(bad), bad).toBe(false);
      expect(normalizeReturnUrl(bad), bad).toBeNull();
    }
  });

  it('rejects tab, CR, LF and other control or invisible characters, which browsers strip from URLs', () => {
    const codes = [0x09, 0x0a, 0x0d, 0x00, 0x1f, 0x7f, 0x85, 0x200b, 0x2028, 0x20, 0xfeff];
    for (const code of codes) {
      const candidate = withChar(code);
      expect(isSafeInternalReturnUrl(candidate), `U+${code.toString(16)}`).toBe(false);
      expect(normalizeReturnUrl(candidate), `U+${code.toString(16)}`).toBeNull();
    }
    expect(normalizeReturnUrl(withChar(0x09, '', '/dashboard'))).toBeNull();
    expect(normalizeReturnUrl(withChar(0x0a, '/dashboard', ''))).toBeNull();
  });

  it('returns null for null, undefined and blank candidates', () => {
    expect(normalizeReturnUrl(null)).toBeNull();
    expect(normalizeReturnUrl(undefined)).toBeNull();
    expect(normalizeReturnUrl('   ')).toBeNull();
  });

  it('allows a path that merely starts with the text "/login"', () => {
    expect(isSafeInternalReturnUrl('/loginx')).toBe(true);
  });

  it('buildLoginQueryParams adds the auth reason only for safe targets', () => {
    expect(buildLoginQueryParams('/dashboard')).toEqual({ returnUrl: '/dashboard', reason: LOGIN_REASON_AUTH });
    expect(buildLoginQueryParams('/login')).toBeNull();
    expect(buildLoginQueryParams(withChar(0x09))).toBeNull();
  });
});
