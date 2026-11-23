import { DestroyRef, Injectable, inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NavigationEnd, NavigationStart, Router } from '@angular/router';

/** Pages remembered at most, so the memory cannot grow without bound over a long session. */
const MAX_REMEMBERED = 50;
/** How long to wait for a page that loads its figures after it opens to become tall enough to scroll back to where it was. */
const MAX_WAIT_MS = 3000;
const INPUT_EVENTS = ['wheel', 'touchstart', 'keydown', 'mousedown'] as const;

/**
 * Opening a page starts at the top; Back returns to where the page was scrolled. The router's own restoration scrolls as soon as the page is drawn,
 * but these pages load their figures afterwards and are still short then, so the browser clamps the position. This waits until the page is tall
 * enough (or a short time passes, or the reader scrolls first) and then goes back to the remembered position.
 */
@Injectable({ providedIn: 'root' })
export class ScrollRestoreService {
  private readonly router = inject(Router);
  private readonly positions = new Map<string, number>();
  private lastUrl: string | null = null;
  private pendingPopstate = false;
  private stopWaiting: (() => void) | null = null;

  constructor() {
    // The browser's own restoration would scroll before the figures are in, and fight this one.
    history.scrollRestoration = 'manual';
    this.router.events.pipe(takeUntilDestroyed(inject(DestroyRef))).subscribe((event) => {
      if (event instanceof NavigationStart) {
        this.stopWaiting?.();
        if (this.lastUrl !== null) this.remember(this.lastUrl, window.scrollY);
        this.pendingPopstate = event.navigationTrigger === 'popstate';
      } else if (event instanceof NavigationEnd) {
        this.lastUrl = event.urlAfterRedirects;
        const saved = this.pendingPopstate ? this.positions.get(event.urlAfterRedirects) : undefined;
        this.pendingPopstate = false;
        if (saved === undefined) {
          // A new page, unless the address points at a part of it.
          if (!new URL(event.urlAfterRedirects, location.origin).hash) window.scrollTo({ top: 0, behavior: 'instant' });
        } else {
          this.restore(saved);
        }
      }
    });
  }

  private remember(url: string, top: number): void {
    this.positions.delete(url);
    this.positions.set(url, top);
    if (this.positions.size > MAX_REMEMBERED) this.positions.delete(this.positions.keys().next().value!);
  }

  private restore(top: number): void {
    const reachable = (): boolean => document.documentElement.scrollHeight - window.innerHeight >= top;
    const go = (): void => window.scrollTo({ top, behavior: 'instant' });
    if (reachable()) {
      go();
      return;
    }
    const observer = new ResizeObserver(() => {
      if (!reachable()) return;
      stop();
      go();
    });
    const timer = setTimeout(() => {
      stop();
      go();
    }, MAX_WAIT_MS);
    const stop = (): void => {
      observer.disconnect();
      clearTimeout(timer);
      for (const name of INPUT_EVENTS) window.removeEventListener(name, stop);
      this.stopWaiting = null;
    };
    // A reader who starts scrolling first is not pulled back.
    for (const name of INPUT_EVENTS) window.addEventListener(name, stop, { passive: true });
    observer.observe(document.body);
    this.stopWaiting = stop;
  }
}
