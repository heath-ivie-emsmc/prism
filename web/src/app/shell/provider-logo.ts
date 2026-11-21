/** A customer logo shipped with the app, and the plate it sits on: a logo drawn in white needs a dark plate to be seen at all. */
export interface ProviderLogo {
  readonly url: string;
  readonly plate: 'light' | 'dark';
}

/**
 * PLACEHOLDER until Provider settings serve a logo: customer logos shipped with the app, found by the Provider's display name.
 * Display only, nothing is authorized from it. Replace with a `logoUrl` on the Provider from the API (and its own alt text).
 */
const LOGOS: Readonly<Record<string, ProviderLogo>> = {
  'seminole county fire department': { url: '/providers/seminole-county-fire-department.png', plate: 'light' },
  'mercy flights incorporated or': { url: '/providers/mercy-flights-incorporated-or.png', plate: 'dark' },
};

/** The logo for this Provider, or null so the caller shows the name instead. */
export function providerLogo(providerName: string | undefined): ProviderLogo | null {
  return LOGOS[(providerName ?? '').trim().toLowerCase()] ?? null;
}
