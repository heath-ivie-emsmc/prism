/** What the frame needs from the browser's Fullscreen API. Declared narrowly so a test can pass a plain object. */
export interface FullscreenDocument {
  readonly fullscreenEnabled?: boolean;
  readonly fullscreenElement?: Element | null;
}

export interface FullscreenTarget {
  readonly requestFullscreen?: unknown;
}

/**
 * True when this browser can put this element in full screen. iPhone Safari cannot (only video), so there the expand button is not drawn
 * at all rather than drawn and dead.
 */
export function fullscreenSupported(doc: FullscreenDocument, target: FullscreenTarget): boolean {
  return doc.fullscreenEnabled === true && typeof target.requestFullscreen === 'function';
}

/** The accessible name of the button: it names the widget so several on one page are told apart, and swaps when expanded. */
export function expandButtonName(heading: string, expanded: boolean): string {
  return expanded ? 'Exit full screen' : `Expand ${heading} to full screen`;
}

export function enteredAnnouncement(heading: string): string {
  return `${heading} full screen. Press Escape to exit.`;
}

export const EXITED_ANNOUNCEMENT = 'Exited full screen.';
export const UNAVAILABLE_ANNOUNCEMENT = "Full screen isn't available here.";

/** What a full screen frame says it covers, because the page filters that set it are out of sight: "October 2026 · Air". Empty parts are left out. */
export function frameContext(periodLabel: string, serviceLineName: string): string {
  return [periodLabel, serviceLineName].filter((part) => part !== '').join(' · ');
}
