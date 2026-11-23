import { HttpClient, HttpContext, HttpContextToken, HttpEvent, HttpHandlerFn, HttpRequest, HttpResponse } from '@angular/common/http';
import { DestroyRef, Injectable, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router } from '@angular/router';
import { Observable, filter, tap } from 'rxjs';
import { AuthService } from '../auth/auth.service';
import { NetCollectionsPeriodsDto } from '../metrics/net-collections/net-collections.dto';
import { metricsBase } from '../metrics/shared/metric-http';
import { isIsoDate } from './data-through';

/** Marks the cheap check for newer data, so its answer is not mistaken for figures on screen. */
export const REVALIDATING = new HttpContextToken<boolean>(() => false);

/** The check runs at most this often, however much the tab is focused or the user navigates. */
export const MIN_REVALIDATE_MS = 15_000;

const METRIC_URL = /^\/api\/v1\/providers\/([^/]+)\/metrics\//;

/**
 * Tracks the Data Through date each Provider's figures on screen were loaded with, and notices when the warehouse has a newer one.
 * There is no polling: the check runs when the tab regains focus and after a navigation. A newer date only raises `updateAvailable`;
 * figures change only when the user applies the update, which bumps `revision`. Resources that show Metrics put `revision()` in their params.
 */
@Injectable({ providedIn: 'root' })
export class DataFreshnessService {
  private readonly http = inject(HttpClient);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly shownByProvider = signal<Readonly<Record<string, string>>>({});
  private readonly latestByProvider = signal<Readonly<Record<string, string>>>({});
  private inFlight = false;
  private lastCheck = Number.NEGATIVE_INFINITY;

  /** Bumped when the user applies an update: the cue for every visible Metric to load again. */
  readonly revision = signal(0);

  private readonly providerId = computed(() => this.auth.activeProviderId());
  /** The Data Through date of the figures on screen for the active Provider, yyyy-MM-dd. */
  readonly shown = computed(() => this.shownByProvider()[this.providerId() ?? ''] ?? null);
  /** The newest Data Through date the API has reported for the active Provider. */
  readonly latest = computed(() => this.latestByProvider()[this.providerId() ?? ''] ?? null);
  readonly updateAvailable = computed(() => {
    const shown = this.shown();
    const latest = this.latest();
    return shown !== null && latest !== null && latest > shown;
  });

  constructor() {
    const destroyRef = inject(DestroyRef);
    const onFocus = (): void => this.revalidate();
    const onVisible = (): void => {
      if (document.visibilityState === 'visible') this.revalidate();
    };
    window.addEventListener('focus', onFocus);
    document.addEventListener('visibilitychange', onVisible);
    destroyRef.onDestroy(() => {
      window.removeEventListener('focus', onFocus);
      document.removeEventListener('visibilitychange', onVisible);
    });
    this.router.events
      .pipe(
        filter((event) => event instanceof NavigationEnd),
        takeUntilDestroyed(destroyRef),
      )
      .subscribe(() => this.revalidate());
  }

  /** A Metric response was loaded for the page: remember the newest date seen. Never lowers a date already shown. */
  recordShown(providerId: string, dataThrough: string): void {
    if (!isIsoDate(dataThrough)) return;
    this.shownByProvider.update((map) => ((map[providerId] ?? '') >= dataThrough ? map : { ...map, [providerId]: dataThrough }));
    this.recordLatest(providerId, dataThrough);
  }

  private recordLatest(providerId: string, dataThrough: string): void {
    if (!isIsoDate(dataThrough)) return;
    this.latestByProvider.update((map) => ((map[providerId] ?? '') >= dataThrough ? map : { ...map, [providerId]: dataThrough }));
  }

  /** Asks for one cheap Metric (the list of periods; the browser revalidates it with its ETag, so an unchanged answer is a 304) and records the date. */
  revalidate(): void {
    const providerId = this.providerId();
    const now = Date.now();
    if (!providerId || this.shown() === null || this.inFlight || now - this.lastCheck < MIN_REVALIDATE_MS) return;
    this.inFlight = true;
    this.lastCheck = now;
    this.http
      .get<NetCollectionsPeriodsDto>(`${metricsBase(providerId)}/periods`, { context: new HttpContext().set(REVALIDATING, true) })
      .subscribe({
        next: (dto) => {
          this.inFlight = false;
          if (dto?.dataThrough) this.recordLatest(providerId, dto.dataThrough);
        },
        error: () => {
          // A failed check is not worth telling anyone about: the figures on screen are still what they were.
          this.inFlight = false;
        },
      });
  }

  /** The user chose to see the new figures. Returns the date they now show, or null when there was nothing to apply. */
  apply(): string | null {
    const providerId = this.providerId();
    const latest = this.latest();
    if (!providerId || !this.updateAvailable() || latest === null) return null;
    this.shownByProvider.update((map) => ({ ...map, [providerId]: latest }));
    this.revision.update((n) => n + 1);
    return latest;
  }
}

/** Passes every Metric response through to the freshness service, so the date on screen is learned from the figures, not from a second source. */
export function freshnessInterceptor(request: HttpRequest<unknown>, next: HttpHandlerFn): Observable<HttpEvent<unknown>> {
  const freshness = inject(DataFreshnessService);
  const match = METRIC_URL.exec(request.url);
  if (!match || request.context.get(REVALIDATING)) return next(request);
  const providerId = decodeURIComponent(match[1]!);
  return next(request).pipe(
    tap((event) => {
      if (!(event instanceof HttpResponse)) return;
      const body = event.body as { dataThrough?: unknown } | null;
      if (body && typeof body === 'object' && typeof body.dataThrough === 'string') freshness.recordShown(providerId, body.dataThrough);
    }),
  );
}
