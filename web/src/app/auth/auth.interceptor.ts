import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';
import { AuthService } from './auth.service';
import { loginUrlTree } from './login-redirect.util';

/**
 * The SPA is same-origin with the API, so only relative URLs are allowed. The XSRF header comes from Angular's
 * built-in double-submit support (`XSRF-TOKEN` cookie, `X-XSRF-TOKEN` header on state-changing requests).
 * A 401 from any call except the identity probe ends the local session and sends the user to sign in.
 */
const IDENTITY_PROBE = '/api/v1/me';

/** The identity probe itself, with or without a query. Not a prefix match: `/api/v1/metrics` is a different route. */
function isIdentityProbe(url: string): boolean {
  return url === IDENTITY_PROBE || url.startsWith(`${IDENTITY_PROBE}?`);
}

export const authInterceptor: HttpInterceptorFn = (req, next) => {
  if (req.url.startsWith('//') || /^[a-zA-Z][a-zA-Z0-9+.-]*:/.test(req.url)) {
    return throwError(() => new Error('Only same-origin relative request URLs are allowed'));
  }

  const auth = inject(AuthService);
  const router = inject(Router);

  return next(req).pipe(
    catchError((err: unknown) => {
      if (err instanceof HttpErrorResponse && err.status === 401 && !isIdentityProbe(req.url)) {
        auth.clearSession();
        void router.navigateByUrl(loginUrlTree(router, router.url));
      }
      return throwError(() => err);
    }),
  );
};
