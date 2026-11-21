import { InjectionToken } from '@angular/core';

/** Full-page navigation. A token so tests can observe it, because jsdom cannot navigate. */
export const BROWSER_NAVIGATE = new InjectionToken<(url: string) => void>('BROWSER_NAVIGATE', {
  providedIn: 'root',
  factory: () => (url: string) => window.location.assign(url),
});
