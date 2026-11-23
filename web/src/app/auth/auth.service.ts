import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { Observable, catchError, finalize, of, shareReplay, tap } from 'rxjs';
import { BROWSER_NAVIGATE } from './browser-navigation';
import { MeDto } from './me.dto';
import { normalizeReturnUrl } from './return-url.util';

/** The browser holds no tokens: the API keeps the session in an HttpOnly cookie and `/api/v1/me` is the only identity source. */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly navigate = inject(BROWSER_NAVIGATE);

  private readonly meSignal = signal<MeDto | null>(null);
  private meInFlight$: Observable<MeDto | null> | null = null;
  /** True once the API has said this browser has no valid session (401 or 403), so guards do not ask again. */
  private knownAnonymous = false;
  /** Identifies the newest /api/v1/me request, so an older response cannot overwrite a newer one. */
  private meGeneration = 0;

  readonly me = this.meSignal.asReadonly();
  readonly isAuthenticated = computed(() => this.meSignal() !== null);
  /** The Provider whose data is shown. Until a switcher exists it is the first one the user was assigned. The one place that choice is made; the API checks it again on every request. */
  readonly activeProvider = computed(() => this.meSignal()?.providers[0] ?? null);
  readonly activeProviderId = computed(() => this.activeProvider()?.id ?? null);

  /** Always issues a new GET /api/v1/me. */
  refreshMe(): Observable<MeDto | null> {
    return this.requestMe();
  }

  /** Returns the cached profile, or joins a request already in flight, or issues one. */
  ensureMeLoaded(): Observable<MeDto | null> {
    const current = this.meSignal();
    if (current) return of(current);
    if (this.knownAnonymous) return of(null);
    return this.meInFlight$ ?? this.requestMe();
  }

  /** Full-page redirect to the API, which sends the user to the Account Portal. */
  beginSsoLogin(options?: { returnUrl?: string | null }): void {
    const params = new URLSearchParams();
    const safeReturn = normalizeReturnUrl(options?.returnUrl);
    if (safeReturn) params.set('returnUrl', safeReturn);
    const query = params.toString();
    this.navigate(query ? `/api/auth/sso/start?${query}` : '/api/auth/sso/start');
  }

  /** Clears local identity and lets the API end the session and sign out of the Account Portal. */
  logout(): void {
    this.meSignal.set(null);
    this.navigate('/api/auth/sso/logout');
  }

  /** The API rejected the session: forget the identity. The caller decides where to send the user. */
  clearSession(): void {
    this.meGeneration++;
    this.meSignal.set(null);
    this.knownAnonymous = true;
  }

  private requestMe(): Observable<MeDto | null> {
    const generation = ++this.meGeneration;
    const isLatest = (): boolean => generation === this.meGeneration;
    const request$: Observable<MeDto | null> = this.http.get<MeDto>('/api/v1/me').pipe(
      tap((me) => {
        if (!isLatest()) return;
        this.knownAnonymous = false;
        this.meSignal.set(me);
      }),
      catchError((err: unknown) => {
        if (isLatest()) {
          this.meSignal.set(null);
          // Only a definite "no session" is remembered. A network error or 5xx is retried on the next navigation.
          this.knownAnonymous = err instanceof HttpErrorResponse && (err.status === 401 || err.status === 403);
        }
        return of(null);
      }),
      finalize(() => {
        if (this.meInFlight$ === request$) this.meInFlight$ = null;
      }),
      shareReplay({ bufferSize: 1, refCount: true }),
    );
    this.meInFlight$ = request$;
    return request$;
  }
}
