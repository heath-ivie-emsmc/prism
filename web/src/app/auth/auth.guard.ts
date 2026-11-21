import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { map } from 'rxjs';
import { AuthService } from './auth.service';
import { loginUrlTree } from './login-redirect.util';

/** Lets signed-in users through and sends everyone else to `/login`, keeping a safe return target. */
export const authGuard: CanActivateFn = (_route, state) => {
  const router = inject(Router);
  return inject(AuthService)
    .ensureMeLoaded()
    .pipe(map((me) => (me !== null ? true : loginUrlTree(router, state.url))));
};

/** Keeps signed-in users off `/login`. */
export const anonymousOnlyGuard: CanActivateFn = () => {
  const router = inject(Router);
  return inject(AuthService)
    .ensureMeLoaded()
    .pipe(map((me) => (me === null ? true : router.parseUrl('/dashboard'))));
};
