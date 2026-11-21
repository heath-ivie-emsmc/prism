import { Router, UrlTree } from '@angular/router';
import { buildLoginQueryParams } from './return-url.util';

/** Builds a `/login` UrlTree that keeps a safe return target and the auth reason. */
export function loginUrlTree(router: Router, attemptedUrl: string): UrlTree {
  const queryParams = buildLoginQueryParams(attemptedUrl);
  return queryParams ? router.createUrlTree(['/login'], { queryParams }) : router.parseUrl('/login');
}
