import { inject } from '@angular/core';
import { CanActivateChildFn, CanActivateFn, Router } from '@angular/router';
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

/** Keeps everyone but Internal Users out of the admin area. This only saves a click: the admin API refuses anyone else on its own. */
export const internalGuard: CanActivateFn = () => {
  const router = inject(Router);
  return inject(AuthService)
    .ensureMeLoaded()
    .pipe(map((me) => (me?.isInternal === true ? true : router.parseUrl('/dashboard'))));
};

/** Sends a user who has not answered the first-login questions to them, before any other page. The server decides who needs them; this only routes. */
export const onboardingGuard: CanActivateChildFn = (_route, state) => {
  const router = inject(Router);
  const auth = inject(AuthService);
  return auth.ensureMeLoaded().pipe(map(() => (auth.onboardingRequired() && !state.url.startsWith('/welcome') ? router.parseUrl('/welcome') : true)));
};

/** Keeps users who do not need the questionnaire off it. */
export const welcomeGuard: CanActivateFn = () => {
  const router = inject(Router);
  const auth = inject(AuthService);
  return auth.ensureMeLoaded().pipe(map(() => (auth.onboardingRequired() ? true : router.parseUrl('/dashboard'))));
};

/**
 * Where "/" goes. An Internal User has no customer Providers of their own, so the dashboard has nothing to show them: they start in the admin area.
 * Everyone else starts on the dashboard.
 */
export const entryGuard: CanActivateFn = () => {
  const router = inject(Router);
  return inject(AuthService)
    .ensureMeLoaded()
    .pipe(map((me) => router.parseUrl(me?.isInternal === true && me.providers.length === 0 ? '/admin' : '/dashboard')));
};
